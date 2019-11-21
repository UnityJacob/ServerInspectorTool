using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using MultiplayPingSample.Server;
using TMPro;
using UnityEngine;

namespace MultiplayPingSample.Client
{
    public class ServerConnectorUI : MonoBehaviour
    {
        [SerializeField] private TMP_Text serverNameLabel;
        [SerializeField] private TMP_InputField endPointField;
        [SerializeField] private MultiplayPingServerBehaviour _serverBehaviourPrefab;

        private MultiplayPingServerBehaviour _multiplayPingServerBehaviour;
        private ServerPingBehaviour _serverPingBehaviour;
      
        private ServerPingBehaviour ServerBehaviour
        {
            get
            {
                if (_serverPingBehaviour != null) return _serverPingBehaviour;
                _serverPingBehaviour = new GameObject("ServerBehaviour").AddComponent<ServerPingBehaviour>();
                _serverPingBehaviour.onEndpointUpdate += SetPingEndpointUI;
                return _serverPingBehaviour;
            }
        }
        
        public void SetPingEndpoint(string endpoint)
        {
            ServerBehaviour.SetPingEndpoint(endpoint);
        }
        
        
        
        public void SetPingEndpointUI(string endpoint)
        {
            endPointField.SetTextWithoutNotify(endpoint);
        }

        #region UIButton callbacks

        public void CreateLocalServer()
        {
            _multiplayPingServerBehaviour = Instantiate(_serverBehaviourPrefab);
            serverNameLabel.text = _multiplayPingServerBehaviour.ServerConfig.Info.ServerName;
            SetPingEndpoint(_multiplayPingServerBehaviour.GetEndpoint());
            SetPingEndpointUI(_multiplayPingServerBehaviour.GetEndpoint());
            
        }

        public void TerminateServer()
        {
            ServerBehaviour.TerminateRemoteServer();
        }
        
        public void StartNewPingClient()
        {
            ServerBehaviour.StartNewPingClient();
        }

        public void ShutDownPingClient()
        {
            ServerBehaviour.ShutdownPingClient();
        }
        public void OnEndpointFieldChanged(string endpoint)
        {
            ServerBehaviour.SetPingEndpoint(endpoint);
        }

        #endregion
    }
}