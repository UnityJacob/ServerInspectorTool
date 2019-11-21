using System.Collections;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using Unity.Networking.QoS;
using Unity.Ucg.MmConnector;

namespace MultiplayPingSample.Client
{
    public enum MatchmakingState
    {
        None,
        Idle,
        QosDiscovery,
        QosPing,
        Matchmaking,
        ServerPing
    }

    public class MatchmakingClientBehaviour : MonoBehaviour
    {
        [SerializeField] string matchMakingServiceURL = "";
        [SerializeField] string fleetId = "";
        [SerializeField] uint matchmakingTimeoutMs = 0;
        QosDiscoveryAsyncWrapper m_QosDiscovery;
        QosPingAsyncWrapper m_QosPing;
        MatchmakingWrapper m_Matchmaking;
        MatchmakingState m_State;
        string m_MatchmakerState;
        CancellationTokenSource m_MatchCts;
        QosServer[] m_QosServers;
        IList<QosResultMultiplay> m_QosResults;
        
        bool _canMatchMake = false;
        bool _useQosForMatchmaking = false;

        public delegate void StateDelegate(MatchmakingState state);
        public StateDelegate onStateSet;
        
        public delegate void ServerResponseDelegate(string response);
        public ServerResponseDelegate onGotEndpoint;
        
        

        void Update()
        {
            m_QosPing?.Update();
            m_Matchmaking?.Update();
            m_MatchmakerState = m_Matchmaking?.GetState() ?? m_MatchmakerState;
        }
 
        public void StartMatchmaking()
        {
            if (string.IsNullOrEmpty(matchMakingServiceURL))
            {
                Debug.LogWarning("Cannot start matchmaking - No matchmaker URL was entered");
                return;
            }

            if (m_Matchmaking != null && !m_Matchmaking.IsDone)
            {
                Debug.LogWarning("Cannot start new matchmaking request - matchmaking is already in progress");
                return;
            }

            // Reset cancellation token
            m_MatchCts = new CancellationTokenSource();

            // Clear existing qos results
            QosConnector.Instance.Reset();

            if (_useQosForMatchmaking)
                StartQosMatchmaking(matchMakingServiceURL, fleetId, m_MatchCts.Token);
            else
                SendMatchRequest(matchMakingServiceURL, m_MatchCts.Token);
        }

        public void CancelMatchmaking()
        {
            if (m_Matchmaking != null && !m_Matchmaking.IsDone)
            {
                m_MatchCts.Cancel();
                Debug.Log("Cancelling matchmaking...");
            }
        }

        public bool CanMatchMake => _canMatchMake;

        public void OnStateChanged(MatchmakingState state)
        {
            m_State = state;
            onStateSet.Invoke(m_State);
        }

        void StartQosMatchmaking(string mmServiceURL, string multiplayFleetID, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(multiplayFleetID))
            {
                Debug.LogError("Qos Discovery Failed - Fleet ID was missing or invalid");
                return;
            }

            // Start with Qos Discovery
            OnStateChanged(MatchmakingState.QosDiscovery);
            m_QosDiscovery = new QosDiscoveryAsyncWrapper();

            m_QosDiscovery.Start(multiplayFleetID, 0, servers =>
            {
                if (token.IsCancellationRequested)
                {
                    Debug.Log("Qos Discovery Cancelled");
                    OnStateChanged(MatchmakingState.Idle);
                    return;
                }

                m_QosServers = servers;

                if (m_QosServers == null || m_QosServers.Length == 0)
                {
                    Debug.LogError($"Qos Discovery Failed - No servers found for {multiplayFleetID}");
                    OnStateChanged(MatchmakingState.Idle);
                    return;
                }

                // Move on to Qos Ping
                StartQosPing(mmServiceURL, multiplayFleetID, token);
            });
        }


        void StartQosPing(string mmServiceURL, string multiplayFleetID, CancellationToken token = default)
        {
            OnStateChanged(MatchmakingState.QosPing);
            m_QosPing = new QosPingAsyncWrapper(m_QosServers);

            m_QosPing.Start(qosResults =>
            {
                if (token.IsCancellationRequested)
                {
                    Debug.Log("Qos Ping Cancelled");
                    OnStateChanged(MatchmakingState.Idle);
                    return;
                }

                if (qosResults == null || qosResults.Count == 0)
                {
                    Debug.LogError($"Qos Pinging Failed - No valid results found for {multiplayFleetID}");
                    OnStateChanged(MatchmakingState.Idle);
                    return;
                }

                m_QosResults = qosResults;
                QosConnector.Instance.RegisterProvider(() => m_QosResults);

                m_QosPing.Dispose();

                // Move on to start matchmaking
                SendMatchRequest(mmServiceURL, token);
            });
        }

        void SendMatchRequest(string mmServiceURL, CancellationToken token = default)
        {
            OnStateChanged(MatchmakingState.Matchmaking);

            // Merge cancellation token w/ timeout
            if (matchmakingTimeoutMs > 0)
            {
                var timeoutCts = new CancellationTokenSource((int) matchmakingTimeoutMs);
                m_MatchCts = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCts.Token);
            }

            m_Matchmaking = new MatchmakingWrapper(mmServiceURL);

            //TODO Do we get more server info than the IP?
            m_Matchmaking.Start(serverEndpoint =>
            {
                if (string.IsNullOrEmpty(serverEndpoint))
                {
                    Debug.LogError("Matchmaking Failed - No server connection information returned");
                    OnStateChanged(MatchmakingState.Idle);
                    return;
                }

                // Success!
                GotServerEndpoint(serverEndpoint);
                OnStateChanged(MatchmakingState.Idle);
            }, token);
        }
        
        public void SetFleetId(string multiplayFleetID)
        {
            fleetId = multiplayFleetID;
            ValidateFleetId(fleetId);
        }

        public void SetMatchmakingUrl(string mmURL)
        {
            matchMakingServiceURL = mmURL;
            ValidateMmServiceUrl(matchMakingServiceURL);
        }
        
        void ValidateFleetId(string id)
        {
            _useQosForMatchmaking = !string.IsNullOrEmpty(id);
        }

        void ValidateMmServiceUrl(string url)
        {
            _canMatchMake = !string.IsNullOrEmpty(url);
        }

        public void GotServerEndpoint(string theEndpoint)
        {
            onGotEndpoint.Invoke(theEndpoint);
        }
    }
}