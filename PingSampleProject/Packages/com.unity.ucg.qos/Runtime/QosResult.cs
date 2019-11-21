namespace Unity.Networking.QoS
{
    public struct QosResult
    {
        public uint RequestsSent;       // Requests sent
        public uint ResponsesReceived;  // Valid, non-duplicate responses received
        public uint AverageLatencyMs;   // Average latency in milliseconds over the responses received
        public float PacketLoss;        // Percentage of packet loss from 0.0f - 1.0f (0 - 100%)
        public uint InvalidRequests;    // Count of requests ignored due to being invalid (wrote fewer than Length bytes)
        public uint InvalidResponses;   // Count of responses discarded due to being invalid (too small, too big, etc.)
        public uint DuplicateResponses; // Count of duplicate responses that were ignored
        public FcType FcType;           // Type of FlowControl set on response (if any)
        public byte FcUnits;            // Units of Flow Control for given FcType (if any)

        public float UpdatePacketLoss()
        {
            PacketLoss = (RequestsSent == 0) ? 0.0f : 1.0f - (ResponsesReceived / (float)RequestsSent);
            return PacketLoss;
        }
    }
}