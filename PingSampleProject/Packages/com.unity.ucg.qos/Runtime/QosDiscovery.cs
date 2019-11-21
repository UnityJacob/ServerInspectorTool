using System;
using System.Net;
using System.Runtime.CompilerServices;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;

namespace Unity.Networking.QoS
{
    public enum DiscoveryState
    {
        NotStarted = 0,
        Running,
        Done,
        Failed
    }

    public class QosDiscovery
    {
        const string k_DefaultDiscoveryServiceUri = "https://qos.multiplay.com/v1/fleets/{0}/servers";
        const int k_DefaultDiscoveryTimeoutSeconds = 30;
        const int k_DefaultFailureCacheTimeMs = 1 * 1000;
        const int k_DefaultSuccessCacheTimeMs = 30 * 1000;

        // Used to track whether code execution is happening on main thread
        static readonly Thread k_MainThread = Thread.CurrentThread;

        DateTime m_CacheExpireTimeUtc = DateTime.MinValue;
        UnityWebRequestAsyncOperation m_DiscoverQosAsyncOp;
        string m_DiscoveryServiceUriPattern = k_DefaultDiscoveryServiceUri;
        string m_Etag;
        string m_FleetId;
        QosServer[] m_QosServersCache;

        public QosDiscovery(string uri = null)
        {
            if (!string.IsNullOrEmpty(uri))
                DiscoveryServiceUri = uri;
        }

        /// <summary>
        ///     The time (in seconds) to wait before timing out a call to the discovery service
        /// </summary>
        public int DiscoveryTimeoutSeconds { get; set; } = k_DefaultDiscoveryTimeoutSeconds;

        /// <summary>
        ///     The time (in milliseconds) to cache a failure state
        /// </summary>
        public int FailureCacheTimeMs { get; set; } = k_DefaultFailureCacheTimeMs;

        /// <summary>
        ///     The time (in milliseconds) to cache a set of successful results.
        ///     Can be overridden by cache-control information received from the service.
        /// </summary>
        public int SuccessCacheTimeMs { get; set; } = k_DefaultSuccessCacheTimeMs;

        /// <summary>
        ///     The callback to invoke when a call to the discovery service is successful
        /// </summary>
        public Action<QosServer[]> OnSuccess { get; set; }

        /// <summary>
        ///     The callback to invoke when a call to the discovery service fails
        /// </summary>
        public Action<string> OnError { get; set; }

        /// <summary>
        ///     The pattern for building the URI to the discovery service.
        ///     For use in a formatter; replace your Fleet ID with {0} and set the FleetId separately using the FleetId property.
        /// </summary>
        public string DiscoveryServiceUri
        {
            get => m_DiscoveryServiceUriPattern;
            set
            {
                if (string.IsNullOrEmpty(value))
                    throw new ArgumentNullException(nameof(value), $"{nameof(DiscoveryServiceUri)} cannot be null or empty.");

                // If trying to set the discovery URI to a new value,
                //  old / in-flight results are no longer valid
                if (m_DiscoveryServiceUriPattern != null
                    && !value.Equals(m_DiscoveryServiceUriPattern, StringComparison.CurrentCultureIgnoreCase))
                {
                    Debug.LogWarning($"{nameof(DiscoveryServiceUri)} changed; resetting discovery service");
                    Reset();
                }

                m_DiscoveryServiceUriPattern = value;
            }
        }

        /// <summary>
        ///     The ID of your multiplay fleet
        /// </summary>
        public string FleetId
        {
            get => m_FleetId;
            set
            {
                if(string.IsNullOrEmpty(value))
                    throw new ArgumentNullException(nameof(value), $"{nameof(m_FleetId)} cannot be null or empty.");

                // If trying to set the fleet ID to a new value,
                //  old / in-flight results are no longer valid
                if (m_FleetId != null
                    && !value.Equals(m_FleetId, StringComparison.CurrentCultureIgnoreCase))
                {
                    Debug.LogWarning($"{nameof(FleetId)} changed; resetting discovery service");
                    Reset();
                }

                m_FleetId = value;
            }
        }

        /// <summary>
        ///     The internal state of the qos request
        /// </summary>
        public DiscoveryState State { get; private set; } = DiscoveryState.NotStarted;

        /// <summary>
        ///     Whether or not the qos request is in a "done" state (success or failure)
        /// </summary>
        public bool IsDone => State == DiscoveryState.Done || State == DiscoveryState.Failed;

        /// <summary>
        ///     Populated with an error if the request has failed
        /// </summary>
        public string ErrorString { get; private set; }

        /// <summary>
        ///     Get a new copy of the cached QosServers
        /// </summary>
        public QosServer[] QosServers => GetCopyOfCachedQosServers();

        /// <summary>
        ///     Starts the QoS server discovery process
        /// </summary>
        /// <param name="fleetId">Multiplay Fleet ID where QoS servers are running</param>
        /// <param name="timeoutSeconds">Seconds to wait for a response before timing out</param>
        /// <param name="successHandler">Called when the result of discovery is success</param>
        /// <param name="errorHandler">Called when the result of discovery is an error or timeout</param>
        /// <remarks>
        ///     * QosDiscovery is not thread safe and does not support concurrent
        ///     Discovery requests. Calling StartDiscovery while another discovery
        ///     is outstanding will cancel the existing request and not trigger handlers.
        /// </remarks>
        public void StartDiscovery(string fleetId = null, int timeoutSeconds = 0, Action<QosServer[]> successHandler = null, Action<string> errorHandler = null)
        {
            // Fail fast - UnityWebRequest's SendWebRequest() only works on main thread
            ThrowExceptionIfNotOnMainThread();

            FleetId = fleetId ?? FleetId; // Will throw exception if final value of FleetId is null or empty
            OnSuccess = successHandler ?? OnSuccess;
            OnError = errorHandler ?? OnError;

            State = DiscoveryState.Running;

            var uri = string.Format(DiscoveryServiceUri, UnityWebRequest.EscapeURL(FleetId));

            // Deliver cached results if valid
            if (DateTime.UtcNow <= m_CacheExpireTimeUtc)
            {
                InvokeSuccessHandler();
                State = DiscoveryState.Done;
                return;
            }

            // A new request needs to be sent, so cancel any old requests in-flight
            DisposeDiscoveryRequest();
            ErrorString = null;

            // Set up a new request
            var timeout = timeoutSeconds > 0 ? timeoutSeconds : DiscoveryTimeoutSeconds;
            m_DiscoverQosAsyncOp = QosDiscoveryClient.GetQosServersAsync(uri, timeout, m_Etag);

            // Register completed handler for the discovery web request
            // Not a race condition (see https://docs.unity3d.com/ScriptReference/Networking.UnityWebRequestAsyncOperation.html)
            m_DiscoverQosAsyncOp.completed += OnDiscoveryCompleted; 
        }

        /// <summary>
        ///     Cancel the current in-progress or completed discovery
        /// </summary>
        /// <remarks>
        ///     * Clears the UnityWebRequestAsyncOperation, all callbacks, and sets the state back to NotStarted.
        ///     * Leaves the cache/etag values so starting a new discovery can take advantage of those values.
        /// </remarks>
        public void Cancel()
        {
            // Remove handlers
            OnSuccess = null;
            OnError = null;

            // Dispose of any discovery requests in-flight
            DisposeDiscoveryRequest();

            ErrorString = null;

            State = DiscoveryState.NotStarted;
        }

        /// <summary>
        ///     Cancel the current in-progress or completed discovery with cache disposal
        /// </summary>
        /// <remarks>
        ///     * Does everything Cancel() does, plus purges the cache
        /// </remarks>
        public void Reset()
        {
            Cancel();
            PurgeCache();
        }

        void PurgeCache()
        {
            m_Etag = null;
            m_QosServersCache = null;
            m_CacheExpireTimeUtc = DateTime.MinValue;
        }

        // Dispose of any discovery requests currently in-flight
        void DisposeDiscoveryRequest()
        {
            if (m_DiscoverQosAsyncOp != null)
            {
                m_DiscoverQosAsyncOp.completed -= OnDiscoveryCompleted;
                m_DiscoverQosAsyncOp.webRequest?.Dispose();
                m_DiscoverQosAsyncOp = null;
            }
        }

        void OnDiscoveryCompleted(AsyncOperation obj)
        {
            if (!(obj is UnityWebRequestAsyncOperation discoveryRequestOperation))
                throw new Exception("Wrong AsyncOperation type in callback");

            var discoveryRequest = discoveryRequestOperation.webRequest;

            try
            {
                // Short-circuit if request was cancelled
                if (m_DiscoverQosAsyncOp == null
                    || OnError == null
                    || OnSuccess == null
                    || discoveryRequestOperation != m_DiscoverQosAsyncOp)
                    return;

                // Handle failed WebRequest
                if (QosDiscoveryClient.IsWebRequestNullOrFailed(discoveryRequest))
                {
                    m_CacheExpireTimeUtc = DateTime.UtcNow.AddSeconds(FailureCacheTimeMs / 1000f);
                    InvokeErrorHandler($"Error discovering QoS servers. {discoveryRequest.error}");
                    return;
                }

                // Extract max-age and update the cache time
                if (QosDiscoveryClient.TryGetMaxAge(discoveryRequest, out var maxAgeSeconds))
                    m_CacheExpireTimeUtc = DateTime.UtcNow.AddSeconds(maxAgeSeconds);
                else
                    m_CacheExpireTimeUtc = DateTime.UtcNow.AddSeconds(SuccessCacheTimeMs / 1000f);

                // Update the cached server array if they have changed
                if (discoveryRequest.responseCode != (long)HttpStatusCode.NotModified)
                {
                    if (QosDiscoveryClient.TryGetEtag(discoveryRequest, out var etag))
                        m_Etag = etag;

                    if (QosDiscoveryClient.TryGetQosServersFromRequest(discoveryRequest, out m_QosServersCache))
                    {
                        InvokeSuccessHandler();
                        return;
                    }

                    // No servers were found, so assume that something may have gone wrong
                    //  Ignore cache control and reset m_CacheExpireTimeUtc to failure value
                    m_CacheExpireTimeUtc = DateTime.UtcNow.AddSeconds(FailureCacheTimeMs / 1000f);
                    InvokeErrorHandler("Error parsing discovery servers from response");
                    return;
                }

                InvokeSuccessHandler();
            }
            finally
            {
                if (discoveryRequestOperation == m_DiscoverQosAsyncOp)
                    DisposeDiscoveryRequest();
                else
                    discoveryRequest?.Dispose();
            }
        }

        void InvokeSuccessHandler()
        {
            State = DiscoveryState.Done;

            // Always return a copy of the results. The next discovery may return a 304 which expects us to have
            // the previous results available.  We don't want to have our cached data dirtied.
            OnSuccess?.Invoke(GetCopyOfCachedQosServers());
        }

        QosServer[] GetCopyOfCachedQosServers()
        {
            var qosServersCopy = new QosServer[m_QosServersCache?.Length ?? 0];
            m_QosServersCache?.CopyTo(qosServersCopy, 0);

            return qosServersCopy;
        }

        void InvokeErrorHandler(string error)
        {
            State = DiscoveryState.Failed;

            // Don't log here.  Let callback log the error if it wants.
            ErrorString = error;
            OnError?.Invoke(error);
        }

        // Throw an exception if not on the main thread
        // Many Unity methods can only be used from the main thread; this allows code to fail fast
        static void ThrowExceptionIfNotOnMainThread([CallerMemberName] string memberName = "")
        {
            if (Thread.CurrentThread != k_MainThread)
                throw new InvalidOperationException($"{memberName} must be called from the main thread.");
        }
    }
}
