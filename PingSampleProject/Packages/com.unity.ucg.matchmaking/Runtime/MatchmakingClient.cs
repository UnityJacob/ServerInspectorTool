using System;
using Google.Protobuf;
using UnityEngine.Networking;

namespace UnityEngine.Ucg.Matchmaking
{
    public class MatchmakingClient
    {
        const string k_AbortedWebRequestErrorText = "Request aborted";
        const string k_ApiVersion = "1";
        const string k_TicketsPath = "/tickets";
        const string k_ContentTypeProtobuf = "application/x-protobuf";
        const int k_DefaultCreateCallTimeoutSeconds = 30;
        const int k_DefaultGetCallTimeoutSeconds = 30;
        const int k_DefaultDeleteCallTimeoutSeconds = 30;
        static readonly string k_LogPre = $"[{nameof(MatchmakingClient)}] ";

        /// <param name="matchmakingServiceBaseUrl">
        ///     The base URL of the matchmaking service, in the form of 'cloud.connected.unity3d.com/[UPID]'
        /// </param>
        public MatchmakingClient(string matchmakingServiceBaseUrl)
        {
            if (string.IsNullOrEmpty(matchmakingServiceBaseUrl))
                throw new ArgumentException(k_LogPre + $"{nameof(matchmakingServiceBaseUrl)} must be a non-null, non-0-length string", nameof(matchmakingServiceBaseUrl));

            MatchmakingServiceUrl = BuildMatchmakingServiceUrl(matchmakingServiceBaseUrl);
            Debug.Log(k_LogPre + $"Created new {nameof(MatchmakingClient)} using Matchmaking URL {MatchmakingServiceUrl}");
        }

        /// <summary>
        ///     The formatted Matchmaking Service URL for this MatchmakingClient instance
        /// </summary>
        public string MatchmakingServiceUrl { get; }

        /// <summary>
        ///     <para>Send a Create Ticket (POST) request to the matchmaker, including custom request data.</para>
        ///     <para>On completion, response body will contain a unique Ticket ID, used for GET and DELETE calls</para>
        /// </summary>
        /// <param name="request">The request data to include with the request</param>
        /// <param name="timeout">The timeout for the underlying web request</param>
        /// <returns>
        ///     A UnityWebRequestAsyncOperation containing the underlying UnityWebRequest; can be yielded or used to track
        ///     completion of the request
        /// </returns>
        public UnityWebRequestAsyncOperation CreateTicketAsync(Protobuf.CreateTicketRequest request, int timeout = 0)
        {
            return CreateTicketAsync(MatchmakingServiceUrl, request, timeout);
        }

        /// <summary>
        ///     <para>Send a Get Ticket (GET) request to the matchmaker for a specific Ticket ID.</para>
        ///     <para>If an assignment is available for the ticket, it will be returned in the body of the response</para>
        /// </summary>
        /// <param name="ticketId">The ticket ID to include in the request</param>
        /// <param name="timeout">The timeout for the underlying web request</param>
        /// <returns>
        ///     A UnityWebRequestAsyncOperation containing the underlying UnityWebRequest; can be yielded or used to track
        ///     completion of the request
        /// </returns>
        public UnityWebRequestAsyncOperation GetTicketAsync(string ticketId, int timeout = 0)
        {
            return GetTicketAsync(MatchmakingServiceUrl, ticketId, timeout);
        }

        /// <summary>
        ///     <para>Send a Delete Ticket (DELETE) request to the matchmaker for a specific Ticket ID.</para>
        ///     <para>This removes the ticket from matchmaking.</para>
        /// </summary>
        /// <param name="ticketId">The ticket ID to include in the request</param>
        /// <param name="timeout">The timeout for the underlying web request</param>
        /// <returns>
        ///     A UnityWebRequestAsyncOperation containing the underlying UnityWebRequest; can be yielded or used to track
        ///     completion of the request
        /// </returns>
        public UnityWebRequestAsyncOperation DeleteTicketAsync(string ticketId, int timeout = 0)
        {
            return DeleteTicketAsync(MatchmakingServiceUrl, ticketId, timeout);
        }

        /// <param name="matchmakingServiceBaseUrl">
        ///     The base URL of the matchmaking service, in the form of
        ///     'cloud.connected.unity3d.com/[UPID]'
        /// </param>
        public static string BuildMatchmakingServiceUrl(string matchmakingServiceBaseUrl)
        {
            if (string.IsNullOrEmpty(matchmakingServiceBaseUrl))
                throw new ArgumentException(k_LogPre + $"{nameof(matchmakingServiceBaseUrl)} must be a non-null, non-0-length string", nameof(matchmakingServiceBaseUrl));

            var matchmakingServiceUrl = "https://" + matchmakingServiceBaseUrl + "/matchmaking/api/v" + k_ApiVersion;

            return matchmakingServiceUrl;
        }

        /// <summary>
        ///     <para>Send a Create Ticket (POST) request to the matchmaker, including custom request data.</para>
        ///     <para>On completion, response body will contain a unique Ticket ID, used for GET and DELETE calls</para>
        /// </summary>
        /// <param name="matchmakingServiceUrl">The matchmaker to send the request to</param>
        /// <param name="request">The request data to include with the request</param>
        /// <param name="timeout">The timeout for the underlying web request</param>
        /// <returns>
        ///     A UnityWebRequestAsyncOperation containing the underlying UnityWebRequest; can be yielded or used to track
        ///     completion of the request
        /// </returns>
        public static UnityWebRequestAsyncOperation CreateTicketAsync(string matchmakingServiceUrl, Protobuf.CreateTicketRequest request, int timeout = 0)
        {
            if (string.IsNullOrEmpty(matchmakingServiceUrl))
                throw new ArgumentException(k_LogPre + $"{nameof(matchmakingServiceUrl)} must be a non-null, non-0-length string", nameof(matchmakingServiceUrl));

            if (request == null)
                throw new ArgumentNullException(nameof(request), k_LogPre + $"{nameof(request)} must not be null");

            var url = matchmakingServiceUrl + k_TicketsPath;
            var webRequest = new UnityWebRequest(url, "POST");
            webRequest.SetRequestHeader("Accept", k_ContentTypeProtobuf);
            webRequest.SetRequestHeader("Content-Type", k_ContentTypeProtobuf);
            webRequest.downloadHandler = new DownloadHandlerBuffer();
            webRequest.timeout = timeout > 0 ? timeout : k_DefaultCreateCallTimeoutSeconds;

            // Upload body data if it exists (if sending no attributes and no properties, this may be empty)
            var requestBody = request.ToByteArray();

            if (requestBody.Length > 0)
                webRequest.uploadHandler = new UploadHandlerRaw(requestBody);

            return webRequest.SendWebRequest();
        }

        /// <summary>
        ///     <para>Send a Get Ticket (GET) request to the matchmaker for a specific Ticket ID.</para>
        ///     <para>If an assignment is available for the ticket, it will be returned in the body of the response</para>
        /// </summary>
        /// <param name="matchmakingServiceUrl">The matchmaker to send the request to</param>
        /// <param name="ticketId">The ticket ID to include in the request</param>
        /// <param name="timeout">The timeout for the underlying web request</param>
        /// <returns>
        ///     A UnityWebRequestAsyncOperation containing the underlying UnityWebRequest; can be yielded or used to track
        ///     completion of the request
        /// </returns>
        public static UnityWebRequestAsyncOperation GetTicketAsync(string matchmakingServiceUrl, string ticketId, int timeout = 0)
        {
            if (string.IsNullOrEmpty(matchmakingServiceUrl))
                throw new ArgumentException(k_LogPre + $"{nameof(matchmakingServiceUrl)} must be a non-null, non-0-length string", nameof(matchmakingServiceUrl));

            if (string.IsNullOrEmpty(ticketId))
                throw new ArgumentException(k_LogPre + $"{nameof(ticketId)} must be a non-null, non-empty", nameof(ticketId));

            var url = matchmakingServiceUrl + k_TicketsPath + "?id=" + ticketId;
            var webRequest = new UnityWebRequest(url, "GET");
            webRequest.SetRequestHeader("Accept", k_ContentTypeProtobuf);
            webRequest.SetRequestHeader("Content-Type", k_ContentTypeProtobuf);
            webRequest.downloadHandler = new DownloadHandlerBuffer();
            webRequest.timeout = timeout > 0 ? timeout : k_DefaultGetCallTimeoutSeconds;

            return webRequest.SendWebRequest();
        }

        /// <summary>
        ///     <para>Send a Delete Ticket (DELETE) request to the matchmaker for a specific Ticket ID.</para>
        ///     <para>This removes the ticket from matchmaking.</para>
        /// </summary>
        /// <param name="matchmakingServiceUrl">The matchmaker to send the request to</param>
        /// <param name="ticketId">The ticket ID to include in the request</param>
        /// <param name="timeout">The timeout for the underlying web request</param>
        /// <returns>
        ///     A UnityWebRequestAsyncOperation containing the underlying UnityWebRequest; can be yielded or used to track
        ///     completion of the request
        /// </returns>
        public static UnityWebRequestAsyncOperation DeleteTicketAsync(string matchmakingServiceUrl, string ticketId, int timeout = 0)
        {
            if (string.IsNullOrEmpty(matchmakingServiceUrl))
                throw new ArgumentException(k_LogPre + $"{nameof(matchmakingServiceUrl)} must be a non-null, non-0-length string", nameof(matchmakingServiceUrl));

            if (string.IsNullOrEmpty(ticketId))
                throw new ArgumentException(k_LogPre + $"{nameof(ticketId)} must be a non-null, non-empty guid", nameof(ticketId));

            var url = matchmakingServiceUrl + k_TicketsPath + "?id=" + ticketId;
            var webRequest = new UnityWebRequest(url, "DELETE");
            webRequest.SetRequestHeader("Accept", k_ContentTypeProtobuf);
            webRequest.SetRequestHeader("Content-Type", k_ContentTypeProtobuf);
            webRequest.downloadHandler = new DownloadHandlerBuffer();
            webRequest.timeout = timeout > 0 ? timeout : k_DefaultDeleteCallTimeoutSeconds;

            return webRequest.SendWebRequest();
        }

        /// <summary>
        ///     Determine if a UnityWebRequest is in a failed state
        /// </summary>
        /// <param name="request">The request to examine</param>
        /// <returns>TRUE if request is in a failed state</returns>
        public static bool IsWebRequestFailed(UnityWebRequest request)
        {
            if (request == null)
            {
                Debug.LogWarning(k_LogPre + $"{nameof(IsWebRequestFailed)} called on a null {nameof(UnityWebRequest)}");
                return true;
            }

            return request.isNetworkError || request.isHttpError;
        }

        /// <summary>
        ///     Determine if the Abort() method was called on a UnityWebRequest.
        ///     This is used to determine if a request was manually aborted (intentionally).
        /// </summary>
        /// <param name="request">The request to examine</param>
        /// <returns>TRUE if request was manually aborted</returns>
        public static bool IsWebRequestAborted(UnityWebRequest request)
        {
            if (request == null)
            {
                Debug.LogWarning(k_LogPre + $"{nameof(IsWebRequestAborted)} called on a null {nameof(UnityWebRequest)}");
                return true;
            }

            var aborted = request?.error != null
                && request.error.Equals(k_AbortedWebRequestErrorText);

            return aborted;
        }

        /// <summary>
        ///     Determine if a web request has already been sent over the wire to its destination.
        ///     CONNECT requests to HTTPS endpoints will not count as "sent".
        /// </summary>
        /// <param name="request">The request to examine</param>
        /// <returns>TRUE if a request has already been sent</returns>
        public static bool IsWebRequestSent(UnityWebRequestAsyncOperation request)
        {
            if (request?.webRequest == null)
                return false;

            // Because of the way UnityWebRequest works, if there's no data to upload,
            //  we can't be sure when the web request has actually been sent
            // Assume that the web request has already been sent
            if (request.webRequest.uploadHandler == null || request.webRequest.isDone)
                return true;

            // If there's an uploader attached, but the data hasn't been sent yet,
            //  the web request hasn't been transmitted yet
            return request.webRequest.uploadedBytes > 0
                || !Mathf.Approximately(0f, request.webRequest.uploadProgress);
        }

        /// <summary>
        ///     Try to parse the body of a UnityWebRequest into a CreateTicketResponse
        /// </summary>
        /// <param name="request">The UnityWebRequest to extract and parse the body data from</param>
        /// <param name="parsedResponse">The out var to store the resulting CreateTicketResponse</param>
        /// <returns>TRUE if successful, FALSE if unable to return a parsed result</returns>
        public static bool TryParseCreateTicketResponse(UnityWebRequest request, out Protobuf.CreateTicketResponse parsedResponse)
        {
            return TryParseCreateTicketResponse(request?.downloadHandler?.data, out parsedResponse);
        }

        /// <summary>
        ///     Try to parse the body of a UnityWebRequest into a GetTicketResponse
        /// </summary>
        /// <param name="request">The UnityWebRequest to extract and parse the body data from</param>
        /// <param name="parsedResponse">The out var to store the resulting GetTicketResponse</param>
        /// <returns>TRUE if successful, FALSE if unable to return a parsed result</returns>
        public static bool TryParseGetTicketResponse(UnityWebRequest request, out Protobuf.GetTicketResponse parsedResponse)
        {
            return TryParseGetTicketResponse(request?.downloadHandler?.data, out parsedResponse);
        }

        /// <summary>
        ///     Try to parse the body of a UnityWebRequest into a DeleteTicketResponse
        /// </summary>
        /// <param name="request">The UnityWebRequest to extract and parse the body data from</param>
        /// <param name="parsedResponse">The out var to store the resulting DeleteTicketResponse</param>
        /// <returns>TRUE if successful, FALSE if unable to return a parsed result</returns>
        public static bool TryParseDeleteTicketResponse(UnityWebRequest request, out Protobuf.DeleteTicketResponse parsedResponse)
        {
            return TryParseDeleteTicketResponse(request?.downloadHandler?.data, out parsedResponse);
        }

        /// <summary>
        ///     Try to parse a byte[] into a CreateTicketResponse
        /// </summary>
        /// <param name="data">The byte[] to parse into a CreateTicketResponse</param>
        /// <param name="parsedResponse">The out var to store the resulting CreateTicketResponse</param>
        /// <returns>TRUE if successful, FALSE if unable to return a parsed result</returns>
        public static bool TryParseCreateTicketResponse(byte[] data, out Protobuf.CreateTicketResponse parsedResponse)
        {
            return TryParseResponse(data, out parsedResponse);
        }

        /// <summary>
        ///     Try to parse a byte[] into a GetTicketResponse
        /// </summary>
        /// <param name="data">The byte[] to parse into a GetTicketResponse</param>
        /// <param name="parsedResponse">The out var to store the resulting GetTicketResponse</param>
        /// <returns>TRUE if successful, FALSE if unable to return a parsed result</returns>
        public static bool TryParseGetTicketResponse(byte[] data, out Protobuf.GetTicketResponse parsedResponse)
        {
            return TryParseResponse(data, out parsedResponse);
        }

        /// <summary>
        ///     Try to parse a byte[] into a DeleteTicketResponse
        /// </summary>
        /// <param name="data">The byte[] to parse into a DeleteTicketResponse</param>
        /// <param name="parsedResponse">The out var to store the resulting DeleteTicketResponse</param>
        /// <returns>TRUE if successful, FALSE if unable to return a parsed result</returns>
        public static bool TryParseDeleteTicketResponse(byte[] data, out Protobuf.DeleteTicketResponse parsedResponse)
        {
            return TryParseResponse(data, out parsedResponse);
        }

        // Try to parse a byte[] into a specific type
        static bool TryParseResponse<T>(byte[] data, out T parsedResponse) where T : IMessage
        {
            parsedResponse = default(T);
            object parsedData = null;
            string error = null;

            try
            {
                if (data == null || data.Length == 0)
                {
                    error = "No data to parse";
                    return false;
                }

                var type = typeof(T);

                if (type == typeof(Protobuf.GetTicketResponse))
                    parsedData = Protobuf.GetTicketResponse.Parser.ParseFrom(data);
                else if (type == typeof(Protobuf.CreateTicketResponse))
                    parsedData = Protobuf.CreateTicketResponse.Parser.ParseFrom(data);
                else if (type == typeof(Protobuf.DeleteTicketResponse))
                    parsedData = Protobuf.DeleteTicketResponse.Parser.ParseFrom(data);

                if (parsedData != null)
                {
                    parsedResponse = (T)parsedData;
                    return true;
                }

                error = "Parsed object was null";
                return false;
            }
            catch (Exception e)
            {
                error = e.Message;
                return false;
            }
            finally
            {
                if (error != null)
                    Debug.LogWarning(k_LogPre + $"{nameof(TryParseResponse)} was unable to parse the provided data into a ({typeof(T).Name}): {error}");
            }
        }
    }
}
