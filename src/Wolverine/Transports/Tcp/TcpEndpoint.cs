using System;
using System.Net;
using Wolverine.Util;
using Wolverine.Configuration;
using Wolverine.Runtime;
using Wolverine.Transports.Sending;

namespace Wolverine.Transports.Tcp;

public class TcpEndpoint : Endpoint
{
    public TcpEndpoint() : this("localhost", 2000)
    {
    }

    public TcpEndpoint(int port) : this("localhost", port)
    {
    }

    public TcpEndpoint(string hostName, int port) : base( ToUri(port, hostName), EndpointRole.Application)
    {
        HostName = hostName;
        Port = port;

        // ReSharper disable once VirtualMemberCallInConstructor
        EndpointName = Uri.ToString();
    }

    public string HostName { get; private set; }

    public int Port { get; private set; }
    

    protected override bool supportsMode(EndpointMode mode)
    {
        return mode != EndpointMode.Inline;
    }

    public static Uri ToUri(int port, string hostName = "localhost")
    {
        return $"tcp://{hostName}:{port}".ToUri();
    }

    public override IListener BuildListener(IWolverineRuntime runtime, IReceiver receiver)
    {
        // check the uri for an ip address to bind to
        var cancellation = runtime.Advanced.Cancellation;

        var hostNameType = Uri.CheckHostName(HostName);

        if (hostNameType != UriHostNameType.IPv4 && hostNameType != UriHostNameType.IPv6)
        {
            return HostName == "localhost"
                ? new SocketListener(this, receiver, runtime.Logger, IPAddress.Loopback, Port, cancellation)
                : new SocketListener(this, receiver, runtime.Logger, IPAddress.Any, Port, cancellation);
        }

        var ipaddr = IPAddress.Parse(HostName);
        return new SocketListener(this, receiver, runtime.Logger, ipaddr, Port, cancellation);
    }

    protected override ISender CreateSender(IWolverineRuntime runtime)
    {
        return new BatchedSender(Uri, new SocketSenderProtocol(), runtime.Advanced.Cancellation, runtime.Logger);
    }
}
