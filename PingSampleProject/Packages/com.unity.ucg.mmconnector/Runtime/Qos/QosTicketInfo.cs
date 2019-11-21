using System;
using System.Collections.Generic;

namespace Unity.Ucg.MmConnector
{
    /// <summary>QoS result compatible with Multiplay server allocation</summary>
    [Serializable]
    public struct QosResultMultiplay
    {
        /// <summary>Multiplay Location ID</summary>
        public long Location;
        /// <summary>Multiplay Region ID</summary>
        public string Region;

        /// <summary>Latency in milliseconds</summary>
        public int Latency;
        /// <summary>Packet loss percentage [0.0..1.0]</summary>
        public float PacketLoss;
    }

    [Serializable]
    public class QosTicketInfo
    {
        public List<QosResultMultiplay> QosResults;

        public QosTicketInfo() => QosResults = new List<QosResultMultiplay>();
    }
}
