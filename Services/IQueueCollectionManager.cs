using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using FFXIManager.Models;

namespace FFXIManager.Services
{
    /// <summary>
    /// Service for managing queue collection operations (add, remove, reorder)
    /// </summary>
    public interface IQueueCollectionManager
    {
        #region Properties

        /// <summary>
        /// Observable collection of queue items
        /// </summary>
        ObservableCollection<AutoLoginQueueItem> QueueItems { get; }

        /// <summary>
        /// Total number of items in the queue
        /// </summary>
        int TotalItems { get; }

        /// <summary>
        /// Number of completed items
        /// </summary>
        int CompletedItems { get; }

        /// <summary>
        /// Number of failed items
        /// </summary>
        int FailedItems { get; }

        /// <summary>
        /// Number of cancelled/skipped items
        /// </summary>
        int CancelledItems { get; }

        /// <summary>
        /// Total number of processed items (completed + failed + cancelled)
        /// </summary>
        int ProcessedItems { get; }

        /// <summary>
        /// Overall progress percentage (0-100)
        /// </summary>
        int OverallProgress { get; }

        #endregion

        #region Collection Operations

        /// <summary>
        /// Checks if the specified account is already in the queue
        /// </summary>
        bool IsAccountAlreadyQueued(PlayOnlineMemberAccount account);

        /// <summary>
        /// Adds an account to the queue
        /// </summary>
        /// <param name="account">PlayOnline Member Account to add</param>
        /// <param name="profile">Profile containing the account</param>
        /// <returns>The created queue item</returns>
        Task<AutoLoginQueueItem> AddToQueueAsync(PlayOnlineMemberAccount account, ProfileInfo profile);

        /// <summary>
        /// Removes an item from the queue
        /// </summary>
        /// <param name="item">Queue item to remove</param>
        /// <returns>True if removed successfully</returns>
        Task<bool> RemoveFromQueueAsync(AutoLoginQueueItem item);

        /// <summary>
        /// Clears all items from the queue
        /// </summary>
        Task ClearQueueAsync();

        /// <summary>
        /// Moves a queue item to a new position
        /// </summary>
        /// <param name="item">Item to move</param>
        /// <param name="newPosition">New position (1-based)</param>
        /// <returns>True if moved successfully</returns>
        Task<bool> MoveItemAsync(AutoLoginQueueItem item, int newPosition);

        /// <summary>
        /// Reorders queue items
        /// </summary>
        /// <param name="newOrder">New order of items</param>
        Task ReorderQueueAsync(IList<AutoLoginQueueItem> newOrder);

        /// <summary>
        /// Resets queue items back to pending state for re-execution
        /// </summary>
        Task ResetQueueAsync();

        /// <summary>
        /// Retries a failed queue item
        /// </summary>
        /// <param name="item">Item to retry</param>
        Task RetryItemAsync(AutoLoginQueueItem item);

        #endregion

        #region Events

        /// <summary>
        /// Raised when items are added to the queue
        /// </summary>
        event EventHandler<AutoLoginQueueItemEventArgs>? ItemAdded;

        /// <summary>
        /// Raised when items are removed from the queue
        /// </summary>
        event EventHandler<AutoLoginQueueItemEventArgs>? ItemRemoved;

        /// <summary>
        /// Raised when queue is cleared
        /// </summary>
        event EventHandler? QueueCleared;

        /// <summary>
        /// Raised when queue is reordered
        /// </summary>
        event EventHandler? QueueReordered;

        #endregion
    }
}