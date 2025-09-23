namespace FFXIManager.Models
{
    /// <summary>
    /// Status of an auto-login queue item
    /// </summary>
    public enum AutoLoginQueueStatus
    {
        /// <summary>
        /// Queue item is waiting to be processed
        /// </summary>
        Pending,

        /// <summary>
        /// Queue item is currently being processed
        /// </summary>
        InProgress,

        /// <summary>
        /// Queue item processing has been paused by user
        /// </summary>
        Paused,

        /// <summary>
        /// Queue item has completed successfully
        /// </summary>
        Completed,

        /// <summary>
        /// Queue item processing failed with an error
        /// </summary>
        Failed,

        /// <summary>
        /// Queue item processing was cancelled by user
        /// </summary>
        Cancelled
    }
}