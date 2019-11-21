#if USE_QOSCONNECTOR
using Unity.QosConnector;
#elif USE_MMCONNECTOR
using Unity.Ucg.MmConnector;
#endif
using System;
using System.Collections;
using System.Linq;
using System.Text;
using Unity.Jobs;
using Unity.Networking.QoS;
using Unity.Networking.Transport;
using UnityEngine;

public class QosCheckSample : MonoBehaviour
{
    Coroutine m_QosCoroutine;
    QosDiscovery m_Discovery;
    QosJob m_Job;
    float m_QosCheckTimer;
    QosStats m_Stats;
#if USE_QOSCONNECTOR || USE_MMCONNECTOR
    int m_QosConnectorId;
#endif

    JobHandle m_UpdateHandle;

    // Properties set in editor
    [Header("QoS Check Settings")]
    [Tooltip("Title to include in QoS Request.  Must be specified.")]
    public string title;

    [Tooltip("Milliseconds for the job to complete.  Each QosServer gets a portion of the overall Timeout to complete.  Must be non-zero.")]
    public ulong timeoutMs = 5000;

    [Tooltip("How many requests to send (and responses to expect) per QoS Server. Must be [1..256].")]
    public uint requestsPerEndpoint = 10;

    [Tooltip("Time in milliseconds to wait between QoS checks.")]
    public ulong qosCheckIntervalMs = 30000;

    [Tooltip("QoS Servers to test for the QoS Check.  If not specified, Discovery will be used.")]
    public QosServer[] qosServers;

    [Header("QoS Result Settings")]
    [Tooltip("Weight to give the most recent QoS results in the overall stats [0.0..1.0].  The historic results share the remainder of the weight equally.")]
    public float weightOfCurrentResult = 0.75f;

    [Tooltip("How many QoS results to save per server. The historic results are used in the computation of the overall weighted rolling average.  Must be non-zero.")]
    public uint qosResultHistory = 5;

    [Header("QoS Discovery Settings")]
    [Tooltip("If true, queries Multiplay for a list of QoS servers for the locations a Fleet is deployed to.")]
    public bool useQosDiscoveryService = true;

    [Tooltip("Multiplay Fleet ID where QoS server(s) are running.  Required for QoS Discovery.")]
    public string fleetId;

    [Tooltip("Seconds to wait for a response from Discovery service before timing out. Default is 30 seconds.")]
    public int discoveryTimeoutSec = 30;

    void Awake()
    {
        NativeBindings.network_initialize();

#if USE_QOSCONNECTOR || USE_MMCONNECTOR
        m_QosConnectorId = QosConnector.Instance.RegisterProvider(GetResultsForQosConnector);

        Debug.Log($"{nameof(QosCheckSample)} using Qos Connector");
#endif
    }

    void Update()
    {
        // Don't do anything more while waiting for a Qos ping job to complete
        if (!m_UpdateHandle.IsCompleted)
            return;

        m_UpdateHandle.Complete();

        // Extract the results of the last completed Qos ping job and log them
        if (m_Job.QosResults.IsCreated)
        {
            // Update the history of QoS results
            UpdateStats();
            PrintStats();

            m_Job.QosResults.Dispose(); // Dispose the results now that we're done with them
        }

        // Start the Qos ping coroutine if not started
        m_QosCoroutine = m_QosCoroutine ?? StartCoroutine(PeriodicQosPingCoroutine());
    }

    void OnDestroy()
    {
        m_UpdateHandle.Complete();

        if (m_Job.QosResults.IsCreated)
            m_Job.QosResults.Dispose();

        m_Discovery?.Reset();

#if USE_QOSCONNECTOR || USE_MMCONNECTOR
        QosConnector.Instance.TryUnregisterProvider(m_QosConnectorId);
#endif

        NativeBindings.network_terminate();
    }

    // A coroutine that perioidically triggers qos checks
    IEnumerator PeriodicQosPingCoroutine()
    {
        // Attempt a qos query once every qosCheckIntervalMs
        while (isActiveAndEnabled)
        {
            if (timeoutMs + (ulong)discoveryTimeoutSec * 1000 > qosCheckIntervalMs)
                Debug.LogWarning("The combination of discovery timeout and qos timeout are longer than the interval between Qos checks." +
                    "  This may result in overlapped discovery and/or qos checks being canceled before completion.");

            var qosCheck = StartCoroutine(ExecuteSingleQosQueryCoroutine());

            // Delay the next QoS check until qosCheckIntervalMs has elapsed 
            Debug.Log($"QoS check will run again in {qosCheckIntervalMs / 1000:F} seconds.");
            yield return new WaitForSeconds(qosCheckIntervalMs / 1000f);

            // Ensure that last coroutine completed before moving on
            if (qosCheck != null)
                StopCoroutine(qosCheck);
        }
    }

    // A coroutine that runs a single qos discovery + qos check
    //  Note - exceptions fired in this method do not affect / get captured by PeriodicQosPingCoroutine()
    IEnumerator ExecuteSingleQosQueryCoroutine()
    {
        if (string.IsNullOrEmpty(title))
            throw new ArgumentNullException(nameof(title), "Title must be set");

        if (timeoutMs == 0)
            throw new ArgumentOutOfRangeException(nameof(timeoutMs), "TimeoutMs must be non-zero");

        if (qosResultHistory == 0)
            throw new ArgumentOutOfRangeException(nameof(qosResultHistory), "Must keep at least 1 QoS result");

        if (weightOfCurrentResult < 0.0f || weightOfCurrentResult > 1.0f)
            throw new ArgumentOutOfRangeException(nameof(weightOfCurrentResult), "Weight must be in the range [0.0..1.0]");

        // Try to use the discovery service to find qos servers
        if (useQosDiscoveryService)
        {
            PopulateQosServerListFromService();

            while (m_Discovery != null && !m_Discovery.IsDone)
                yield return null;
        }

        // Kick off a new qos job if possible
        if (qosServers?.Length > 0)
            ScheduleNewQosJob();
        else
            Debug.LogWarning($"{nameof(QosCheck)} tried to update QoS results, but no QoS servers have been specified or discovered");
    }

    // Make a discovery call if requirements are met
    void PopulateQosServerListFromService()
    {
        if (!useQosDiscoveryService)
            return;

        var fleet = fleetId?.Trim();

        // If a fleet is no longer specified, remove the discovery component
        //  Note - This does not remove the last results from the qosServers list
        if (string.IsNullOrEmpty(fleet))
            throw new ArgumentNullException(nameof(fleetId), "Fleet ID is invalid - Discovery aborted");

        m_Discovery = m_Discovery ?? new QosDiscovery();

        if (m_Discovery.State != DiscoveryState.Running)
            m_Discovery.StartDiscovery(fleet, discoveryTimeoutSec, DiscoverySuccess, DiscoveryError);
    }

    // Handle a successful discovery service call
    void DiscoverySuccess(QosServer[] servers)
    {
        // Update the list of available QoS servers
        qosServers = servers ?? new QosServer[0];

        if (qosServers.Length == 0)
        {
            Debug.LogWarning("Discovery found no QoS servers");
        }
        else
        {
            var sb = new StringBuilder(qosServers.Length);
            sb.AppendLine("QoS Discovery found the following QoS servers:");

            foreach (var server in qosServers)
                sb.AppendLine(server.ToString());

            Debug.Log(sb.ToString());
        }
    }

    // Handle a failed discovery service call
    void DiscoveryError(string error)
    {
        Debug.LogError(error);
    }

    // Start up a job that pings QoS endpoints
    void ScheduleNewQosJob()
    {
        m_Job = new QosJob(qosServers, title)
        {
            RequestsPerEndpoint = requestsPerEndpoint,
            TimeoutMs = timeoutMs
        };

        m_UpdateHandle = m_Job.Schedule();
        JobHandle.ScheduleBatchedJobs();
    }

    void UpdateStats()
    {
        var results = m_Job.QosResults.ToArray();

        if (results.Length == 0)
        {
            Debug.LogWarning("No QoS results available because no QoS servers contacted.");
            return;
        }

        // We've got stats to record, so create a new stats tracker if we don't have one yet
        m_Stats = m_Stats ?? new QosStats(qosResultHistory, weightOfCurrentResult);

        // Add stats for each endpoint to the stats tracker
        for (var i = 0; i < results.Length; ++i)
        {
            var ipAndPort = qosServers[i].ToString();
            var r = results[i];
            m_Stats.AddResult(ipAndPort, r);

            if (r.RequestsSent == 0)
                Debug.Log($"{ipAndPort}: Sent/Received: 0");
            else
                Debug.Log($"{ipAndPort}: " +
                    $"Received/Sent: {r.ResponsesReceived}/{r.RequestsSent}, " +
                    $"Latency: {r.AverageLatencyMs}ms, " +
                    $"Packet Loss: {r.PacketLoss * 100.0f:F1}%, " +
                    $"Flow Control Type: {r.FcType}, " +
                    $"Flow Control Units: {r.FcUnits}, " +
                    $"Duplicate responses: {r.DuplicateResponses}, " +
                    $"Invalid responses: {r.InvalidResponses}");

            // Deal with flow control in results (must have gotten at least one response back)
            if (r.ResponsesReceived > 0 && r.FcType != FcType.None)
            {
                qosServers[i].BackoffUntilUtc = GetBackoffUntilTime(r.FcUnits);
                Debug.Log($"{ipAndPort}: Server applied flow control and will no longer respond until {qosServers[i].BackoffUntilUtc}.");
            }
        }
    }

    void PrintStats()
    {
        // Print out all the aggregate stats
        for (var i = 0; i < qosServers?.Length; ++i)
        {
            var ipAndPort = qosServers[i].ToString();

            if (m_Stats.TryGetWeightedAverage(ipAndPort, out var result))
            {
                m_Stats.TryGetAllResults(ipAndPort, out var allResults);

                // NOTE:  You probably don't want Linq in your game, but it's convenient here to filter out the invalid results.
                Debug.Log($"Weighted average QoS report for {ipAndPort}: " +
                    $"Latency: {result.LatencyMs}ms, " +
                    $"Packet Loss: {result.PacketLoss * 100.0f:F1}%, " +
                    $"All Results: {string.Join(", ", allResults.Select(x => x.IsValid() ? x.LatencyMs : 0))}");
            }
            else
            {
                Debug.Log($"No results for {ipAndPort}.");
            }
        }

#if USE_QOSCONNECTOR || USE_MMCONNECTOR
        // Print out what would be returned through the QosConnector
        QosTicketInfo results = QosConnector.Instance.Execute();
        if (results.QosResults?.Count > 0)
            Debug.Log("QosTicketInfo coming from the QosConnector:\n" + JsonUtility.ToJson(results, true));
#endif
    }

#if USE_QOSCONNECTOR || USE_MMCONNECTOR
    private IList<QosResultMultiplay> GetResultsForQosConnector()
    {
        // Note: QosConnector has its own QosResult. Make sure we're using it.
        var results = new List<QosResultMultiplay>();

        if (qosServers != null)
        {
            foreach (var qs in qosServers)
            {
                if (m_Stats.TryGetWeightedAverage(qs.ToString(), out var result))
                {
                    results.Add(new QosResultMultiplay
                    {
                        Location = qs.locationid,
                        Region = qs.regionid,
                        Latency = result.LatencyMs,
                        PacketLoss = result.PacketLoss
                    });
                }
            }
        }

        return results;
    }
#endif

    // Do not modify - Contract with server
    static DateTime GetBackoffUntilTime(byte fcUnits)
    {
        return DateTime.UtcNow.AddMinutes(2 * fcUnits + 0.5f); // 2 minutes for each unit, plus 30 seconds buffer
    }
}
