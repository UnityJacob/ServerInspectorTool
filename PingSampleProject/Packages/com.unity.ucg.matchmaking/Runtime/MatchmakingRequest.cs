using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using Google.Protobuf;
using Unity.Ucg.MmConnector;
using UnityEngine.Networking;

namespace UnityEngine.Ucg.Matchmaking
{
    /// <summary>
    ///     A class which can request a match, manage the request lifecycle, and return match results on completion
    /// </summary>
    public class MatchmakingRequest : IMatchmakingRequest, IDisposable
    {
        const string k_Prefix = "[" + nameof(MatchmakingRequest) + "]";
        const float k_DefaultPollingIntervalSeconds = 2.0f;

        // Used to track whether code execution is happening on main thread
        static readonly Thread k_MainThread = Thread.CurrentThread;

        readonly Protobuf.CreateTicketRequest m_CreateTicketRequest;
        readonly MatchmakingClient m_Client;
        readonly uint m_MatchRequestTimeoutMs;

        MatchmakingRequestYielder m_Awaiter;
        UnityWebRequestAsyncOperation m_CreateTicketAsyncOperation;
        UnityWebRequestAsyncOperation m_GetTicketAsyncOperation;
        UnityWebRequestAsyncOperation m_DeleteTicketAsyncOperation;

        float m_LastPollTime;
        Stopwatch m_RequestTimer;
        bool m_Disposed;
        string logPre => k_Prefix + (TicketId == null ? " " : $"[{TicketId}] ");

        /// <summary>
        ///     Initialize a new MatchmakingRequest object
        /// </summary>
        /// <param name="endpoint">
        ///     The base URL of the matchmaking service, in the form of 'cloud.connected.unity3d.com/[UPID]'
        /// </param>
        /// <param name="request">
        ///     The ticket data to send with the matchmaking request.
        ///     Data will be copied internally on construction and treated as immutable.
        /// </param>
        /// <param name="timeoutMs">
        ///     The amount of time to wait (in ms) before aborting an incomplete MatchmakingRequest after it has been sent.
        ///     Match requests that time out on the client side will immediately be set to completed and stop listening for a match
        ///     assignment.
        /// </param>
        public MatchmakingRequest(string endpoint, CreateTicketRequest request, uint timeoutMs = 0)
            : this(new MatchmakingClient(endpoint), request, timeoutMs) { }

        /// <summary>
        ///     Initialize a new MatchmakingRequest object
        /// </summary>
        /// <param name="client">
        ///     An already existing MatchmakingClient to use as the client for the request
        /// </param>
        /// <param name="request">
        ///     The ticket data to send with the matchmaking request.
        ///     Data will be copied internally on construction and treated as immutable.
        /// </param>
        /// <param name="timeoutMs">
        ///     The amount of time to wait (in ms) before aborting an incomplete MatchmakingRequest after it has been sent.
        ///     Match requests that time out on the client side will immediately be set to completed and stop listening for a match
        ///     assignment.
        /// </param>
        public MatchmakingRequest(MatchmakingClient client, CreateTicketRequest request, uint timeoutMs = 0)
        {
            m_Client = client
                ?? throw new ArgumentNullException(nameof(client), logPre + $"Matchmaking {nameof(client)} must not be null");

            if(request == null)
                throw new ArgumentNullException(nameof(request), logPre + $"{nameof(request)} must be a non-null, valid {nameof(CreateTicketRequest)} object");

            // Try to immediately create and store the protobuf version of the CreateTicketRequest
            //  This allows us to fail fast, and also copies the data to prevent it from being mutable
            //  This may cause exceptions inside the protobuf code, which is fine since we're in the constructor
            var createTicketRequest = new Protobuf.CreateTicketRequest();


            string key = nameof(QosTicketInfo).ToLower();
            if (request.Properties != null && !request.Properties.ContainsKey(key))
            {
                QosTicketInfo results = QosConnector.Instance.Execute();
                if (results?.QosResults?.Count > 0)
                {
                    request.Properties = request.Properties ?? new Dictionary<string, string>();
                    request.Properties.Add(key, JsonUtility.ToJson(results));
                }
            }

            // Only set properties if not null
            // Request properties have to be massaged to be protobuf ByteString compatible
            if (request.Properties != null)
            {
                foreach (var kvp in request.Properties)
                {
                    var keyToLower = kvp.Key.ToLower();

#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    if (!kvp.Key.Equals(keyToLower))
                        Debug.LogWarning(logPre + $"Ticket property with key {kvp.Key} must be all lowercase; changing in-place.");
#endif
                    createTicketRequest.Properties.Add(keyToLower, ByteString.CopyFrom(Encoding.UTF8.GetBytes(kvp.Value)));
                }
            }

            // Only add attributes if they exist
            if(request?.Attributes?.Count > 0)
                createTicketRequest.Attributes.Add(request.Attributes);

            m_CreateTicketRequest = createTicketRequest;

            State = MatchmakingRequestState.NotStarted;

            m_MatchRequestTimeoutMs = timeoutMs;
        }

        /// <summary>
        ///     The number of seconds to wait between poll requests to get ticket state
        /// </summary>
        public float GetTicketPollIntervalSeconds { get; set; } = k_DefaultPollingIntervalSeconds;

        /// <summary>
        ///     The state of this MatchmakingRequest
        /// </summary>
        public MatchmakingRequestState State { get; private set; }

        /// <summary>
        ///     <para>Dispose the MatchmakingRequest and release resources.  All in-flight web requests will be disposed regardless of state.
        ///     Object methods will no-op after disposal.</para>
        ///     <para>Best practice is to ensure that Cancel() has been called and completed before calling Dispose().</para>
        /// </summary>
        public void Dispose()
        {
            if (!m_Disposed)
            {
                // Dispose any leftover web requests; most of these should be disposed of elsewhere
                // This is just a catch-all in case we Dispose() in the middle of a request

                var createInFlight = false;
                var getInFlight = false;
                var deleteInFlight = false;

                if (m_CreateTicketAsyncOperation != null)
                {
                    createInFlight = MatchmakingClient.IsWebRequestSent(m_CreateTicketAsyncOperation);
                    m_CreateTicketAsyncOperation.completed -= OnCreateTicketAsyncCompleted;
                    m_CreateTicketAsyncOperation.webRequest?.Dispose();
                    m_CreateTicketAsyncOperation = null;
                }

                if (m_GetTicketAsyncOperation != null)
                {
                    getInFlight = State == MatchmakingRequestState.Polling;
                    m_GetTicketAsyncOperation.completed -= OnGetTicketAsyncCompleted;
                    m_GetTicketAsyncOperation.webRequest?.Dispose();
                    m_GetTicketAsyncOperation = null;
                }

                if (m_DeleteTicketAsyncOperation != null)
                {
                    deleteInFlight = !m_DeleteTicketAsyncOperation.isDone;
                    m_DeleteTicketAsyncOperation.completed -= OnDeleteTicketAsyncCompleted;
                    m_DeleteTicketAsyncOperation.webRequest?.Dispose();
                    m_DeleteTicketAsyncOperation = null;
                }

                if ((createInFlight || getInFlight) && State != MatchmakingRequestState.Canceled)
                    Debug.LogWarning(logPre +
                        $"{nameof(MatchmakingRequest)} was terminated without being deleted." +
                        "  This may cause ghost tickets in the matchmaker.");

                if (deleteInFlight)
                    Debug.LogWarning(logPre +
                        $"{nameof(MatchmakingRequest)} was terminated while a Delete request was in flight" +
                        "; Delete may not be processed by the matchmaker.");

                if (!IsDone)
                {
                    State = MatchmakingRequestState.Disposed;
                    SetTerminalState();
                }

                m_Disposed = true;
            }
        }

        /// <summary>
        ///     A yield-able class that can be used to await the completion of the match request
        /// </summary>
        public IEnumerator WaitUntilCompleted
        {
            get
            {
                m_Awaiter = m_Awaiter ?? new MatchmakingRequestYielder(this);
                return m_Awaiter;
            }
        }

        /// <summary>
        ///     The matchmaking Ticket Id used to reference this request on the backend
        /// </summary>
        public string TicketId { get; private set; }

        /// <summary>
        ///     TRUE if the MatchmakingRequest is finished and in a terminal state; FALSE otherwise
        /// </summary>
        public bool IsDone { get; private set; }

        /// <summary>
        ///     If the MatchmakingRequest is in a terminal error state, this will hold the associated error message
        /// </summary>
        public string ErrorString { get; private set; }

        /// <summary>
        ///     If the MatchmakingRequest has completed and a match assignment has been returned, this will be populated the
        ///     assignment
        /// </summary>
        public Assignment Assignment { get; private set; }

        /// <summary>
        ///     Event handler invoked when the request reaches a terminal state
        /// </summary>
        public EventHandler<MatchmakingRequestCompletionArgs> Completed { get; set; }

        /// <summary>
        ///     <para>Start the matchmaking request.</para>
        ///     <para>
        ///         This contacts the matchmaking service, requests a unique ID for the match request (TicketId),
        ///         and waits for the service to return a match assignment.
        ///     </para>
        /// </summary>
        /// <returns>A yield-able object that can be used to await the completion of the request</returns>
        public IEnumerator SendRequest()
        {
            // UnityWebRequest calls can only be made from the Main thread
            ThrowExceptionIfNotOnMainThread();
            ThrowExceptionIfNotInState(nameof(SendRequest), MatchmakingRequestState.NotStarted);

            try
            {
                // Send the Create request - Throws errors if request is invalid
                m_CreateTicketAsyncOperation = m_Client.CreateTicketAsync(m_CreateTicketRequest);
                m_CreateTicketAsyncOperation.completed += OnCreateTicketAsyncCompleted;
            }
            catch (Exception e)
            {
                // This is probably fatal, so clean up the request
                HandleError($"Error starting matchmaking: {e.Message}");

                // Re-throw the exception
                throw;
            }

            State = MatchmakingRequestState.Creating;
            Debug.Log(logPre + $"{nameof(SendRequest)} called, requesting new Ticket ID from {m_Client.MatchmakingServiceUrl}");

            if(m_MatchRequestTimeoutMs > 0)
                m_RequestTimer = Stopwatch.StartNew();

            return WaitUntilCompleted;
        }

        /// <summary>
        ///     <para>Cancel searching for a match, and de-register this request with the matchmaking service.</para>
        ///     <para>May only be called after Start() has been called.</para>
        ///     <para>
        ///         If a match assignment is found after Cancel() is called (but before it is processed by the matchmaker),
        ///         the request will still be canceled and the success handler will not trigger.
        ///     </para>
        ///     <para>Must be called from the main thread.</para>
        /// </summary>
        public IEnumerator CancelRequest()
        {
            // UnityWebRequest calls can only be made from the Main thread
            ThrowExceptionIfNotOnMainThread();

            if (!InState(MatchmakingRequestState.NotStarted, MatchmakingRequestState.Creating, MatchmakingRequestState.Polling))
            {
                Debug.LogError(logPre + $"Trying to call {nameof(CancelRequest)} in an invalid state ({State})");
                return null;
            }

            Debug.Log(logPre + "Canceling matchmaking ticket");

            switch (State)
            {
                // If we're not started, we can just set the object state to cancelled immediately
                case MatchmakingRequestState.NotStarted:
                    Debug.Log(logPre + $"Matchmaking aborted before {nameof(SendRequest)} was called");
                    HandleDelete();
                    break;

                // If we're in Polling and have a ticket ID, try to do a delete
                case MatchmakingRequestState.Polling:
                    DeleteMatchRequestTicket();
                    break;

                // If we're in Creating, try to cancel the Create request if it hasn't actually been sent yet
                //  If it has, then queue up a cancellation request for once we have a valid ticket id
                case MatchmakingRequestState.Creating:
                    State = MatchmakingRequestState.Canceling;

                    if (!MatchmakingClient.IsWebRequestSent(m_CreateTicketAsyncOperation))
                    {
                        m_CreateTicketAsyncOperation?.webRequest?.Abort();
                        Debug.LogWarning(logPre + "Attempting to abort match request while Create call is still pending");
                    }
                    else
                    {
                        Debug.LogWarning(logPre + "Match request does not have a TicketId yet; queuing cancel request");
                    }

                    break;

                default:
                    throw new InvalidOperationException(logPre + $"Trying to call {nameof(CancelRequest)} in an invalid state ({State})");
            }

            return WaitUntilCompleted;
        }

        /// <summary>
        ///     Ticks the MatchRequest state machine.
        ///     Polls for a match assignment if one has been requested and not yet received and it is possible to do so.
        ///     No-ops if Start() has not been called yet or the MatchRequest is in a terminal state.
        /// </summary>
        public void Update()
        {
            // UnityWebRequest calls can only be made from the Main thread
            ThrowExceptionIfNotOnMainThread();

            if (IsDone)
                return;

            // Abort match request if we timed out
            if (m_RequestTimer != null && m_RequestTimer.ElapsedMilliseconds > m_MatchRequestTimeoutMs)
            {
                State = MatchmakingRequestState.TimedOut;
                Debug.LogError(logPre + $"Matchmaking request timed out");
                SetTerminalState();
                return;
            }

            if (ShouldPoll())
                StartGetTicketRequest(TicketId);
        }

        // Callback for "Create Ticket" UnityWebRequestAsyncOperation completion
        void OnCreateTicketAsyncCompleted(AsyncOperation obj)
        {
            if (!(obj is UnityWebRequestAsyncOperation createTicketOp))
                throw new ArgumentException(logPre + "Wrong AsyncOperation type in callback.");

            Protobuf.CreateTicketResponse result;

            // Every return statement in here will trigger finally{} cleanup
            try
            {
                // Short-circuit if we're in a terminal state or have no creation registered
                if (IsDone || m_CreateTicketAsyncOperation == null)
                    return;

                if (createTicketOp != m_CreateTicketAsyncOperation)
                    throw new InvalidOperationException(logPre + $"Wrong operation object received by {nameof(OnCreateTicketAsyncCompleted)}.");

                if (State != MatchmakingRequestState.Creating && State != MatchmakingRequestState.Canceling)
                {
                    Debug.LogWarning(logPre + "Ignoring Matchmaking Create Ticket response while not in creating state.");
                    return;
                }

                if (MatchmakingClient.IsWebRequestFailed(createTicketOp.webRequest))
                {
                    // If we tried to cancel while the Create call was being constructed or in flight,
                    //   the Create request will be set to an aborted state (counts as an "IsFailed" state)
                    if (State == MatchmakingRequestState.Canceling && MatchmakingClient.IsWebRequestAborted(createTicketOp.webRequest))
                    {
                        //if (MatchmakingClient.IsWebRequestSent(createTicketOp))
                            //Debug.LogWarning(logPre + "Matchmaking call was aborted, but ticket may have been created on the service end.");

                        Debug.Log(logPre + "Matchmaking was aborted during ticket creation");

                        HandleDelete();
                        return;
                    }

                    HandleError($"Error creating matchmaking ticket: {createTicketOp.webRequest.error}");
                    return;
                }

                // Parse the body of the response - only try if we actually have a body
                if (!MatchmakingClient.TryParseCreateTicketResponse(createTicketOp.webRequest, out result))
                {
                    // Could not parse the CREATE response; this is a fatal error
                    HandleError($"Error parsing CREATE response for ticket; could not get Ticket ID from service");
                }
            }
            catch (Exception e)
            {
                HandleError($"Error creating matchmaking ticket: {e.Message}");
                return;
            }
            finally
            {
                // Allow the operation to get garbage collected
                if (createTicketOp == m_CreateTicketAsyncOperation)
                    m_CreateTicketAsyncOperation = null;

                createTicketOp.webRequest?.Dispose();
            }

            // Try to consume the parsed response

            if (result == null)
            {
                HandleError("Error creating matchmaking ticket.");
                return;
            }

            if (result.Status != null)
            {
                HandleError($"Error creating matchmaking ticket. Code {result.Status.Code}: {result.Status.Message}");
                return;
            }

            if (string.IsNullOrEmpty(result.Id))
            {
                HandleError("Error creating matchmaking ticket. Id not set.");
                return;
            }

            // We were able to parse the Ticket Id
            TicketId = result.Id;

            // If a cancellation request is queued, send the cancellation instead of polling for assignment
            if (State == MatchmakingRequestState.Canceling)
            {
                DeleteMatchRequestTicket();
                return;
            }

            // Start polling for the assignment for the assigned Ticket Id
            Debug.Log(logPre + $"Ticket ID received; polling matchmaking for assignment");
            StartGetTicketRequest(TicketId);
        }

        // Callback for "Get Ticket" UnityWebRequestAsyncOperation completion
        void OnGetTicketAsyncCompleted(AsyncOperation obj)
        {
            if (!(obj is UnityWebRequestAsyncOperation getTicketOp))
                throw new ArgumentException(logPre + "Wrong AsyncOperation type in callback.");

            Protobuf.GetTicketResponse getResponse;

            // Every return statement in here will trigger finally{} cleanup
            try
            {
                // Short-circuit if we're in a terminal state or have no Get call registered
                if (IsDone || m_GetTicketAsyncOperation == null)
                    return;

                if (getTicketOp != m_GetTicketAsyncOperation)
                    throw new InvalidOperationException(logPre + $"Wrong operation object received by {nameof(OnGetTicketAsyncCompleted)}.");

                // No need to log on an expected abort state
                if (State == MatchmakingRequestState.Canceling || MatchmakingClient.IsWebRequestAborted(getTicketOp.webRequest))
                    return;

                // This helps to ensure that GET calls aborted during a delete are not processed
                if (State != MatchmakingRequestState.Polling)
                {
                    Debug.LogWarning(logPre + "Ignoring Matchmaking Get Ticket response while not in polling state.");
                    return;
                }

                if (MatchmakingClient.IsWebRequestFailed(getTicketOp.webRequest))
                {
                    HandleError($"Error getting matchmaking ticket: {getTicketOp.webRequest.error}");
                    return;
                }

                // Parse the body of the response - only try if we actually have a body
                // Successful responses w/o bodies are not considered failures
                if (getTicketOp.webRequest?.downloadHandler?.data?.Length == 0)
                    return;

                // Try to parse body
                if (!MatchmakingClient.TryParseGetTicketResponse(getTicketOp.webRequest, out getResponse))
                {
                    // Body was present but we couldn't parse it; this is probably a fatal error
                    HandleError($"Error parsing GET response for ticket");
                    return;
                }
            }
            catch (Exception e)
            {
                HandleError($"Error getting information for ticket: {e.Message}");
                return;
            }
            finally
            {
                // Allow the operation to get garbage collected
                if (getTicketOp == m_GetTicketAsyncOperation)
                {
                    m_GetTicketAsyncOperation = null;
                    m_LastPollTime = Time.unscaledTime;
                }

                getTicketOp.webRequest?.Dispose();
            }

            // Consume the response

            if (getResponse.Status != null)
            {
                HandleError($"Error getting matchmaking ticket. Code {getResponse.Status.Code}: {getResponse.Status.Message}");
                return;
            }

            // Check to see if this GET call has an assignment
            //  If assignment is null, ticket hasn't completed matchmaking yet
            if (getResponse.Assignment != null)
            {
                var errorExists = !string.IsNullOrEmpty(getResponse.Assignment.Error);
                var connectionExists = !string.IsNullOrEmpty(getResponse.Assignment.Connection);
                var propertiesExists = !string.IsNullOrEmpty(getResponse.Assignment.Properties);

                // Note that assignment can have null fields
                Assignment = new Assignment(getResponse.Assignment.Connection, getResponse.Assignment.Error, getResponse.Assignment.Properties);

                // Set to ErrorRequest state if assignment has no real data
                if (!errorExists && !connectionExists && !propertiesExists)
                {
                    HandleError("Error getting matchmaking ticket: assignment returned by service could not be processed");
                    return;
                }

                // Set to ErrorAssignment state if parsed assignment object contains an error entry
                if (errorExists)
                {
                    State = MatchmakingRequestState.ErrorAssignment;
                    Debug.LogError(logPre + $"Matchmaking completed with Assignment error: {Assignment.Error}");
                    SetTerminalState();
                    return;
                }

                // No error and valid connection and/or properties - set to AssignmentReceived
                State = MatchmakingRequestState.AssignmentReceived;
                Debug.Log(logPre + $"Matchmaking completed successfully; connection information received.");
                SetTerminalState();
            }
        }

        // Callback for "Delete Ticket" UnityWebRequestAsyncOperation completion
        void OnDeleteTicketAsyncCompleted(AsyncOperation obj)
        {
            if (!(obj is UnityWebRequestAsyncOperation deleteTicketOp))
                throw new ArgumentException(logPre + "Wrong AsyncOperation type in callback.");

            // Every return statement in here will trigger finally{} cleanup
            try
            {
                // Short-circuit if we're in a terminal state or have no deletion registered
                if (IsDone || m_DeleteTicketAsyncOperation == null)
                    return;

                if (deleteTicketOp != m_DeleteTicketAsyncOperation)
                    throw new InvalidOperationException(logPre + $"Wrong operation object received by {nameof(OnDeleteTicketAsyncCompleted)}.");

                // If the request here was a success, we have successfully deleted a ticket
                if (MatchmakingClient.IsWebRequestFailed(deleteTicketOp.webRequest))
                {
                    Debug.LogError(logPre + $"Error deleting matchmaking ticket: {deleteTicketOp.webRequest.error}");
                    return;
                }

                // Everything after this is just to detect if there was additional information
                //  provided in the body of the DELETE response

                // If no body, we're done
                if (deleteTicketOp.webRequest?.downloadHandler?.data?.Length == 0)
                    return;

                // Try to parse body
                if (!MatchmakingClient.TryParseDeleteTicketResponse(deleteTicketOp.webRequest, out var result))
                {
                    // Delete succeeded but additional information could not be parsed; not a fatal error
                    Debug.LogError(logPre + $"Error parsing DELETE information for ticket");
                    return;
                }

                // Handle the status code returned inside the DELETE body
                if (result?.Status != null && result.Status.Code != 0)
                {
                    Debug.LogError(logPre + $"Error deleting matchmaking ticket. Code {result.Status.Code}: {result.Status.Message}");
                    return;
                }

                // Success!
                Debug.Log(logPre + $"Matchmaking ticket deleted successfully");
            }
            catch (Exception e)
            {
                Debug.LogError(logPre + $"Error deleting matchmaking ticket: {e.Message}");
            }
            finally
            {
                // Allow the operation to get garbage collected
                if (deleteTicketOp == m_DeleteTicketAsyncOperation)
                    m_DeleteTicketAsyncOperation = null;

                deleteTicketOp.webRequest?.Dispose();

                // Handle failed deletion cases as a successful cancel
                HandleDelete();
            }
        }

        // Attempt to cancel the current request; changes state to Canceling and invokes cancellation callback on completion
        void DeleteMatchRequestTicket()
        {
            // Can only be called from the main thread
            ThrowExceptionIfNotOnMainThread();

            if (IsDone)
                throw new InvalidOperationException(logPre + $"Trying to call {nameof(DeleteMatchRequestTicket)} on a disposed {nameof(MatchmakingRequest)} object");

            if (string.IsNullOrEmpty(TicketId))
                throw new InvalidOperationException(logPre + $"Trying to delete a matchmaking ticket when {nameof(TicketId)} is not populated");

            // Ignore duplicate calls
            if (m_DeleteTicketAsyncOperation != null && !m_DeleteTicketAsyncOperation.isDone)
            {
                Debug.LogWarning(logPre + $"Duplicate request to delete matchmaking ticket was ignored");
                return;
            }

            State = MatchmakingRequestState.Canceling;

            // Abort any existing polling request
            //  This will cause that operation to trigger the completion handler with a failure and an "aborted" message
            m_GetTicketAsyncOperation?.webRequest?.Abort();

            // Send the Delete request
            m_DeleteTicketAsyncOperation = m_Client.DeleteTicketAsync(TicketId);
            m_DeleteTicketAsyncOperation.completed += OnDeleteTicketAsyncCompleted;
        }

        // Start a new "Get Ticket" UnityWebRequestAsyncOperation
        void StartGetTicketRequest(string ticketId)
        {
            // UnityWebRequest calls can only be made from the Main thread
            ThrowExceptionIfNotOnMainThread();

            if (IsDone)
                throw new InvalidOperationException(logPre + $"Trying to call {nameof(StartGetTicketRequest)} on a disposed {nameof(MatchmakingRequest)} object");

            // Ignore duplicate calls
            if (m_GetTicketAsyncOperation != null && !m_GetTicketAsyncOperation.isDone)
            {
                Debug.LogWarning(logPre + $"Call to {nameof(StartGetTicketRequest)} was ignored due to existing request still in-flight");
                return;
            }

            //Debug.Log(logPre + $"Polling matchmaking for assignment");

            State = MatchmakingRequestState.Polling;
            m_LastPollTime = Time.unscaledTime;

            // Send the Get request
            m_GetTicketAsyncOperation = m_Client.GetTicketAsync(ticketId);
            m_GetTicketAsyncOperation.completed += OnGetTicketAsyncCompleted;
        }

        // Set the object to the (fatal) Error terminal state and invoke any registered error handlers
        void HandleError(string error)
        {
            if (IsDone)
                return;

            State = MatchmakingRequestState.ErrorRequest;
            ErrorString = error;
            Debug.LogError(logPre + error);

            // Cleanup
            SetTerminalState();
        }

        // Set the object to the Canceled terminal state and invoke any registered cancellation handlers
        void HandleDelete()
        {
            if (IsDone)
                return;

            State = MatchmakingRequestState.Canceled;
            SetTerminalState();
        }

        // Clean up object state and release resources when reaching terminal state
        void SetTerminalState()
        {
            if (IsDone)
                throw new InvalidOperationException(logPre + $"{nameof(SetTerminalState)} called more than once");

            // Only allow terminal state setting if the state is correct
            ThrowExceptionIfNotInState(nameof(SetTerminalState),
                MatchmakingRequestState.Disposed, MatchmakingRequestState.Canceled,
                MatchmakingRequestState.ErrorAssignment, MatchmakingRequestState.ErrorRequest,
                MatchmakingRequestState.TimedOut, MatchmakingRequestState.AssignmentReceived);

            IsDone = true;
            m_RequestTimer?.Stop();

            // Having an object dispose itself is normally bad, but in this case we are 100% sure
            //  that the caller doesn't need the managed resources that we're disposing of
            Dispose();

            // Invoke Completed handler if registered
            Completed?.Invoke(this, new MatchmakingRequestCompletionArgs(TicketId, State, Assignment, ErrorString));
        }

        // Tests polling constraints of poll rate, current request status, and current state
        bool ShouldPoll()
        {
            var shouldPoll =
                State == MatchmakingRequestState.Polling
                && m_GetTicketAsyncOperation == null
                && !string.IsNullOrEmpty(TicketId)
                && (Time.unscaledTime < m_LastPollTime // Time overflow
                    || Time.unscaledTime - m_LastPollTime >= GetTicketPollIntervalSeconds); // Exceeded interval

            return shouldPoll;
        }

        // Throw an error if this MatchRequest is not in a specific state or set of states
        void ThrowExceptionIfNotInState(string memberName = "", params MatchmakingRequestState[] states)
        {
            if (InState(states))
                return;

            throw new InvalidOperationException(logPre + $"Trying to call {memberName} in an invalid state ({State})");
        }

        // Throw an error if this MatchRequest is not in a specific state or set of states
        bool InState(params MatchmakingRequestState[] states)
        {
            foreach (var state in states)
                if (state == State)
                    return true;

            return false;
        }

        // Throw an exception if not on the main thread
        // Many Unity methods can only be used from the main thread; this allows code to fail fast
        static void ThrowExceptionIfNotOnMainThread([CallerMemberName] string memberName = "")
        {
            if (Thread.CurrentThread != k_MainThread)
                throw new InvalidOperationException($"[{nameof(MatchmakingRequest)}] {memberName} must be called from the main thread.");
        }

        /// <summary>
        ///     A class that can be used to yield for the completion of a matchmaking request.
        ///     It will automatically tick the request's Update() method during each yield check.
        /// </summary>
        public class MatchmakingRequestYielder : CustomYieldInstruction
        {
            IMatchmakingRequest m_Request;

            public override bool keepWaiting
            {
                get
                {
                    if (m_Request == null)
                        return false;

                    // Tick the request's Update() method every time the yield is processed
                    m_Request.Update();

                    return !m_Request.IsDone;
                }
            }

            public MatchmakingRequestYielder(IMatchmakingRequest request)
            {
                m_Request = request;
            }
        }
    }
}
