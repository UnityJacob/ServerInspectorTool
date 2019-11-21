using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using UnityEngine.Networking;
using Debug = UnityEngine.Debug;
using Exception = System.Exception;

namespace Unity.Networking.QoS
{
    public static class QosDiscoveryClient
    {
        // ETag configuration 
        const string k_WeakValidatorPrefix = "W/\"";
        const string k_WeakValidatorSuffix = "\"";
        static readonly int k_WeakValidatorCharacterCount = k_WeakValidatorPrefix.Length + k_WeakValidatorSuffix.Length;

        // Wrapper that is required to get Unity's JSON deserializer to handle arrays of objects.
        // Do not change the case of the variable names. They map to JSON keys and the deserializer is case-sensitive.
        [Serializable]
        struct JsonServerWrapper
        {
            public QosServer[] servers;
        }

        /// <summary>
        ///     Send a request to get a list of available Qos servers for a specific URI.  Supports caching.
        /// </summary>
        /// <param name="uri">The URI to send the request to</param>
        /// <param name="timeout">(optional) The amount of time (in seconds) to wait before timing out the request</param>
        /// <param name="etag">(optional) ETag for cache control</param>
        /// <param name="useGzip">(optional) Enable gzip-encoded responses.  This reduces data transfer, but costs extra cpu cycles.</param>
        /// <returns>A UnityWebRequestAsyncOperation containing the UnityWebRequest for the Qos server query</returns>
        public static UnityWebRequestAsyncOperation GetQosServersAsync(string uri, int timeout = 0, string etag = null, bool useGzip = false)
        {
            if (string.IsNullOrEmpty(uri))
                throw new ArgumentNullException(nameof(uri), $"{nameof(uri)} cannot be null or empty");

            var request = new UnityWebRequest(uri, UnityWebRequest.kHttpVerbGET);
            request.SetRequestHeader("Accept", "application/json");
            request.downloadHandler = new DownloadHandlerBuffer();

            if(useGzip)
                request.SetRequestHeader("Accept-Encoding", "gzip");

            if (timeout > 0)
                request.timeout = timeout;

            if (!string.IsNullOrEmpty(etag))
                request.SetRequestHeader("If-None-Match", etag);

            return request.SendWebRequest();
        }

        /// <summary>
        ///     Try to populate a list of QosServers from the response of a previous GetQosServersAsync request
        /// </summary>
        /// <param name="webRequest">The request to parse the list of QosServers from</param>
        /// <param name="qosServers">The array to store the resulting QosServers (null if failed)</param>
        /// <param name="useGzip">(optional) Enable decoding gzip-encoded responses.  This reduces data transfer, but costs extra cpu cycles.</param>
        /// <returns>True if parsing successful, false if not</returns>
        public static bool TryGetQosServersFromRequest(UnityWebRequest webRequest, out QosServer[] qosServers, bool useGzip = false)
        {
            qosServers = null;

            if (webRequest == null)
                return false;

            if (webRequest.downloadHandler?.data?.Length > 0)
            {
                // Handle gzip case
                if (useGzip)
                {
                    var gzipHeader = webRequest.GetResponseHeader("Content-Encoding");
                    if (gzipHeader != null && gzipHeader.IndexOf("gzip", StringComparison.CurrentCultureIgnoreCase) != -1)
                        return TryGetQosServersFromGzip(webRequest.downloadHandler.data, out qosServers);
                }

                return TryGetQosServersFromJsonString(webRequest.downloadHandler.text, out qosServers);
            }
            
            return false;
        }

        public static bool TryGetQosServersFromGzip(byte[] gzipData, out QosServer[] qosServers)
        {
            qosServers = null;

            if (gzipData == null || gzipData.Length == 0)
                return false;

            try
            {
                using (var ms = new MemoryStream(gzipData))
                using (var gs = new GZipStream(ms, CompressionMode.Decompress))
                using (var sr = new StreamReader(gs))
                {
                    var decodedResult = sr.ReadToEnd();
                    return TryGetQosServersFromJsonString(decodedResult, out qosServers);
                }
            }
            catch (Exception e)
            {
                Debug.LogError("Unable to parse gzip stream: " + e.Message);
                return false;
            }
        }

        /// <summary>
        ///     Try to populate a list of QosServers from a string representing the body of a previous GetQosServersAsync request
        /// </summary>
        /// <param name="jsonEncodedString">The body of the request to parse the list of QosServers from</param>
        /// <param name="qosServers">The array to store the resulting QosServers (null if failed)</param>
        /// <returns>True if parsing successful, false if not</returns>
        public static bool TryGetQosServersFromJsonString(string jsonEncodedString, out QosServer[] qosServers)
        {
            qosServers = null;

            if (string.IsNullOrEmpty(jsonEncodedString))
                return false;

            try
            {
                var qosServersWrapper = JsonHelper.FromJson<JsonServerWrapper>(jsonEncodedString);

                // The parse succeeded, but there may be bad/missing data
                // Remove any invalid servers from the list
                RemoveInvalidServers(ref qosServersWrapper.servers);

                qosServers = qosServersWrapper.servers;

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        ///     Try to parse an ETag from the headers of a UnityWebRequest
        /// </summary>
        /// <param name="webRequest">The UnityWebRequest to check for an ETag</param>
        /// <param name="etag">The string to store the resulting ETag (null if not found)</param>
        /// <returns>True if header was found and could be parsed, false if not</returns>
        public static bool TryGetEtag(UnityWebRequest webRequest, out string etag)
        {
            etag = null;

            if (webRequest == null)
                return false;

            var etagHeader = webRequest.GetResponseHeader("ETag");

            if (string.IsNullOrEmpty(etagHeader))
                return false;

            // Strip Weak-validator if it exists
            if (etagHeader.StartsWith(k_WeakValidatorPrefix) && etagHeader.EndsWith(k_WeakValidatorSuffix))
            {
                etag = etagHeader.Substring(k_WeakValidatorPrefix.Length, etagHeader.Length - k_WeakValidatorCharacterCount);
                return true;
            }

            return false;
        }

        /// <summary>
        ///     Test to see if a UnityWebRequest is null or in a failure state
        /// </summary>
        /// <param name="webRequest">The UnityWebRequest to validate</param>
        /// <returns>True if UnityWebRequest is failed, false if not</returns>
        public static bool IsWebRequestNullOrFailed(UnityWebRequest webRequest)
        {
            return webRequest == null || webRequest.isNetworkError || webRequest.isHttpError;
        }

        /// <summary>
        ///     Try to parse the Cache-Control header from a UnityWebRequest and return the max-age value
        /// </summary>
        /// <param name="webRequest">The UnityWebRequest to parse</param>
        /// <param name="maxAge">The value to store the resulting max-age value in (0 if failed)</param>
        /// <returns>True if header was found and could be parsed; false if not</returns>
        public static bool TryGetMaxAge(UnityWebRequest webRequest, out int maxAge)
        {
            maxAge = 0;

            if (webRequest == null)
                return false;

            var cacheControl = webRequest.GetResponseHeader("Cache-Control");

            if (!string.IsNullOrEmpty(cacheControl))
            {
                foreach (var directive in cacheControl.Split(','))
                {
                    if (directive.IndexOf("max-age=", StringComparison.OrdinalIgnoreCase) != -1)
                    {
                        var maxAgeParts = directive.Split('=');
                        return int.TryParse(maxAgeParts[1].Trim(), out maxAge);
                    }
                }
            }

            return false;
        }

        // Remove invalid QosServers from a list of QosServers
        static void RemoveInvalidServers(ref QosServer[] qosServers)
        {
            if (qosServers == null)
                return;

            var validQosServers = new List<QosServer>(qosServers.Length);

            foreach (var server in qosServers)
            {
                if (IsValidServer(server))
                    validQosServers.Add(server);
            }

            if (validQosServers.Count != qosServers.Length)
                qosServers = validQosServers.ToArray();
        }

        // Check the validity of a QosServer
        static bool IsValidServer(QosServer server)
        {
            // QosJob requires a valid ipv4 and port.
            if (!IPAddress.TryParse(server.ipv4, out _))
                return false;

            if (server.port == 0)
                return false;

            // Matchmaking requires a valid regionid.
            if (string.IsNullOrEmpty(server.regionid))
                return false;

            // Everything else is (at the moment) irrelevant.
            return true;
        }
    }
}