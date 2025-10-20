using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using FFXIManager.Infrastructure;
using FFXIManager.Models;

namespace FFXIManager.Services
{
    /// <summary>
    /// Implementation of queue collection management operations
    /// </summary>
    public class QueueCollectionManager : IQueueCollectionManager
    {
        private readonly ILoggingService _loggingService;
        private readonly IUiDispatcher _uiDispatcher;
        private readonly object _lockObject = new();

        public QueueCollectionManager(
            ILoggingService loggingService,
            IUiDispatcher uiDispatcher)
        {
            _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
            _uiDispatcher = uiDispatcher ?? throw new ArgumentNullException(nameof(uiDispatcher));

            QueueItems = new ObservableCollection<AutoLoginQueueItem>();
            QueueItems.CollectionChanged += (_, _) => UpdateQueuePositions();
        }

        #region Properties

        public ObservableCollection<AutoLoginQueueItem> QueueItems { get; }

        public int TotalItems => QueueItems.Count;

        public int CompletedItems => QueueItems.Count(x => x.Status == AutoLoginQueueStatus.Completed);

        public int FailedItems => QueueItems.Count(x => x.Status == AutoLoginQueueStatus.Failed);

        public int CancelledItems => QueueItems.Count(x => x.Status == AutoLoginQueueStatus.Cancelled);

        public int ProcessedItems => CompletedItems + FailedItems + CancelledItems;

        public int OverallProgress
        {
            get
            {
                if (TotalItems == 0) return 0;
                return (int)((double)ProcessedItems / TotalItems * 100);
            }
        }

        #endregion

        #region Events

        public event EventHandler<AutoLoginQueueItemEventArgs>? ItemAdded;
        public event EventHandler<AutoLoginQueueItemEventArgs>? ItemRemoved;
        public event EventHandler? QueueCleared;
        public event EventHandler? QueueReordered;

        #endregion

        #region Collection Operations

        public bool IsAccountAlreadyQueued(PlayOnlineMemberAccount account)
        {
            if (account == null) return false;

            lock (_lockObject)
            {
                return QueueItems.Any(item => item.Account.Id == account.Id);
            }
        }

        public async Task<AutoLoginQueueItem> AddToQueueAsync(PlayOnlineMemberAccount account, ProfileInfo profile)
        {
            if (account == null) throw new ArgumentNullException(nameof(account));
            if (profile == null) throw new ArgumentNullException(nameof(profile));

            // Check for duplicate account (by GUID) to prevent conflicts
            lock (_lockObject)
            {
                var existingItem = QueueItems.FirstOrDefault(item => item.Account.Id == account.Id);
                if (existingItem != null)
                {
                    throw new InvalidOperationException($"Account '{account.DisplayName}' is already in the queue. Duplicate accounts cannot be added as this would cause login conflicts.");
                }
            }

            var queueItem = new AutoLoginQueueItem
            {
                Account = account,
                Profile = profile,
                Position = TotalItems + 1
            };

            await _uiDispatcher.InvokeAsync(() =>
            {
                lock (_lockObject)
                {
                    QueueItems.Add(queueItem);
                }
            });

            await _loggingService.LogInfoAsync($"Added {account.DisplayName} from profile {profile.Name} to auto-login queue");

            ItemAdded?.Invoke(this, new AutoLoginQueueItemEventArgs(queueItem));
            return queueItem;
        }

        public async Task<bool> RemoveFromQueueAsync(AutoLoginQueueItem item)
        {
            if (item == null) return false;

            // Don't allow removal of currently executing item
            if (item.Status == AutoLoginQueueStatus.InProgress)
            {
                await _loggingService.LogWarningAsync("Cannot remove currently executing queue item");
                return false;
            }

            bool removed = false;
            await _uiDispatcher.InvokeAsync(() =>
            {
                lock (_lockObject)
                {
                    removed = QueueItems.Remove(item);
                }
            });

            // Clean up the item's event subscriptions if it was removed
            if (removed)
            {
                item.Cleanup();
                await _loggingService.LogInfoAsync($"Removed {item.DisplayName} from auto-login queue");
                ItemRemoved?.Invoke(this, new AutoLoginQueueItemEventArgs(item));
            }

            return removed;
        }

        public async Task ClearQueueAsync()
        {
            // Check if any items are currently executing
            var executingItems = QueueItems.Where(x => x.Status == AutoLoginQueueStatus.InProgress).ToList();
            if (executingItems.Any())
            {
                await _loggingService.LogWarningAsync("Cannot clear queue while items are executing");
                return;
            }

            var itemCount = TotalItems;

            // Clean up all items before clearing
            var itemsToCleanup = QueueItems.ToList();

            await _uiDispatcher.InvokeAsync(() =>
            {
                lock (_lockObject)
                {
                    QueueItems.Clear();
                }
            });

            // Clean up event subscriptions for all removed items
            foreach (var item in itemsToCleanup)
            {
                item.Cleanup();
            }

            await _loggingService.LogInfoAsync($"Cleared {itemCount} items from auto-login queue");
            QueueCleared?.Invoke(this, EventArgs.Empty);
        }

        public async Task<bool> MoveItemAsync(AutoLoginQueueItem item, int newPosition)
        {
            if (item == null || newPosition < 1 || newPosition > TotalItems) return false;

            // Don't allow moving currently executing item
            if (item.Status == AutoLoginQueueStatus.InProgress)
            {
                await _loggingService.LogWarningAsync("Cannot move currently executing queue item");
                return false;
            }

            lock (_lockObject)
            {
                var currentIndex = QueueItems.IndexOf(item);
                if (currentIndex == -1) return false;

                var newIndex = newPosition - 1; // Convert to 0-based
                QueueItems.Move(currentIndex, newIndex);
            }

            await _loggingService.LogInfoAsync($"Moved {item.DisplayName} to position {newPosition}");
            QueueReordered?.Invoke(this, EventArgs.Empty);
            return true;
        }

        public async Task ReorderQueueAsync(IList<AutoLoginQueueItem> newOrder)
        {
            if (newOrder == null) return;

            // Don't allow reordering if any items are executing
            var executingItems = QueueItems.Where(x => x.Status == AutoLoginQueueStatus.InProgress).ToList();
            if (executingItems.Any())
            {
                await _loggingService.LogWarningAsync("Cannot reorder queue while items are executing");
                return;
            }

            await _uiDispatcher.InvokeAsync(() =>
            {
                lock (_lockObject)
                {
                    QueueItems.Clear();
                    foreach (var item in newOrder)
                    {
                        QueueItems.Add(item);
                    }
                }
            });

            await _loggingService.LogInfoAsync($"Reordered queue with {newOrder.Count} items");
            QueueReordered?.Invoke(this, EventArgs.Empty);
        }

        public async Task ResetQueueAsync()
        {
            // Don't reset if any items are executing
            var executingItems = QueueItems.Where(x => x.Status == AutoLoginQueueStatus.InProgress).ToList();
            if (executingItems.Any())
            {
                await _loggingService.LogWarningAsync("Cannot reset queue while items are executing");
                return;
            }

            await _uiDispatcher.InvokeAsync(() =>
            {
                lock (_lockObject)
                {
                    // Reset all items back to pending state
                    foreach (var item in QueueItems)
                    {
                        item.Reset();
                    }
                }
            });

            await _loggingService.LogInfoAsync($"Reset {QueueItems.Count} queue items to pending state");

            // Trigger collection changed event to ensure UI updates
            QueueReordered?.Invoke(this, EventArgs.Empty);
        }

        public async Task RetryItemAsync(AutoLoginQueueItem item)
        {
            if (item?.Status != AutoLoginQueueStatus.Failed) return;

            item.Reset();
            await _loggingService.LogInfoAsync($"Reset failed item for retry: {item.DisplayName}");
        }

        #endregion

        #region Helper Methods

        private void UpdateQueuePositions()
        {
            lock (_lockObject)
            {
                for (int i = 0; i < QueueItems.Count; i++)
                {
                    QueueItems[i].Position = i + 1;
                }
            }
        }

        #endregion
    }
}
