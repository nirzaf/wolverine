using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using Baseline;
using DotPulsar;
using DotPulsar.Abstractions;
using Wolverine.Configuration;
using Wolverine.Runtime;
using Wolverine.Transports;
using Wolverine.Transports.Sending;

namespace Wolverine.Pulsar
{
    public class PulsarEndpoint : Endpoint
    {
        private readonly PulsarTransport _parent;
        public const string Persistent = "persistent";
        public const string NonPersistent = "non-persistent";
        public const string DefaultNamespace = "tenant";
        public const string Public = "public";

        public static Uri UriFor(bool persistent, string tenant, string @namespace, string topicName)
        {
            var scheme = persistent ? "persistent" : "non-persistent";
            return new Uri($"{scheme}://{tenant}/{@namespace}/{topicName}");
        }

        public static Uri UriFor(string topicPath)
        {
            var uri = new Uri(topicPath);
            return new Uri($"pulsar://{uri.Scheme}/{uri.Host}/{uri.Segments.Skip(1).Select(x => x.TrimEnd('/')).Join("/")}");
        }

        public PulsarEndpoint(Uri uri, PulsarTransport parent) : base(uri, EndpointRole.Application)
        {
            _parent = parent;
            Parse(uri);
        }

        public override IDictionary<string, object> DescribeProperties()
        {
            var dict = base.DescribeProperties();

            dict.Add(nameof(Persistent), Persistent);
            dict.Add(nameof(Tenant), Tenant);
            dict.Add(nameof(Namespace), Namespace);
            if (TopicName != null)
            {
                dict.Add(nameof(TopicName), TopicName);
            }

            return dict;
        }

        public string Persistence { get; private set; } = Persistent;
        public string Tenant { get; private set; } = Public;
        public string Namespace { get; private set; } = DefaultNamespace;
        public string? TopicName { get; private set;}

        internal PulsarEnvelopeMapper BuildMapper(IWolverineRuntime runtime)
        {
            return new PulsarEnvelopeMapper(this, runtime);
        }
        
        internal void Parse(Uri uri)
        {
            if (uri.Segments.Length != 4)
            {
                throw new InvalidPulsarUriException(uri);
            }

            if (uri.Host != Persistent && uri.Host != NonPersistent)
            {
                throw new InvalidPulsarUriException(uri);
            }

            Persistence = uri.Host;
            Tenant = uri.Segments[1].TrimEnd('/');
            Namespace = uri.Segments[2].TrimEnd('/');
            TopicName = uri.Segments[3].TrimEnd('/');
        }

        public string PulsarTopic()
        {
            return $"{Persistence}://{Tenant}/{Namespace}/{TopicName}";
        }

        public override IListener BuildListener(IWolverineRuntime runtime, IReceiver receiver)
        {
            // TODO -- parallel listener option????
            return new PulsarListener(runtime, this, receiver, _parent, runtime.Cancellation);
        }

        protected override ISender CreateSender(IWolverineRuntime runtime)
        {
            return new PulsarSender(runtime, this, _parent, runtime.Cancellation);
        }

    }
}
