using System.Threading.Tasks;
using Shouldly;
using TestingSupport;
using TestMessages;
using Wolverine.Tracking;
using Wolverine.Transports.Tcp;
using Xunit;

namespace CoreTests.Transports.Tcp;

public class using_stubbed_listeners
{
    #region sample_using_stubbed_listeners

    [Fact]
    public async Task track_outgoing_to_tcp_when_stubbed()
    {
        using var host = WolverineHost.For(options =>
        {
            options.PublishAllMessages().ToPort(7777);
            options.StubAllExternallyOutgoingEndpoints();
        });

        var message = new Message1();

        // The session can be interrogated to see
        // what activity happened while the tracking was
        // ongoing
        var session = await host.SendMessageAndWaitAsync(message);

        session.FindSingleTrackedMessageOfType<Message1>(EventType.Sent)
            .ShouldBeSameAs(message);
    }

    #endregion
}
