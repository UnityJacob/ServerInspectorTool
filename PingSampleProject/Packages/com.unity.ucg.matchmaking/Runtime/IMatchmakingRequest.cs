using System;
using System.Collections;

namespace UnityEngine.Ucg.Matchmaking
{
    public interface IMatchmakingRequest
    {
        /// <summary>
        ///     The ticket Id used to reference the request on the backend
        /// </summary>
        string TicketId { get; }

        /// <summary>
        ///     Whether or not the match request is finished
        /// </summary>
        bool IsDone { get; }

        /// <summary>
        ///     If the match request is in an error state, this will hold the associated error message
        /// </summary>
        string ErrorString { get; }

        /// <summary>
        ///     A yield-able class that can be used to await the completion of the match request
        /// </summary>
        IEnumerator WaitUntilCompleted { get; }

        /// <summary>
        ///     If the match request has completed, this will be populated with match assignment information
        /// </summary>
        Assignment Assignment { get; }

        /// <summary>
        ///     Event handler invoked when the request reaches a terminal state
        /// </summary>
        EventHandler<MatchmakingRequestCompletionArgs> Completed { get; set; }

        /// <summary>
        ///     Start a matchmaking request call
        /// </summary>
        IEnumerator SendRequest();

        /// <summary>
        ///     Cancel the current matchmaking request if it's in a cancellable state
        /// </summary>
        IEnumerator CancelRequest();

        /// <summary>
        ///     Poll for a match assignment if match request is in flight
        /// </summary>
        void Update();
    }

    public enum MatchmakingRequestState
    {
        Unknown = 0,
        NotStarted,
        Creating,
        Polling,
        Canceling,
        Canceled,
        TimedOut,
        AssignmentReceived,
        ErrorRequest,
        ErrorAssignment,
        Disposed,
    }

    public class MatchmakingRequestCompletionArgs : EventArgs
    {
        public string TicketId { get; }
        public MatchmakingRequestState State { get; }
        public Assignment Assignment { get; }
        public string Error { get; }

        public MatchmakingRequestCompletionArgs(string ticketId, MatchmakingRequestState state, Assignment assignment, string error)
        {
            this.TicketId = ticketId;
            this.State = state;
            this.Assignment = assignment;
            this.Error = error;
        }
    }
}
