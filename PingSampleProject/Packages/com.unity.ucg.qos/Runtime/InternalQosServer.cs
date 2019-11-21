using System;
using Unity.Networking.Transport;

namespace Unity.Networking.QoS
{
    public partial struct QosJob
    {
        /// <summary>
        /// Internal version of the QosServer struct that trades IP/Port for RemoteEndPoint since NativeArrays can only
        /// have value types.
        /// </summary>
        private struct InternalQosServer
        {
            public readonly NetworkEndPoint RemoteEndpoint;
            public readonly DateTime BackoffUntilUtc;
            public readonly ulong Id;

            public InternalQosServer(string ip, ushort port, DateTime backoffUntilUtc)
            {
                if (TryParseIpv4(ip, out var ipv4) == false)
                    throw new ArgumentException($"Invalid IP address {ip} in QoS Servers list", nameof(ip));

                RemoteEndpoint = NetworkEndPoint.Parse(ip, port);
                BackoffUntilUtc = backoffUntilUtc;

                Id = ((ulong)ipv4 << 32) | port;
            }
        }
    }
}