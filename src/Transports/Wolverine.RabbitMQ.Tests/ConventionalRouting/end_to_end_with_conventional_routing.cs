using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Shouldly;
using TestingSupport;
using Wolverine.Tracking;
using Xunit;

namespace Wolverine.RabbitMQ.Tests.ConventionalRouting
{
    public class end_to_end_with_conventional_routing : IDisposable
    {
        private readonly IHost _sender;
        private readonly IHost _receiver;

        public end_to_end_with_conventional_routing()
        {
            _sender = WolverineHost.For(opts =>
            {
                opts.UseRabbitMq().UseConventionalRouting().AutoProvision().AutoPurgeOnStartup();
                opts.Handlers.DisableConventionalDiscovery();
                opts.ServiceName = "Sender";
            });

            _receiver = WolverineHost.For(opts =>
            {
                opts.UseRabbitMq().UseConventionalRouting().AutoProvision().AutoPurgeOnStartup();
                opts.ServiceName = "Receiver";
            });
        }

        public void Dispose()
        {
            _sender?.Dispose();
            _receiver?.Dispose();
        }

        [Fact]
        public async Task send_from_one_node_to_another_all_with_conventional_routing()
        {
            var session = await _sender.TrackActivity()
                .AlsoTrack(_receiver)
                .IncludeExternalTransports()
                .SendMessageAndWaitAsync(new RoutedMessage());

            var received = session
                .AllRecordsInOrder()
                .Where(x => x.Envelope.Message?.GetType() == typeof(RoutedMessage))
                .Single(x => x.EventType == EventType.Received);

            received
                .ServiceName.ShouldBe("Receiver");

        }
    }
}
