using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MultiplayPingSample.Client
{
    public class ClientInspectorUI : MonoBehaviour
    {
        public string PingServerEndpoint = "127.0.0.1:9000";
        string m_PingServerEndpoint = "";
        UdpPingWrapper m_UdpPing;

        private MatchmakingState m_State;

        // Start is called before the first frame update
        void StartNewPingClient()
        {
            if (string.IsNullOrEmpty(m_PingServerEndpoint))
            {
                Debug.LogWarning("Cannot start pinging - No ping server endpoint was entered");
                return;
            }

            if (m_UdpPing != null)
            {
                Debug.LogWarning("Cannot start pinging - Pinging already in progress");
                return;
            }

            Debug.Log("Starting pinging...");

            try
            {
                m_UdpPing = new UdpPingWrapper();
                m_UdpPing.Start(m_PingServerEndpoint);
                m_State = MatchmakingState.ServerPing;
            }
            catch (Exception e)
            {
                Debug.LogError("Cannot start pinging due to exception - " + e.Message);
                m_UdpPing?.Dispose();
                m_UdpPing = null;
            }
        }
        void ShutdownPingClient()
        {
            if (m_UdpPing == null)
            {
                Debug.LogWarning("Cannot shut down ping client - no pinging in progress");
                return;
            }

            Debug.Log("Shutting down ping client...");
            m_UdpPing.Dispose();
            m_UdpPing = null;
            m_State = MatchmakingState.Idle;
        }
        
        void TerminateRemoteServer()
        {
            if (m_UdpPing != null)
            {
                m_UdpPing.TryTerminateRemoteServer();
                m_UdpPing.Dispose();
                m_UdpPing = null;
            }
            else
            {
                UdpPingWrapper.TryTerminateRemoteServer(m_PingServerEndpoint);
            }

            m_State = MatchmakingState.Idle;
        }
        // Update is called once per frame
        void Update()
        {
            m_UdpPing?.Update();
        }
    }
}