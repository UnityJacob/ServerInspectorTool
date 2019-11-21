using System;
using System.Collections;
using System.Collections.Generic;
using MultiplayPingSample.Client;
using UnityEngine;

public class ServerPingBehaviour : MonoBehaviour
{
    [SerializeField] string pingServerEndpoint = "";
    UdpPingWrapper m_UdpPing;
    MatchmakingState m_State;

    public delegate void OnEndpointUpdate(string endpointString);

    public OnEndpointUpdate onEndpointUpdate;

    void Update()
    {
        m_UdpPing?.Update();
    }

    public void SetPingEndpoint(string endpoint)
    {
        pingServerEndpoint = endpoint;
    }


    public void StartNewPingClient()
    {
        if (string.IsNullOrEmpty(pingServerEndpoint))
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
            m_UdpPing.Start(pingServerEndpoint);
            m_State = MatchmakingState.ServerPing;
        }
        catch (Exception e)
        {
            Debug.LogError("Cannot start pinging due to exception - " + e.Message);
            m_UdpPing?.Dispose();
            m_UdpPing = null;
        }
    }

    public void ShutdownPingClient()
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

    public void TerminateRemoteServer()
    {
        if (m_UdpPing != null)
        {
            m_UdpPing.TryTerminateRemoteServer();
            m_UdpPing.Dispose();
            m_UdpPing = null;
        }
        else
        {
            UdpPingWrapper.TryTerminateRemoteServer(pingServerEndpoint);
        }

        m_State = MatchmakingState.Idle;
    }

    public void OnDestroy()
    {
        ShutdownPingClient();
    }
}