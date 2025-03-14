using System;
using System.Linq;
using Baseline.Dates;
using Baseline.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RabbitMQ.Client;
using Shouldly;
using Wolverine.RabbitMQ.Internal;
using Xunit;

namespace Wolverine.RabbitMQ.Tests.Internals
{
    public class RabbitMqQueueTests
    {
        private readonly RabbitMqTransport theTransport = new RabbitMqTransport
        {
            
        };
        private readonly IModel theChannel = Substitute.For<IModel>();
        
        [Fact]
        public void defaults()
        {
            var queue = new RabbitMqQueue("foo", new RabbitMqTransport());

            queue.EndpointName.ShouldBe("foo");
            queue.IsDurable.ShouldBeTrue();
            queue.IsExclusive.ShouldBeFalse();
            queue.AutoDelete.ShouldBeFalse();
            queue.Arguments.Any().ShouldBeFalse();
        }

        [Fact]
        public void set_time_to_live()
        {
            var queue = new RabbitMqQueue("foo", new RabbitMqTransport());
            queue.TimeToLive(3.Minutes());
            queue.Arguments["x-message-ttl"].ShouldBe(180000);
        }

        [Fact]
        public void uri_construction()
        {
            var queue = new RabbitMqQueue("foo", new RabbitMqTransport());
            queue.Uri.ShouldBe(new Uri("rabbitmq://queue/foo"));
        }

        [Theory]
        [InlineData(true, false, false)]
        [InlineData(false, true, false)]
        [InlineData(false, false, true)]
        public void declare(bool autoDelete, bool isExclusive, bool isDurable)
        {
            var queue = new RabbitMqQueue("foo", new RabbitMqTransport())
            {
                AutoDelete = autoDelete,
                IsExclusive = isExclusive,
                IsDurable = isDurable,
            };

            queue.HasDeclared.ShouldBeFalse();

            var channel = Substitute.For<IModel>();
            queue.Declare(channel, NullLogger.Instance);

            channel.Received()
                .QueueDeclare("foo", queue.IsDurable, queue.IsExclusive, queue.AutoDelete, queue.Arguments);

            queue.HasDeclared.ShouldBeTrue();
        }

        [Theory]
        [InlineData(true, false, false)]
        [InlineData(false, true, false)]
        [InlineData(false, false, true)]
        public void declare_second_time(bool autoDelete, bool isExclusive, bool isDurable)
        {
            var queue = new RabbitMqQueue("foo", new RabbitMqTransport())
            {
                AutoDelete = autoDelete,
                IsExclusive = isExclusive,
                IsDurable = isDurable,
            };

            // cheating here.
            var prop = ReflectionHelper.GetProperty<RabbitMqQueue>(x => x.HasDeclared);
            prop.SetValue(queue, true);

            var channel = Substitute.For<IModel>();
            queue.Declare(channel, NullLogger.Instance);

            channel.DidNotReceiveWithAnyArgs().QueueDeclare("foo", isDurable, isExclusive, autoDelete, queue.Arguments);
            queue.HasDeclared.ShouldBeTrue();
        }

        [Fact]
        public void initialize_with_no_auto_provision_or_auto_purge()
        {
            theTransport.AutoProvision = false;
            theTransport.AutoPurgeAllQueues = false;
            var queue = new RabbitMqQueue("foo", theTransport);

            theTransport.Queues["foo"].PurgeOnStartup = false;
            
            queue.Initialize(theChannel, NullLogger.Instance);
            
            theChannel.DidNotReceiveWithAnyArgs().QueueDeclare("foo", true, true, true, null);
            theChannel.DidNotReceiveWithAnyArgs().QueuePurge("foo");
        }


        [Fact]
        public void initialize_with_no_auto_provision_but_auto_purge_on_endpoint_only()
        {
            theTransport.AutoProvision = false;
            theTransport.AutoPurgeAllQueues = false;

            var endpoint = theTransport.Queues["foo"];
            endpoint.PurgeOnStartup = true;

            endpoint.Initialize(theChannel, NullLogger.Instance);

            theChannel.DidNotReceiveWithAnyArgs().QueueDeclare("foo", true, true, true, null);
            theChannel.Received().QueuePurge("foo");
        }

        [Fact]
        public void initialize_with_no_auto_provision_but_global_auto_purge()
        {
            theTransport.AutoProvision = false;
            theTransport.AutoPurgeAllQueues = true;

            var endpoint = new RabbitMqQueue("foo",theTransport);

            theTransport.Queues["foo"].PurgeOnStartup = false;

            endpoint.Initialize(theChannel, NullLogger.Instance);

            theChannel.DidNotReceiveWithAnyArgs().QueueDeclare("foo", true, true, true, null);
            theChannel.Received().QueuePurge("foo");
        }

        [Fact]
        public void initialize_with_auto_provision_and_global_auto_purge()
        {
            theTransport.AutoProvision = true;
            theTransport.AutoPurgeAllQueues = true;

            var endpoint = new RabbitMqQueue("foo", theTransport);

            theTransport.Queues["foo"].PurgeOnStartup = false;

            endpoint.Initialize(theChannel, NullLogger.Instance);

            theChannel.Received().QueueDeclare("foo", true, false, false, endpoint.Arguments);
            theChannel.Received().QueuePurge("foo");
        }

        [Fact]
        public void initialize_with_auto_provision_and_local_auto_purge()
        {
            theTransport.AutoProvision = true;
            theTransport.AutoPurgeAllQueues = false;

            var endpoint = theTransport.Queues["foo"];
            endpoint.PurgeOnStartup = true;

            endpoint.Initialize(theChannel, NullLogger.Instance);

            theChannel.Received().QueueDeclare("foo", true, false, false, endpoint.Arguments);
            theChannel.Received().QueuePurge("foo");
        }
    }
}
