using System;
using System.Buffers;
using System.Threading;
using System.Threading.Tasks;
using DotPulsar;
using DotPulsar.Abstractions;
using DotPulsar.Extensions;
using Wolverine.Runtime;
using Wolverine.Transports.Sending;

namespace Wolverine.Pulsar
{
    public class PulsarSender : ISender, IAsyncDisposable
    {
        private readonly PulsarEndpoint _endpoint;
        private readonly CancellationToken _cancellation;
        private readonly IProducer<ReadOnlySequence<byte>> _producer;
        private readonly PulsarEnvelopeMapper _mapper;

        public PulsarSender(IWolverineRuntime runtime, PulsarEndpoint endpoint, PulsarTransport transport,
            CancellationToken cancellation)
        {
            _endpoint = endpoint;
            _cancellation = cancellation;

            // TODO -- make this more configurable with ConsumerOptions
            _producer = transport.Client!.NewProducer().Topic(_endpoint.PulsarTopic()).Create();

            Destination = _endpoint.Uri;
            _mapper = endpoint.BuildMapper(runtime);
        }

        public ValueTask DisposeAsync()
        {
            return _producer.DisposeAsync();
        }


        public bool SupportsNativeScheduledSend => true;
        public Uri Destination { get; }
        public async Task<bool> PingAsync()
        {
            Envelope envelope = Envelope.ForPing(Destination);
            try
            {
                await SendAsync(envelope);
            }
            catch
            {
                return false;
            }

            return true;
        }

        public async ValueTask SendAsync(Envelope envelope)
        {
            var message = new MessageMetadata();

            _mapper.MapEnvelopeToOutgoing(envelope, message);

            await _producer.Send(message, new ReadOnlySequence<byte>(envelope.Data!), _cancellation);
        }
    }
}
