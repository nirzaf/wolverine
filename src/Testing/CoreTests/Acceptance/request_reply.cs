using System;
using System.Linq;
using System.Threading.Tasks;
using Baseline.Dates;
using Lamar;
using Microsoft.Extensions.Hosting;
using TestingSupport;
using Wolverine.Attributes;
using Wolverine.Runtime.ResponseReply;
using Wolverine.Runtime.Routing;
using Wolverine.Tracking;
using Wolverine.Transports.Tcp;
using Xunit;

namespace CoreTests.Acceptance;

public class request_reply : IAsyncLifetime
{
    private IHost _receiver1;

    private IHost _receiver2;

    private IHost _sender;

    private int _receiver1Port;

    private int _receiver2Port;

    public async Task InitializeAsync()
    {
        var senderPort = PortFinder.GetAvailablePort();
        _receiver1Port = PortFinder.GetAvailablePort();
        _receiver2Port = PortFinder.GetAvailablePort();
        
        _receiver1 = await Host.CreateDefaultBuilder()
            .UseWolverine(opts =>
            {
                opts.ServiceName = "Receiver1";
                opts.ListenAtPort(_receiver1Port);
            }).StartAsync();
        
        _receiver2 = await Host.CreateDefaultBuilder()
            .UseWolverine(opts =>
            {
                opts.ServiceName = "Receiver2";
                opts.ListenAtPort(_receiver2Port);
            }).StartAsync();
        
        _sender = await Host.CreateDefaultBuilder()
            .UseWolverine(opts =>
            {
                opts.Handlers.DisableConventionalDiscovery();
                opts.ServiceName = "Sender";
                opts.ListenAtPort(senderPort);

                opts.PublishMessage<Request1>().ToPort(_receiver1Port).Named("Receiver1");
                opts.PublishMessage<Request2>().ToPort(_receiver1Port).Named("Receiver1");
                opts.PublishMessage<Request3>().ToPort(_receiver2Port).Named("Receiver2");

                opts.PublishMessage<Request4>().ToPort(_receiver1Port);
                opts.PublishMessage<Request4>().ToPort(_receiver2Port);
            }).StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _receiver1.StopAsync();
        await _receiver2.StopAsync();
        await _sender.StopAsync();
    }
    
    /*

     * Pass CancellationToken through to ReplyListener
     */

    [Fact]
    public async Task request_reply_with_no_reply()
    {
        using var nested = _sender.Get<IContainer>().GetNestedContainer();
        var publisher = nested.GetInstance<IMessagePublisher>();
        
        var ex = await Should.ThrowAsync<WolverineRequestReplyException>(async () =>
        {
            await publisher.RequestAsync<Response1>(new Request3());
        });
        
        ex.Message.ShouldContain("Request failed: No response was created for expected response 'CoreTests.Acceptance.Response1'");
    }
    
    [Fact]
    public async Task send_and_wait_with_multiple_subscriptions()
    {
        using var nested = _sender.Get<IContainer>().GetNestedContainer();
        var publisher = nested.GetInstance<IMessagePublisher>();
        
        var ex = await Should.ThrowAsync<InvalidOperationException>(async () =>
        {
            await publisher.SendAndWaitAsync(new Request4());
        });
        
        ex.Message.ShouldContain("There are multiple subscribing endpoints");

    }

    [Fact]
    public async Task happy_path_with_auto_routing()
    {
        var (session, response) = await _sender.TrackActivity()
            .AlsoTrack(_receiver1, _receiver2)
            .Timeout(5.Seconds())
            .RequestAndWaitAsync(c => c.RequestAsync<Response1>(new Request1 { Name = "Croaker" }));

        var send = session.FindEnvelopesWithMessageType<Request1>()
            .Single(x => x.EventType == EventType.Sent);

        send.Envelope.DeliverBy.ShouldNotBeNull();
        
        var envelope = session.Received.SingleEnvelope<Response1>();
        envelope.Source.ShouldBe("Receiver1");
        envelope.Message.ShouldBe(response);
        
        response.Name.ShouldBe("Croaker");
    }

    [Fact]
    public async Task sad_path_request_reply_no_subscriptions()
    {
        using var nested = _sender.Get<IContainer>().GetNestedContainer();
        var publisher = nested.GetInstance<IMessagePublisher>();
        
        await Should.ThrowAsync<NoRoutesException>(async () =>
        {
            await publisher.RequestAsync<Response1>(new RequestWithNoHandler());
        });
    }
    
    [Fact]
    public async Task happy_path_with_explicit_endpoint_name()
    {
        var (session, response) = await _sender.TrackActivity()
            .AlsoTrack(_receiver1, _receiver2)
            .Timeout(5.Seconds())
            .RequestAndWaitAsync(c => c.RequestAsync<Response1>("Receiver2", new Request1 { Name = "Croaker" }));

        var send = session.FindEnvelopesWithMessageType<Request1>()
            .Single(x => x.EventType == EventType.Sent);

        send.Envelope.DeliverBy.ShouldNotBeNull();
        
        var envelope = session.Received.SingleEnvelope<Response1>();
        envelope.Source.ShouldBe("Receiver2");
        envelope.Message.ShouldBe(response);
        
        response.Name.ShouldBe("Croaker");
    }
    
        
    [Fact]
    public async Task happy_path_with_explicit_uri_destination()
    {
        var destination = new Uri("tcp://localhost:" + _receiver2Port);
        
        var (session, response) = await _sender.TrackActivity()
            .AlsoTrack(_receiver1, _receiver2)
            .Timeout(5.Seconds())
            .RequestAndWaitAsync(c => c.RequestAsync<Response1>(destination, new Request1 { Name = "Croaker" }));

        var send = session.FindEnvelopesWithMessageType<Request1>()
            .Single(x => x.EventType == EventType.Sent);

        send.Envelope.DeliverBy.ShouldNotBeNull();
        
        var envelope = session.Received.SingleEnvelope<Response1>();
        envelope.Source.ShouldBe("Receiver2");
        envelope.Message.ShouldBe(response);
        
        response.Name.ShouldBe("Croaker");
    }

    [Fact]
    public async Task sad_path_with_auto_routing()
    {
        var ex = await Should.ThrowAsync<WolverineRequestReplyException>(async () =>
        {
            var (session, response) = await _sender.TrackActivity()
                .AlsoTrack(_receiver1, _receiver2)
                .Timeout(5.Seconds())
                .DoNotAssertOnExceptionsDetected()
                // This message is rigged to fail
                .RequestAndWaitAsync(c => c.RequestAsync<Response1>(new Request1 { Name = "Soulcatcher" }));

        });
            
        ex.Message.ShouldContain("Request failed");
        ex.Message.ShouldContain("System.Exception: You shall not pass!");
    }
    
        
    [Fact]
    public async Task timeout_with_auto_routing()
    {
        using var nested = _sender.Get<IContainer>().GetNestedContainer();
        var publisher = nested.GetInstance<IMessagePublisher>();
        
        var ex = await Should.ThrowAsync<TimeoutException>(async () =>
        {
            await publisher.RequestAsync<Response1>(new Request1 { Name = "SLOW" });
        });
        
        ex.Message.ShouldContain("Timed out waiting for expected response CoreTests.Acceptance.Response1");
    }
    
    [Fact]
    public async Task happy_path_send_and_wait_with_auto_routing()
    {
        var (session, ack) = await _sender.TrackActivity()
            .AlsoTrack(_receiver1, _receiver2)
            .Timeout(5.Seconds())
            .SendMessageAndWaitForAcknowledgementAsync(c => c.SendAndWaitAsync(new Request2 { Name = "Croaker" }));

        var send = session.FindEnvelopesWithMessageType<Request2>()
            .Single(x => x.EventType == EventType.Sent);

        send.Envelope.DeliverBy.ShouldNotBeNull();
        
        var envelope = session.Received.SingleEnvelope<Acknowledgement>();
        envelope.Source.ShouldBe("Receiver1");
    }
    
    [Fact]
    public async Task happy_path_send_and_wait_to_specific_endpoint()
    {
        var (session, ack) = await _sender.TrackActivity()
            .AlsoTrack(_receiver1, _receiver2)
            .Timeout(5.Seconds())
            .SendMessageAndWaitForAcknowledgementAsync(c => c.SendAndWaitAsync("Receiver2", new Request2 { Name = "Croaker" }));

        var send = session.FindEnvelopesWithMessageType<Request2>()
            .Single(x => x.EventType == EventType.Sent);

        send.Envelope.DeliverBy.ShouldNotBeNull();
        
        var envelope = session.Received.SingleEnvelope<Acknowledgement>();
        envelope.Source.ShouldBe("Receiver2");
    }
    
    [Fact]
    public async Task happy_path_send_and_wait_to_specific_endpoint_by_uri()
    {
        var destination = new Uri("tcp://localhost:" + _receiver2Port);
        
        var (session, ack) = await _sender.TrackActivity()
            .AlsoTrack(_receiver1, _receiver2)
            .Timeout(5.Seconds())
            .SendMessageAndWaitForAcknowledgementAsync(c => c.SendAndWaitAsync(destination, new Request2 { Name = "Croaker" }));

        var send = session.FindEnvelopesWithMessageType<Request2>()
            .Single(x => x.EventType == EventType.Sent);

        send.Envelope.DeliverBy.ShouldNotBeNull();
        
        var envelope = session.Received.SingleEnvelope<Acknowledgement>();
        envelope.Source.ShouldBe("Receiver2");
    }
    
    [Fact]
    public async Task sad_path_send_and_wait_for_acknowledgement_with_auto_routing()
    {
        var ex = await Should.ThrowAsync<WolverineRequestReplyException>(async () =>
        {
            var (session, response) = await _sender.TrackActivity()
                .AlsoTrack(_receiver1, _receiver2)
                .Timeout(5.Seconds())
                .DoNotAssertOnExceptionsDetected()
                // This message is rigged to fail
                .SendMessageAndWaitForAcknowledgementAsync(c => c.SendAndWaitAsync(new Request2 { Name = "Limper" }));

        });
            
        ex.Message.ShouldContain("Request failed");
        ex.Message.ShouldContain("System.Exception: You shall not pass!");
    }
    
    [Fact]
    public async Task timeout_on_send_and_wait_with_auto_routing()
    {
        using var nested = _sender.Get<IContainer>().GetNestedContainer();
        var publisher = nested.GetInstance<IMessagePublisher>();
        
        var ex = await Should.ThrowAsync<TimeoutException>(async () =>
        {
            await publisher.SendAndWaitAsync(new Request2 { Name = "SLOW" });
        });
        
        ex.Message.ShouldContain("Timed out waiting for expected acknowledgement for original message");
    }
    
    [Fact]
    public async Task sad_path_request_and_reply_with_no_handler()
    {
        using var nested = _sender.Get<IContainer>().GetNestedContainer();
        var publisher = nested.GetInstance<IMessagePublisher>();

        var ex = await Should.ThrowAsync<WolverineRequestReplyException>(async () =>
        {
            await publisher.RequestAsync<Response1>("Receiver1", new RequestWithNoHandler());
        });
        
        ex.Message.ShouldContain("No known message handler for message type 'CoreTests.Acceptance.RequestWithNoHandler'");
    }

    [Fact]
    public async Task sad_path_send_and_wait_with_no_handler()
    {
        using var nested = _sender.Get<IContainer>().GetNestedContainer();
        var publisher = nested.GetInstance<IMessagePublisher>();

        var ex = await Should.ThrowAsync<WolverineRequestReplyException>(async () =>
        {
            await publisher.SendAndWaitAsync("Receiver1", new RequestWithNoHandler());
        });
        
        ex.Message.ShouldContain("No known message handler for message type 'CoreTests.Acceptance.RequestWithNoHandler'");
    }

    [Fact]
    public async Task sad_path_send_and_wait_with_no_subscription()
    {
        using var nested = _sender.Get<IContainer>().GetNestedContainer();
        var publisher = nested.GetInstance<IMessagePublisher>();

        await Should.ThrowAsync<NoRoutesException>(() => publisher.SendAndWaitAsync(new RequestWithNoHandler()));
    }
}

public class Request1
{
    public string Name { get; set; }
}

public class Request2
{
    public string Name { get; set; }
}

public class Request3
{
    public string Name { get; set; }
}

public class Request4
{
    public string Name { get; set; }
}

public class RequestWithNoHandler
{
    
}

public class Response1
{
    public string Name { get; set; }
}

public class Response3
{
    public string Name { get; set; }
}

public class RequestHandler
{
    [MessageTimeout(3)]
    public async Task<Response1> Handle(Request1 request)
    {
        if (request.Name == "Soulcatcher")
        {
            throw new Exception("You shall not pass!");
        }

        if (request.Name == "SLOW")
        {
            await Task.Delay(5.Seconds());
        }
        
        return new Response1 { Name = request.Name };
    }

    [MessageTimeout(3)]
    public async Task Handle(Request2 request)
    {
        if (request.Name == "Limper")
        {
            throw new Exception("You shall not pass!");
        }
        
        if (request.Name == "SLOW")
        {
            await Task.Delay(6.Seconds());
        }
    }
    public Response3 Handle(Request3 request)
    {
        return new Response3 { Name = request.Name };
    }
}