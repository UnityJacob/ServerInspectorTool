using System;
using UnityEngine;

namespace Unity.Networking.QoS
{
    /// <summary>
    /// Definition of QoS server returned from the Discovery Service.
    /// </summary>
    /// <remarks>
    /// Note Unity's JSON deserializer is case-sensitive.  The fields must match exactly.
    /// </remarks>
    [Serializable]
    public struct QosServer
    {
        [Tooltip("Multiplay-specific location ID of the server")]
        public long locationid;

        [Tooltip("Multiplay-specific region ID of the server")]
        public string regionid;

        [Tooltip("Dotted-quad IPv4 address of QoS Server.")]
        public string ipv4;

        [Tooltip("Dotted-quad IPv6 address of QoS Server.")]
        public string ipv6;

        [Tooltip("Port of QoS Server. Must be [1..65535].")]
        public ushort port;

        [HideInInspector]
        [NonSerialized]
        // May be set as a result of Flow Control.  If set, when to allow this QosServer to be used again.
        public DateTime BackoffUntilUtc;

        public override string ToString()
        {
            // Prefer IPv6 address with fallback to IPv4
            if (!string.IsNullOrEmpty(ipv6))
                return $"{ipv6}:{port}";

            return string.IsNullOrEmpty(ipv4) ? "" : $"{ipv4}:{port}";
        }
    }
}