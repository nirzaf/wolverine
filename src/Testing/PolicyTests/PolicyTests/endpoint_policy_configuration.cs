using Baseline;
using IntegrationTests;
using Marten;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using TestingSupport;
using TestMessages;
using Wolverine;
using Wolverine.Configuration;
using Wolverine.Marten;
using Wolverine.RabbitMQ;
using Wolverine.Runtime;
using Wolverine.Transports;
using Wolverine.Transports.Local;
using Wolverine.Transports.Tcp;
using Wolverine.Util;

namespace PolicyTests;

public class endpoint_policy_configuration : IDisposable
{
    private IHost _host;

    private async Task<WolverineOptions> UsingOptions(Action<WolverineOptions> configure)
    {
        _host = await Host.CreateDefaultBuilder()
            .UseWolverine(opts =>
            {
                opts.Services.AddMarten(Servers.PostgresConnectionString)
                    .IntegrateWithWolverine();

                configure(opts);
            }).StartAsync();

        return _host.Services.GetRequiredService<IWolverineRuntime>().Options;
    }

    public void Dispose()
    {
        _host.Dispose();
    }

    [Fact]
    public async Task make_all_local_queues_durable()
    {
        var options = await UsingOptions(opts =>
        {
            opts.LocalQueue("one");
            opts.LocalQueue("two");
            opts.LocalQueue("three");

            opts.ListenAtPort(PortFinder.GetAvailablePort());
            opts.ListenAtPort(PortFinder.GetAvailablePort());
            
            opts.Policies.UseDurableLocalQueues();
        });

        options.Transports.AllEndpoints().OfType<LocalQueueSettings>()
            .Each(x => x.Mode.ShouldBe(EndpointMode.Durable));

        options.Transports.AllEndpoints().OfType<TcpEndpoint>()
            .Each(x => x.Mode.ShouldBe(EndpointMode.BufferedInMemory));
    }

    [Fact]
    public async Task make_all_outgoing_endpoints_durable()
    {
        var options = await UsingOptions(opts =>
        {
            opts.LocalQueue("one");
            opts.LocalQueue("two");
            
            opts.ListenToRabbitQueue("one");
            opts.ListenToRabbitQueue("two");

            opts.ListenAtPort(PortFinder.GetAvailablePort());

            opts.PublishAllMessages().ToPort(PortFinder.GetAvailablePort());
            opts.PublishAllMessages().ToPort(PortFinder.GetAvailablePort());

            opts.PublishAllMessages().ToRabbitExchange("outgoing");

            opts.UseRabbitMq().AutoProvision();
            
            opts.Policies.UseDurableOutboxOnAllSendingEndpoints();
        });

        // Don't touch local endpoints
        options.Transports.AllEndpoints().OfType<LocalQueueSettings>()
            .Where(x => x.Uri != TransportConstants.DurableLocalUri)
            .Each(x => x.Mode.ShouldBe(EndpointMode.BufferedInMemory));


        options.Transports.AllEndpoints().Each(e =>
        {
            if (e is LocalQueueSettings) return;

            if (e.IsListener)
            {
                e.Mode.ShouldNotBe(EndpointMode.Durable);
            }
            else
            {
                e.Mode.ShouldBe(EndpointMode.Durable);
            }
        });
    }
    
    [Fact]
    public async Task make_all_incoming_endpoints_durable()
    {
        var options = await UsingOptions(opts =>
        {
            opts.LocalQueue("one");
            opts.LocalQueue("two");
            
            opts.ListenToRabbitQueue("one");
            opts.ListenToRabbitQueue("two");

            opts.ListenAtPort(PortFinder.GetAvailablePort());

            opts.PublishAllMessages().ToPort(PortFinder.GetAvailablePort());
            opts.PublishAllMessages().ToPort(PortFinder.GetAvailablePort());

            opts.PublishAllMessages().ToRabbitExchange("outgoing");

            opts.UseRabbitMq().AutoProvision();
            
            opts.Policies.UseDurableInboxOnAllListeners();
        });

        // Don't touch local endpoints
        options.Transports.AllEndpoints().OfType<LocalQueueSettings>()
            .Where(x => x.Uri != TransportConstants.DurableLocalUri)
            .Each(x => x.Mode.ShouldBe(EndpointMode.BufferedInMemory));


        options.Transports.AllEndpoints().Each(e =>
        {
            if (e is LocalQueueSettings) return;

            if (e.IsListener)
            {
                e.Mode.ShouldBe(EndpointMode.Durable);
            }
            else
            {
                e.Mode.ShouldNotBe(EndpointMode.Durable);
            }
        });
    }

    [Fact]
    public async Task apply_to_rabbit_mq_listeners()
    {
        var options = await UsingOptions(opts =>
        {
            opts.ListenAtPort(PortFinder.GetAvailablePort());
            opts.PublishAllMessages().ToPort(PortFinder.GetAvailablePort());

            opts.UseRabbitMq().AutoProvision()
                .ConfigureSenders(x => x.UseDurableOutbox());

            opts.ListenToRabbitQueue("one");
            opts.PublishAllMessages().ToRabbitExchange("ex2");
        });
        
        options.Transports.GetOrCreateEndpoint("rabbitmq://queue/one".ToUri())
            .Mode.ShouldBe(EndpointMode.Inline);
        
        options.Transports.GetOrCreateEndpoint("rabbitmq://exchange/ex2".ToUri())
            .Mode.ShouldBe(EndpointMode.Durable);

        foreach (var endpoint in options.Transports.AllEndpoints().OfType<TcpEndpoint>())
        {
            endpoint.Mode.ShouldBe(EndpointMode.BufferedInMemory);
        }

    }
    
    [Fact]
    public async Task apply_to_rabbit_mq_senders()
    {
        var options = await UsingOptions(opts =>
        {
            opts.ListenAtPort(PortFinder.GetAvailablePort());
            opts.PublishAllMessages().ToPort(PortFinder.GetAvailablePort());

            opts.UseRabbitMq().AutoProvision()
                .ConfigureSenders(x => x.UseDurableOutbox());

            opts.ListenToRabbitQueue("one");
            opts.PublishAllMessages().ToRabbitExchange("ex2");
        });
        
        options.Transports.GetOrCreateEndpoint("rabbitmq://queue/one".ToUri())
            .Mode.ShouldBe(EndpointMode.Inline);
        
        options.Transports.GetOrCreateEndpoint("rabbitmq://exchange/ex2".ToUri())
            .Mode.ShouldBe(EndpointMode.Durable);

        foreach (var endpoint in options.Transports.AllEndpoints().OfType<TcpEndpoint>())
        {
            endpoint.Mode.ShouldBe(EndpointMode.BufferedInMemory);
        }

    }

    [Fact]
    public async Task discover_local_endpoints_with_default_name_pattern()
    {
        var options = await UsingOptions(opts =>
        {
            opts.Policies.UseConventionalLocalRouting()
                .CustomizeQueues((type, listener) =>
                {
                    listener.UseDurableInbox();
                });
        });

        var runtime = _host.Services.GetRequiredService<IWolverineRuntime>()
            .ShouldBeOfType<WolverineRuntime>();

        var endpoint1 = runtime.DetermineLocalSendingAgent(typeof(Message1))
            .Endpoint.ShouldBeOfType<LocalQueueSettings>();
        endpoint1.EndpointName.ShouldBe(typeof(Message1).ToMessageTypeName().ToLowerInvariant());
        endpoint1.Mode.ShouldBe(EndpointMode.Durable);

        var endpoint2 = runtime.DetermineLocalSendingAgent(typeof(Message2))
            .Endpoint.ShouldBeOfType<LocalQueueSettings>();
        endpoint2.EndpointName.ShouldBe(typeof(Message2).ToMessageTypeName().ToLowerInvariant());
        endpoint2.Mode.ShouldBe(EndpointMode.Durable);
        
        runtime.DetermineLocalSendingAgent(typeof(Message3))
            .Endpoint.ShouldBeOfType<LocalQueueSettings>().EndpointName.ShouldBe(typeof(Message3).ToMessageTypeName().ToLowerInvariant());
        
        runtime.DetermineLocalSendingAgent(typeof(Message4))
            .Endpoint.ShouldBeOfType<LocalQueueSettings>().EndpointName.ShouldBe(typeof(Message4).ToMessageTypeName().ToLowerInvariant());
    }
    
    [Fact]
    public async Task discover_local_endpoints_exclude_some_types()
    {
        var options = await UsingOptions(opts =>
        {
            opts.Policies.UseConventionalLocalRouting()
                .ExcludeTypes(t => t == typeof(Message1))
                .CustomizeQueues((type, listener) =>
                {
                    listener.UseDurableInbox();
                    
                });
        });

        var runtime = _host.Services.GetRequiredService<IWolverineRuntime>()
            .ShouldBeOfType<WolverineRuntime>();

        var endpoint1 = runtime.DetermineLocalSendingAgent(typeof(Message1))
            .Endpoint.ShouldBeOfType<LocalQueueSettings>();
        endpoint1.EndpointName.ShouldBe(TransportConstants.Default);
        endpoint1.Mode.ShouldBe(EndpointMode.BufferedInMemory);

        var endpoint2 = runtime.DetermineLocalSendingAgent(typeof(Message2))
            .Endpoint.ShouldBeOfType<LocalQueueSettings>();
        endpoint2.EndpointName.ShouldBe(typeof(Message2).ToMessageTypeName().ToLowerInvariant());
        endpoint2.Mode.ShouldBe(EndpointMode.Durable);
        
        runtime.DetermineLocalSendingAgent(typeof(Message3))
            .Endpoint.ShouldBeOfType<LocalQueueSettings>().EndpointName.ShouldBe(typeof(Message3).ToMessageTypeName().ToLowerInvariant());
        
        runtime.DetermineLocalSendingAgent(typeof(Message4))
            .Endpoint.ShouldBeOfType<LocalQueueSettings>().EndpointName.ShouldBe(typeof(Message4).ToMessageTypeName().ToLowerInvariant());
    }
    
    [Fact]
    public async Task discover_local_endpoints_include_some_types()
    {
        var options = await UsingOptions(opts =>
        {
            opts.Policies.UseConventionalLocalRouting()
                .IncludeTypes(t => t != typeof(Message1))
                .CustomizeQueues((type, listener) =>
                {
                    listener.UseDurableInbox();
                    
                });
        });

        var runtime = _host.Services.GetRequiredService<IWolverineRuntime>()
            .ShouldBeOfType<WolverineRuntime>();

        var endpoint1 = runtime.DetermineLocalSendingAgent(typeof(Message1))
            .Endpoint.ShouldBeOfType<LocalQueueSettings>();
        endpoint1.EndpointName.ShouldBe(TransportConstants.Default);
        endpoint1.Mode.ShouldBe(EndpointMode.BufferedInMemory);

        var endpoint2 = runtime.DetermineLocalSendingAgent(typeof(Message2))
            .Endpoint.ShouldBeOfType<LocalQueueSettings>();
        endpoint2.EndpointName.ShouldBe(typeof(Message2).ToMessageTypeName().ToLowerInvariant());
        endpoint2.Mode.ShouldBe(EndpointMode.Durable);
        
        runtime.DetermineLocalSendingAgent(typeof(Message3))
            .Endpoint.ShouldBeOfType<LocalQueueSettings>().EndpointName.ShouldBe(typeof(Message3).ToMessageTypeName().ToLowerInvariant());
        
        runtime.DetermineLocalSendingAgent(typeof(Message4))
            .Endpoint.ShouldBeOfType<LocalQueueSettings>().EndpointName.ShouldBe(typeof(Message4).ToMessageTypeName().ToLowerInvariant());
    }
    
    [Fact]
    public async Task discover_local_endpoints_with_custom_name_pattern()
    {
        var options = await UsingOptions(opts =>
        {
            opts.Policies.UseConventionalLocalRouting()
                .Named(t => t.ToMessageTypeName() + "_more")
                .CustomizeQueues((type, listener) =>
                {
                    listener.UseDurableInbox();
                });
        });

        var runtime = _host.Services.GetRequiredService<IWolverineRuntime>()
            .ShouldBeOfType<WolverineRuntime>();

        var endpoint1 = runtime.DetermineLocalSendingAgent(typeof(Message1))
            .Endpoint.ShouldBeOfType<LocalQueueSettings>();
        endpoint1.EndpointName.ShouldBe(typeof(Message1).ToMessageTypeName().ToLowerInvariant()+ "_more");
        endpoint1.Mode.ShouldBe(EndpointMode.Durable);

        var endpoint2 = runtime.DetermineLocalSendingAgent(typeof(Message2))
            .Endpoint.ShouldBeOfType<LocalQueueSettings>();
        endpoint2.EndpointName.ShouldBe(typeof(Message2).ToMessageTypeName().ToLowerInvariant()+ "_more");
        endpoint2.Mode.ShouldBe(EndpointMode.Durable);
        
        runtime.DetermineLocalSendingAgent(typeof(Message3))
            .Endpoint.ShouldBeOfType<LocalQueueSettings>().EndpointName.ShouldBe(typeof(Message3).ToMessageTypeName().ToLowerInvariant()+ "_more");
        
        runtime.DetermineLocalSendingAgent(typeof(Message4))
            .Endpoint.ShouldBeOfType<LocalQueueSettings>().EndpointName.ShouldBe(typeof(Message4).ToMessageTypeName().ToLowerInvariant()+ "_more");
    }
}


public class MessageHandler
{
    public void Handle(TestMessages.Message1 message)
    {
        
    }
    
    public void Handle(TestMessages.Message2 message)
    {
        
    }
    
    public void Handle(TestMessages.Message3 message)
    {
        
    }
    
    public void Handle(TestMessages.Message4 message)
    {
        
    }
}