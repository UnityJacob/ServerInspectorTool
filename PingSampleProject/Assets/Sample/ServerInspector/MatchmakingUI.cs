using System.Collections.Generic;
using System.Linq;
using Unity.Ucg.MmConnector;
using System.Threading;
using TMPro;
using UnityEngine;
using UnityEditor;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace MultiplayPingSample.Client
{
    public class MatchmakingUI : MonoBehaviour
    {
        [SerializeField] private ServerConnectorUI serverUI;
        [SerializeField] TMP_Dropdown matchmakeConfigDropdown;
        [SerializeField] TMP_InputField fleetIDField;
        [SerializeField] TMP_InputField mmServiceField;
        [SerializeField] Button matchMakeButton;
        [SerializeField] TMP_Text stateText;

        private MatchmakingClientBehaviour _mmClient;

        [SerializeField]
        private MatchmakingClientBehaviour MmClient
        {
            get
            {
                if (_mmClient != null) return _mmClient;
                _mmClient = new GameObject("MatchmakerClient").AddComponent<MatchmakingClientBehaviour>();
                _mmClient.onStateSet += SetState;
                _mmClient.onGotEndpoint += SetServerEndpoint;

                return _mmClient;
            }
        }

        private Dictionary<TMP_Dropdown.OptionData, MatchmakingConfigAsset> _optionConfigAssetDict =
            new Dictionary<TMP_Dropdown.OptionData, MatchmakingConfigAsset>();

        string m_PingServerEndpoint = "";
        string m_MatchmakerState;
        bool _canMatchMake = false;
        bool _useQosForMatchmaking = false;

        void Start()
        {
            GetMatchMakingConfigs();
            SetButtonInteractable();
        }


        void SetState(MatchmakingState state)
        {
            stateText.text = state.ToString();
        }

        void SetServerEndpoint(string endpoint)
        {
            serverUI.SetPingEndpointUI(endpoint);
        }

        /// <summary>
        /// Create a dropdown menu of the configs in Resources/Configs, register the option as a key, and the asset as a value
        /// </summary>
        void GetMatchMakingConfigs()
        {
            matchmakeConfigDropdown.ClearOptions();
            List<TMP_Dropdown.OptionData> dropDownObjects = new List<TMP_Dropdown.OptionData>();

            _optionConfigAssetDict.Clear();
            foreach (MatchmakingConfigAsset config in Resources.LoadAll<MatchmakingConfigAsset>("Configs"))
            {
                TMP_Dropdown.OptionData od = new TMP_Dropdown.OptionData();
                od.text = config.name;
                _optionConfigAssetDict.Add(od, config);
                dropDownObjects.Add(od);
            }

            matchmakeConfigDropdown.AddOptions(dropDownObjects);
            SelectedMatchmaking(0);
        }


        void SetButtonInteractable()
        {
            matchMakeButton.interactable = MmClient.CanMatchMake;
        }

        #region  GUI Calls

        public void StartMatchmaking()
        {
            MmClient.StartMatchmaking();
        }


        public void CancelMatchmaking()
        {
            MmClient.CancelMatchmaking();
        }


        public void SetFleetId(string multiplayFleetID)
        {
            MmClient.SetFleetId(multiplayFleetID);
            SetButtonInteractable();
        }


        public void SetMatchmakingUrl(string mmURL)
        {
            MmClient.SetMatchmakingUrl(mmURL);
            SetButtonInteractable();
        }

        /// <summary>
        ///Function for the dropdownt to push its selection
        /// </summary>
        /// <param name="optionIndex">the return index from the Dropdown</param>
        public void SelectedMatchmaking(int optionIndex)
        {
            TMP_Dropdown.OptionData selectedData = matchmakeConfigDropdown.options[optionIndex];
            MatchmakingConfigAsset selectedAsset = _optionConfigAssetDict[selectedData];
            Debug.LogFormat("Selected: {0} URLUPID: {1} FleetID: {2} ", selectedAsset.name, selectedAsset.URLAndUPID(),
                selectedAsset.MultiplayFleetID);
            SetMatchmakingConfigAsset(selectedAsset);
        }

        /// <summary>
        /// Implement the config to this UI
        /// </summary>
        /// <param name="mca">reference to the config.</param>
        void SetMatchmakingConfigAsset(MatchmakingConfigAsset mca)
        {
            MmClient.SetFleetId(mca.MultiplayFleetID);
            fleetIDField.SetTextWithoutNotify(mca.MultiplayFleetID);
            MmClient.SetMatchmakingUrl(mca.URLAndUPID());
            mmServiceField.SetTextWithoutNotify(mca.URLAndUPID());
        }

        #endregion
    }
}