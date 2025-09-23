namespace FFXIManager.Models;

/// <summary>
/// Represents the execution state of the auto-login queue.
/// This enum provides explicit state management to eliminate race conditions
/// and improve UI state synchronization.
/// </summary>
public enum QueueExecutionState
{
    /// <summary>
    /// Queue is idle and ready to start
    /// </summary>
    Idle,

    /// <summary>
    /// Queue is starting up and initializing
    /// </summary>
    Starting,

    /// <summary>
    /// Queue is actively processing an item
    /// </summary>
    Processing,

    /// <summary>
    /// Queue is transitioning between items (after skip/complete)
    /// </summary>
    Transitioning,

    /// <summary>
    /// Queue execution has been paused by user
    /// </summary>
    Paused,

    /// <summary>
    /// Queue is stopping and cleaning up
    /// </summary>
    Stopping,

    /// <summary>
    /// Queue has completed all items
    /// </summary>
    Completed
}