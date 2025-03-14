using System;
using System.Linq;
using System.Threading.Tasks;
using Baseline.Dates;
using MassTransit;
using MassTransitService;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Wolverine.Tracking;
using Xunit;
using IHost = Microsoft.Extensions.Hosting.IHost;

namespace Wolverine.RabbitMQ.Tests.Interop.MassTransit
{
    public class MassTransitFixture : IAsyncLifetime
    {
        private IHost _massTransit;

        public async Task InitializeAsync()
        {
            #region sample_MassTransit_interoperability

            Wolverine = await Host.CreateDefaultBuilder().UseWolverine(opts =>
            {
                opts.ApplicationAssembly = GetType().Assembly;
                
                opts.UseRabbitMq()
                    .AutoProvision().AutoPurgeOnStartup()
                    .BindExchange("wolverine").ToQueue("wolverine")
                    .BindExchange("masstransit").ToQueue("masstransit");

                opts.PublishAllMessages().ToRabbitExchange("masstransit")

                    // Tell Wolverine to make this endpoint send messages out in a format
                    // for MassTransit
                    .UseMassTransitInterop();

                opts.ListenToRabbitQueue("wolverine")

                    // Tell Wolverine to make this endpoint interoperable with MassTransit
                    .UseMassTransitInterop(mt =>
                    {
                        // optionally customize the inner JSON serialization
                    })
                    .DefaultIncomingMessage<ResponseMessage>().UseForReplies();

            }).StartAsync();

                #endregion

            _massTransit = await MassTransitService.Program.CreateHostBuilder(Array.Empty<string>())
                .StartAsync();
        }

        public IHost MassTransit => _massTransit;

        public IHost Wolverine { get; private set; }

        public async Task DisposeAsync()
        {
            await Wolverine.StopAsync();
            await _massTransit.StopAsync();

        }
    }

    public class MassTransitSpecs : IClassFixture<MassTransitFixture>
    {
        private readonly MassTransitFixture theFixture;

        public MassTransitSpecs(MassTransitFixture fixture)
        {
            theFixture = fixture;
        }

        [Fact]
        public async Task masstransit_sends_message_to_wolverine()
        {
            ResponseHandler.Received.Clear();

            var id = Guid.NewGuid();


            var session = await theFixture.Wolverine.ExecuteAndWaitAsync(async () =>
            {
                var sender = theFixture.MassTransit.Services.GetRequiredService<ISendEndpointProvider>();
                var endpoint = await sender.GetSendEndpoint(new Uri("rabbitmq://localhost/wolverine"));
                await endpoint.Send(new ResponseMessage {Id = id});
            }, 60000);

            var envelope = ResponseHandler.Received.FirstOrDefault();
            envelope.Message.ShouldBeOfType<ResponseMessage>().Id.ShouldBe(id);
            envelope.ShouldNotBeNull();
            
            envelope.Id.ShouldNotBe(Guid.Empty);
            envelope.ConversationId.ShouldNotBe(Guid.Empty);
            
        }

        [Fact]
        public async Task wolverine_sends_message_to_masstransit_that_then_responds()
        {
            ResponseHandler.Received.Clear();

            var id = Guid.NewGuid();

            var session = await theFixture.Wolverine.TrackActivity().Timeout(10.Minutes())
                .WaitForMessageToBeReceivedAt<ResponseMessage>(theFixture.Wolverine)
                .SendMessageAndWaitAsync(new InitialMessage {Id = id});

            ResponseHandler.Received
                .Select(x => x.Message)
                .OfType<ResponseMessage>()
                .Any(x => x.Id == id)
                .ShouldBeTrue();
      }


    }


}
