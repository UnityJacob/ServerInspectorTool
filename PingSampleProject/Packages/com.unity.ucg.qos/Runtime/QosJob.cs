using System;
using System.Collections.Generic;
using System.Text;
using Unity.Collections;
using Unity.Jobs;
using Unity.Networking.Transport;
using UnityEngine;
using Random = System.Random;

namespace Unity.Networking.QoS
{
    public partial struct QosJob : IJob
    {
        public uint RequestsPerEndpoint;
        public ulong TimeoutMs;

        // Leave the results allocated after the job is done.  It's the user's responsibility to dispose it.
        public NativeArray<QosResult> QosResults;

        [DeallocateOnJobCompletion] private NativeArray<InternalQosServer> m_QosServers;
        [DeallocateOnJobCompletion] private NativeArray<byte> m_TitleBytesUtf8;
        [DeallocateOnJobCompletion] private NativeArray<ulong> m_VisitedQosServers;
        private long m_Socket;
        private DateTime m_JobExpireTimeUtc;
        private ushort m_Identifier;

        public QosJob(IList<QosServer> qosServers, string title) : this()
        {
            // Copy the QoS Servers into the job, converting all the IP/Port to NetworkEndPoint and DateTime to ticks.
            m_QosServers = new NativeArray<InternalQosServer>(qosServers?.Count ?? 0, Allocator.Persistent);
            if (qosServers != null)
            {
                for (int i = 0; i < m_QosServers.Length; ++i)
                    // Indexing NativeArray returns temporary values, so can't edit in place.
                    m_QosServers[i] = new InternalQosServer(qosServers[i].ipv4, qosServers[i].port, qosServers[i].BackoffUntilUtc);
            }

            m_VisitedQosServers = new NativeArray<ulong>(qosServers?.Count ?? 0, Allocator.Persistent);

            // Indexes into QosResults correspond to indexes into qosServers/m_QosServers
            QosResults = new NativeArray<QosResult>(m_QosServers.Length, Allocator.Persistent);

            // Convert the title to a NativeArray of bytes (since everything in the job has to be a value-type)
            byte[] utf8Title = Encoding.UTF8.GetBytes(title);
            m_TitleBytesUtf8 = new NativeArray<byte>(utf8Title.Length, Allocator.Persistent);
            m_TitleBytesUtf8.CopyFrom(utf8Title);
        }

        public void Execute()
        {
            if (m_QosServers.Length == 0)
                return;    // Nothing to do.

            m_JobExpireTimeUtc = DateTime.UtcNow.AddMilliseconds(TimeoutMs);

            // Create the local socket
            int errorcode = 0;
            (m_Socket, errorcode) = CreateAndBindSocket();
            if (m_Socket == -1 || errorcode != 0)
            {
                // Can't run the job
                Debug.LogError("Failed to create and bind the local socket for QoS Check");
            }
            else
            {
                m_Identifier = (ushort)new Random().Next(ushort.MinValue, ushort.MaxValue);
                for (int i = 0; i < m_QosServers.Length; ++i)
                {
                    QosResult result = QosResults[i];
                    InternalQosServer server = m_QosServers[i];

                    if (QosHelper.ExpiredUtc(m_JobExpireTimeUtc))
                    {
                        Debug.LogWarning($"Ran out of time to finish remaining QoS Check for endpoint {i}.");
                        break;
                    }

                    // If we've already visited this server, just copy those results here.
                    if (QosServerVisited(server.Id))
                    {
                        if (TryCopyResult(server.Id, ref result) == false)
                        {
                            Debug.LogError($"Visited server must have a previous result available");
                            break;
                        }
                    }
                    else if (DateTime.UtcNow > server.BackoffUntilUtc) // Only contact this server if we are allowed
                    {
                        // For each iteration of the loop, give the remaining endpoints an equal fraction of the remaining
                        // overall job time.  For example if there are five endpoints that each get 1000ms (5000ms total),
                        // and the first endpoint finishes in 200ms, the remaining endpoints will get 1200ms to complete
                        // (4800ms remaining / 4 endpoints = 1200ms/endpoint).
                        double allottedTimeMs = QosHelper.RemainingUtc(m_JobExpireTimeUtc).TotalMilliseconds / (m_QosServers.Length - i);
                        DateTime startTimeUtc = DateTime.UtcNow;
                        DateTime expireTimeUtc = DateTime.UtcNow.AddMilliseconds(allottedTimeMs);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
                        Debug.Log($"QoS Check {i} gets {(expireTimeUtc - DateTime.UtcNow).TotalMilliseconds:F0}ms to complete.");
#endif

                        ++m_Identifier;
                        int err = SendQosRequests(server.RemoteEndpoint, m_Identifier, expireTimeUtc, ref result);
                        if (err != 0)
                        {
                            Debug.LogError($"Error {err} sending QoS requests.  Will attempt to receive responses anyway.");
                        }
                        err = RecvQosResponses(server.RemoteEndpoint, m_Identifier, expireTimeUtc, ref result);
                        if (err != 0)
                        {
                            Debug.LogError($"Error {err} receiving QoS responses. Will attempt to continue anyway.");
                        }

                        Debug.Log($"Received {result.ResponsesReceived}/{result.RequestsSent} responses from endpoint {i} in {(DateTime.UtcNow - startTimeUtc).TotalMilliseconds:F0}ms");

                        // Mark this server as visited

                        SetQosServerVisited(server.Id);
                    }
                    else
                    {
                        Debug.LogWarning($"Did not contact endpoint {i} due to backoff restrictions.");
                    }

                    // Save the result (even if we didn't contact the server)
                    QosResults[i] = result;
                }
            }

            NativeBindings.network_close(ref m_Socket, ref errorcode);
        }

        private void SetQosServerVisited(ulong id)
        {
            // Max number of servers to visit is the length of the array. If we exceed that, something bad happened.
            // Let it throw an exception.
            var i = 0;
            while (m_VisitedQosServers[i] != 0)
            {
                if (m_VisitedQosServers[i] == id)
                {
                    // If this happens, we're re-marking an already visited server.  Something is probably wrong
                    // so complain.
                    Debug.LogError($"QoS Server {id} has already been visited");
                    return;
                }
                ++i;
            }
            m_VisitedQosServers[i] = id;
        }

        private bool QosServerVisited(ulong id)
        {
            foreach (var visitedId in m_VisitedQosServers)
            {
                if (visitedId == id)
                    return true;
            }

            return false;
        }

        private bool TryCopyResult(ulong id, ref QosResult result)
        {
            var rc = TryFindFirstMatchingResult(id, out var r);
            if (rc == true)
                result = r;

            return rc;
        }

        private bool TryFindFirstMatchingResult(ulong id, out QosResult r)
        {
            for (var i = 0; i < m_QosServers.Length; ++i)
            {
                if (m_QosServers[i].Id == id)
                {
                    r = QosResults[i];
                    return true;
                }
            }
            r = new QosResult();
            return false;
        }

        /// <summary>
        /// Send QoS requests to the given endpoint.
        /// </summary>
        /// <param name="remoteEndpoint">Server that QoS requests should be sent to</param>
        /// <param name="expireTime">When to stop trying to send requests</param>
        /// <param name="result">Results from the send side of the check (packets sent)</param>
        /// <returns>
        /// errorcode - the last error code generated (if any).  0 indicates no error.
        /// </returns>
        private int SendQosRequests(NetworkEndPoint remoteEndpoint, ushort identifier, DateTime expireTime, ref QosResult result)
        {
            QosRequest request = new QosRequest
            {
                Title = m_TitleBytesUtf8.ToArray(),
                Identifier = identifier
            };

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log($"Identifier for this QoS check is: 0x{request.Identifier:X4}");
#endif

            // Send all requests.
            result.RequestsSent = 0;
            while (result.RequestsSent < RequestsPerEndpoint && !QosHelper.ExpiredUtc(expireTime))
            {
                request.Timestamp = (ulong)(DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond);
                request.Sequence = (byte)result.RequestsSent;

                int errorcode = 0;
                int sent = 0;
                (sent, errorcode) = request.Send(m_Socket, ref remoteEndpoint, expireTime);
                if (errorcode != 0)
                {
                    Debug.LogError($"Send returned error code {errorcode}, can't continue");
                    return errorcode;
                }
                else if (sent != request.Length)
                {
                    Debug.LogWarning($"Sent {sent} of {request.Length} bytes, ignoring this request");
                    ++result.InvalidRequests;
                }
                else
                {
                    ++result.RequestsSent;
                }
            }

            return 0;
        }

        /// <summary>
        /// Receive QoS responses from the given endpoint
        /// </summary>
        /// <param name="remoteEndpoint">Where to expect responses to come from</param>
        /// <param name="identifier">Identifier that will accompany a valid response</param>
        /// <param name="expireTimeUtc">How long to wait for responses</param>
        /// <param name="result">Results from the receive side of the check (packets received, latency, packet loss)</param>
        /// <returns>
        /// errorcode - the last error code (if any). 0 means no error.
        /// </returns>
        private int RecvQosResponses(NetworkEndPoint remoteEndpoint, ushort identifier, DateTime expireTimeUtc, ref QosResult result)
        {
            if (result.RequestsSent == 0)
            {
                return 0; // Not expecting any responses
            }

            NativeArray<int> responseLatency = new NativeArray<int>((int)result.RequestsSent, Allocator.Temp);
            for (int i = 0; i < responseLatency.Length; ++i)
            {
                responseLatency[i] = -1;
            }

            QosResponse response = new QosResponse();
            int errorcode = 0;
            while (result.ResponsesReceived < result.RequestsSent && !QosHelper.ExpiredUtc(expireTimeUtc))
            {
                errorcode = 0;
                int received = 0;
                (received, errorcode) = response.Recv(m_Socket, remoteEndpoint, expireTimeUtc);
                if (received == -1)
                {
                    Debug.LogError($"Invalid or no response received (errorcode = {errorcode})");
                }
                else if (!response.Verify())
                {
                    Debug.LogWarning("Ignoring invalid QosResponse");
                    ++result.InvalidResponses;
                }
                else if (response.Identifier != identifier)
                {
                    Debug.LogWarning($"Identifier 0x{response.Identifier:X4} != expected identifier 0x{identifier:X4}; ignoring...");
                    ++result.InvalidResponses;
                }
                else
                {
                    if (response.Sequence >= result.RequestsSent) // Sequence can't be more than number of requests sent
                    {
                        Debug.LogWarning($"Ignoring response with sequence {response.Sequence} that is higher than max sequence expected");
                        ++result.InvalidResponses;
                    }
                    else if (responseLatency[response.Sequence] == -1)
                    {
                        responseLatency[response.Sequence] = response.LatencyMs;
                        ++result.ResponsesReceived;
                    }
                    else
                    {
                        Debug.Log($"Duplicate response {response.Sequence} received for QosCheck identifier 0x{response.Identifier:X4}");
                        ++result.DuplicateResponses;
                    }

                    // Determine if we've had flow control applied to us.  If so, save the most significant result based
                    // on the unit count.  In this version, both Ban and Throttle have the same result: client back-off.
                    var fc = response.ParseFlowControl();
                    if (fc.type != FcType.None && fc.units > result.FcUnits)
                    {
                        result.FcType = fc.type;
                        result.FcUnits = fc.units;
                    }
                }
            }

            // Calculate average latency and log results
            result.AverageLatencyMs = 0;
            if (result.ResponsesReceived > 0)
            {
                uint validResponses = 0;
                for (int i = 0, length = responseLatency.Length; i < length; i++)
                {
                    var latency = responseLatency[i];
                    if (latency >= 0)
                    {
                        result.AverageLatencyMs += (uint)latency;
                        validResponses++;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                        Debug.Log($"Received response {i} for QosCheck identifier 0x{identifier:X4} with latency {latency}ms");
#endif
                    }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    else
                    {
                        Debug.LogWarning($"Failed to receive response {i} for QosCheck identifier 0x{identifier:X4}");
                    }
#endif
                }

                result.AverageLatencyMs /= validResponses;
            }

            result.UpdatePacketLoss();
            responseLatency.Dispose();
            return errorcode;
        }

        /// <summary>
        /// Create and bind the local UDP socket for QoS checks. Also sets appropriate options on the socket such as
        /// non-blocking and buffer sizes.
        /// </summary>
        /// <returns>
        /// (socketfd, errorcode) where socketfd is a native socket descriptor and errorcode is the error code (if any)
        /// errorcode is 0 on no error.
        /// </returns>
        private static (long, int) CreateAndBindSocket()
        {
            // Create the local socket.
            NetworkEndPoint localEndpoint = NetworkEndPoint.AnyIpv4;
            int errorcode = 0;
            long socket = -1;
            int rc = NativeBindings.network_create_and_bind(ref socket, ref localEndpoint, ref errorcode);
            if (rc != 0)
            {
                Debug.LogError($"network_create_and_bind returned {rc}, error code is {errorcode}");
                return (rc, errorcode);
            }

            NativeBindings.network_set_nonblocking(socket);
            NativeBindings.network_set_send_buffer_size(socket, ushort.MaxValue);
            NativeBindings.network_set_receive_buffer_size(socket, ushort.MaxValue);

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            // Avoid WSAECONNRESET errors when sending to an endpoint which isn't open yet (unclean connect/disconnects)
            NativeBindings.network_set_connection_reset(socket, 0);
#endif
            return (socket, errorcode);
        }

        private static bool TryParseIpv4(string strIp, out uint ip)
        {
            ip = 0;

            // Parse failure check
            if (strIp == null || strIp.Length < 7 || strIp.Length > 15)
                return false;

            var pos = 0;

            // Parse characters
            for (var part = 0; part < 4; ++part)
            {
                // Parse failure check
                if (pos >= strIp.Length || strIp[pos] < '0' || strIp[pos] > '9')
                    return false;

                uint octet = 0;

                while (pos < strIp.Length && strIp[pos] >= '0' && strIp[pos] <= '9')
                {
                    octet = (octet * 10) + (uint)(strIp[pos] - '0');
                    ++pos;
                }

                // Parse failure check
                if (octet > 255)
                    return false;

                ip = (ip << 8) | octet;

                if (pos < strIp.Length && strIp[pos] == '.')
                    ++pos;
            }

            return true;
        }
    }
}