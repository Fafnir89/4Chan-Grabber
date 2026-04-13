using Microsoft.EntityFrameworkCore;
using FourChanGrabber.Data.Models.Enums;
using FourChanGrabber.Services;
using FourChanGrabber.Tests.TestFixtures;
using Xunit;

namespace FourChanGrabber.Tests.Services;

/// <summary>
/// Unit tests for QueueService covering all queue operations.
/// </summary>
public class QueueServiceTests : IDisposable
{
    private readonly TestDbContext _dbContext;
    private readonly QueueService _sut;

    public QueueServiceTests()
    {
        _dbContext = new TestDbContext();
        _sut = new QueueService(_dbContext.Context);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    #region GetPendingItemsAsync Tests

    [Fact]
    public async Task GetPendingItemsAsync_ReturnsOnlyPendingItems()
    {
        // Arrange - Create mixed status items
        _dbContext.CreateDownloadQueue(status: DownloadStatus.Pending);
        _dbContext.CreateDownloadQueue(status: DownloadStatus.Pending);
        _dbContext.CreateDownloadQueue(status: DownloadStatus.Downloading);
        _dbContext.CreateDownloadQueue(status: DownloadStatus.Completed);

        // Act
        var result = await _sut.GetPendingItemsAsync();

        // Assert
        Assert.Equal(2, result.Count);
        Assert.All(result, item => Assert.Equal(DownloadStatus.Pending, item.Status));
    }

    [Fact]
    public async Task GetPendingItemsAsync_ReturnsItemsOrderedByPriorityDescRequestTimeAsc()
    {
        // Arrange - Create items with different priorities and request times
        var sourceId = _dbContext.CreateImageSource().Id;

        var lowestPriority = new DownloadQueueBuilder()
            .WithImageSourceId(sourceId)
            .WithPriority(1)
            .WithRequestTime(DateTime.UtcNow.AddMinutes(-10))
            .Build();
        var highestPriority = new DownloadQueueBuilder()
            .WithImageSourceId(sourceId)
            .WithPriority(100)
            .WithRequestTime(DateTime.UtcNow.AddMinutes(-5))
            .Build();
        var mediumPriority = new DownloadQueueBuilder()
            .WithImageSourceId(sourceId)
            .WithPriority(50)
            .WithRequestTime(DateTime.UtcNow)
            .Build();

        _dbContext.Context.DownloadQueue.AddRange(lowestPriority, highestPriority, mediumPriority);
        await _dbContext.Context.SaveChangesAsync();

        // Act
        var result = await _sut.GetPendingItemsAsync();

        // Assert - Should be ordered by Priority DESC, then RequestTime ASC
        Assert.Equal(3, result.Count);
        Assert.Equal(100, result[0].Priority);  // Highest priority first
        Assert.Equal(50, result[1].Priority);   // Medium priority second
        Assert.Equal(1, result[2].Priority);     // Lowest priority third
    }

    [Fact]
    public async Task GetPendingItemsAsync_RespectsMaxItemsLimit()
    {
        // Arrange
        _dbContext.CreateMultipleDownloadQueueItems(20);

        // Act
        var result = await _sut.GetPendingItemsAsync(maxItems: 5);

        // Assert
        Assert.Equal(5, result.Count);
    }

    [Fact]
    public async Task GetPendingItemsAsync_ReturnsEmptyListWhenNoPendingItems()
    {
        // Arrange - Only non-pending items
        _dbContext.CreateDownloadQueue(status: DownloadStatus.Completed);
        _dbContext.CreateDownloadQueue(status: DownloadStatus.Failed);

        // Act
        var result = await _sut.GetPendingItemsAsync();

        // Assert
        Assert.Empty(result);
    }

    #endregion

    #region TryLockItemAsync Tests

    [Fact]
    public async Task TryLockItemAsync_LocksPendingItem_ReturnsTrue()
    {
        // Arrange
        var item = _dbContext.CreateDownloadQueue(status: DownloadStatus.Pending);

        // Act
        var result = await _sut.TryLockItemAsync(item.Id);

        // Assert
        Assert.True(result);

        // Verify item is now locked
        var updatedItem = await _dbContext.Context.DownloadQueue.FindAsync(item.Id);
        Assert.Equal(DownloadStatus.Downloading, updatedItem!.Status);
        Assert.NotNull(updatedItem.StartedAt);
    }

    [Fact]
    public async Task TryLockItemAsync_DoesNotLockAlreadyLockedItem_ReturnsFalse()
    {
        // Arrange - Item already downloading
        var item = _dbContext.CreateDownloadQueue(status: DownloadStatus.Downloading);

        // Act
        var result = await _sut.TryLockItemAsync(item.Id);

        // Assert
        Assert.False(result);

        // Verify status unchanged
        var updatedItem = await _dbContext.Context.DownloadQueue.FindAsync(item.Id);
        Assert.Equal(DownloadStatus.Downloading, updatedItem!.Status);
    }

    [Fact]
    public async Task TryLockItemAsync_DoesNotLockCompletedItem_ReturnsFalse()
    {
        // Arrange
        var item = _dbContext.CreateDownloadQueue(status: DownloadStatus.Completed);

        // Act
        var result = await _sut.TryLockItemAsync(item.Id);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task TryLockItemAsync_DoesNotLockItemWithNonNullStartedAt_ReturnsFalse()
    {
        // Arrange - Item has StartedAt set (stale item being recovered)
        var item = _dbContext.CreateDownloadQueue(status: DownloadStatus.Pending);
        item.StartedAt = DateTime.UtcNow.AddMinutes(-10);
        await _dbContext.Context.SaveChangesAsync();

        // Act
        var result = await _sut.TryLockItemAsync(item.Id);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task TryLockItemAsync_DoesNotLockNonExistentItem_ReturnsFalse()
    {
        // Act
        var result = await _sut.TryLockItemAsync(99999);

        // Assert
        Assert.False(result);
    }

    #endregion

    #region UpdateStatusAsync Tests

    [Fact]
    public async Task UpdateStatusAsync_UpdatesStatusCorrectly()
    {
        // Arrange
        var item = _dbContext.CreateDownloadQueue(status: DownloadStatus.Pending);

        // Act
        await _sut.UpdateStatusAsync(item.Id, DownloadStatus.Downloading);

        // Assert
        var updatedItem = await _dbContext.Context.DownloadQueue.FindAsync(item.Id);
        Assert.Equal(DownloadStatus.Downloading, updatedItem!.Status);
    }

    [Fact]
    public async Task UpdateStatusAsync_SetsStartedAtWhenStatusIsDownloading()
    {
        // Arrange
        var item = _dbContext.CreateDownloadQueue(status: DownloadStatus.Pending);

        // Act
        await _sut.UpdateStatusAsync(item.Id, DownloadStatus.Downloading);

        // Assert
        var updatedItem = await _dbContext.Context.DownloadQueue.FindAsync(item.Id);
        Assert.NotNull(updatedItem!.StartedAt);
    }

    [Fact]
    public async Task UpdateStatusAsync_IncrementsAttemptsWhenStatusIsDownloading()
    {
        // Arrange
        var item = _dbContext.CreateDownloadQueue(status: DownloadStatus.Pending);
        Assert.Equal(0, item.Attempts);

        // Act
        await _sut.UpdateStatusAsync(item.Id, DownloadStatus.Downloading);

        // Assert
        var updatedItem = await _dbContext.Context.DownloadQueue.FindAsync(item.Id);
        Assert.Equal(1, updatedItem!.Attempts);
    }

    [Fact]
    public async Task UpdateStatusAsync_SetsCompletedAtWhenStatusIsCompleted()
    {
        // Arrange
        var item = _dbContext.CreateDownloadQueue(status: DownloadStatus.Downloading);

        // Act
        await _sut.UpdateStatusAsync(item.Id, DownloadStatus.Completed);

        // Assert
        var updatedItem = await _dbContext.Context.DownloadQueue.FindAsync(item.Id);
        Assert.NotNull(updatedItem!.CompletedAt);
    }

    [Fact]
    public async Task UpdateStatusAsync_SetsErrorMessage()
    {
        // Arrange
        var item = _dbContext.CreateDownloadQueue(status: DownloadStatus.Pending);
        var errorMessage = "Test error message";

        // Act
        await _sut.UpdateStatusAsync(item.Id, DownloadStatus.Failed, errorMessage);

        // Assert
        var updatedItem = await _dbContext.Context.DownloadQueue.FindAsync(item.Id);
        Assert.Equal(errorMessage, updatedItem!.ErrorMessage);
    }

    [Fact]
    public async Task UpdateStatusAsync_DoesNothingForNonExistentItem()
    {
        // Act & Assert - Should not throw
        await _sut.UpdateStatusAsync(99999, DownloadStatus.Failed);
    }

    #endregion

    #region CompleteDownloadAsync Tests

    [Fact]
    public async Task CompleteDownloadAsync_CreatesMediaDataAndDeletesQueueItem()
    {
        // Arrange
        var item = _dbContext.CreateDownloadQueue(status: DownloadStatus.Downloading);
        var fileHash = "abc123";
        var fileSize = 1024L;

        // Act
        await _sut.CompleteDownloadAsync(item.Id, fileHash, fileSize);

        // Assert - MediaData created
        var mediaData = await _dbContext.Context.MediaData.FirstOrDefaultAsync();
        Assert.NotNull(mediaData);
        Assert.Equal(fileHash, mediaData.FileHash);
        Assert.Equal("sha256", mediaData.HashType);
        Assert.Equal(fileSize, mediaData.FileSize);
        Assert.Equal(item.TargetPath, mediaData.FilePath);

        // Assert - Queue item deleted
        var queueItem = await _dbContext.Context.DownloadQueue.FindAsync(item.Id);
        Assert.Null(queueItem);
    }

    [Fact]
    public async Task CompleteDownloadAsync_WithChanBoard_CreatesChanBoardData()
    {
        // Arrange
        var item = _dbContext.CreateDownloadQueue(status: DownloadStatus.Downloading);
        _dbContext.CreateChanBoard(item.Id, board: "test", threadId: "thread123");

        // Act
        await _sut.CompleteDownloadAsync(item.Id, "hash123", 1024);

        // Assert
        var chanBoardData = await _dbContext.Context.ChanBoardData.FirstOrDefaultAsync();
        Assert.NotNull(chanBoardData);
        Assert.Equal("test", chanBoardData.Board);
        Assert.Equal("thread123", chanBoardData.ThreadId);

        // Assert - ChanBoard record deleted
        var chanBoard = await _dbContext.Context.DownloadQueue_ChanBoard
            .FirstOrDefaultAsync(cb => cb.DownloadQueueId == item.Id);
        Assert.Null(chanBoard);
    }

    [Fact]
    public async Task CompleteDownloadAsync_DoesNothingForNonExistentItem()
    {
        // Act & Assert - Should not throw
        await _sut.CompleteDownloadAsync(99999, "hash", 1024);

        // Assert - No MediaData created
        var mediaDataCount = await _dbContext.Context.MediaData.CountAsync();
        Assert.Equal(0, mediaDataCount);
    }

    #endregion

    #region HandleFailureAsync Tests

    [Fact]
    public async Task HandleFailureAsync_ResetsToPendingForRetry_WhenRetryAttemptsRemainingAndAttemptsBelowMax()
    {
        // Arrange
        var item = _dbContext.CreateDownloadQueue(status: DownloadStatus.Downloading);
        item.Attempts = 1; // Below max of 3
        await _dbContext.Context.SaveChangesAsync();

        // Act - retryAttemptsRemaining = maxRetries - currentAttempts = 3 - 1 = 2
        await _sut.HandleFailureAsync(item.Id, "Network error", retryAttemptsRemaining: 2);

        // Assert
        var updatedItem = await _dbContext.Context.DownloadQueue.FindAsync(item.Id);
        Assert.Equal(DownloadStatus.Pending, updatedItem!.Status);
        Assert.Equal("Network error", updatedItem.ErrorMessage);
    }

    [Fact]
    public async Task HandleFailureAsync_MarksAsFailed_WhenRetryAttemptsExhausted()
    {
        // Arrange
        var item = _dbContext.CreateDownloadQueue(status: DownloadStatus.Downloading);
        item.Attempts = 3; // At max of 3
        await _dbContext.Context.SaveChangesAsync();

        // Act - retryAttemptsRemaining = maxRetries - currentAttempts = 3 - 3 = 0
        await _sut.HandleFailureAsync(item.Id, "Network error", retryAttemptsRemaining: 0);

        // Assert
        var updatedItem = await _dbContext.Context.DownloadQueue.FindAsync(item.Id);
        Assert.Equal(DownloadStatus.Failed, updatedItem!.Status);
        Assert.Equal("Network error", updatedItem.ErrorMessage);
    }

    [Fact]
    public async Task HandleFailureAsync_MarksAsFailed_WhenRetryAttemptsRemainingIsZero()
    {
        // Arrange
        var item = _dbContext.CreateDownloadQueue(status: DownloadStatus.Downloading);
        item.Attempts = 0;
        await _dbContext.Context.SaveChangesAsync();

        // Act - retryAttemptsRemaining = 0 means no retries allowed
        await _sut.HandleFailureAsync(item.Id, "Corrupt file", retryAttemptsRemaining: 0);

        // Assert
        var updatedItem = await _dbContext.Context.DownloadQueue.FindAsync(item.Id);
        Assert.Equal(DownloadStatus.Failed, updatedItem!.Status);
        Assert.Equal("Corrupt file", updatedItem.ErrorMessage);
    }

    [Fact]
    public async Task HandleFailureAsync_DoesNothingForNonExistentItem()
    {
        // Act & Assert - Should not throw
        await _sut.HandleFailureAsync(99999, "error", retryAttemptsRemaining: 0);
    }

    #endregion

    #region GetStatsAsync Tests

    [Fact]
    public async Task GetStatsAsync_ReturnsCorrectCounts()
    {
        // Arrange
        _dbContext.CreateDownloadQueue(status: DownloadStatus.Pending);
        _dbContext.CreateDownloadQueue(status: DownloadStatus.Pending);
        _dbContext.CreateDownloadQueue(status: DownloadStatus.Downloading);
        _dbContext.CreateDownloadQueue(status: DownloadStatus.Completed);
        _dbContext.CreateDownloadQueue(status: DownloadStatus.Completed);
        _dbContext.CreateDownloadQueue(status: DownloadStatus.Completed);
        _dbContext.CreateDownloadQueue(status: DownloadStatus.Failed);
        _dbContext.CreateDownloadQueue(status: DownloadStatus.Cancelled);

        // Act
        var stats = await _sut.GetStatsAsync();

        // Assert
        Assert.Equal(2, stats.Pending);
        Assert.Equal(1, stats.Downloading);
        Assert.Equal(3, stats.Completed);
        Assert.Equal(1, stats.Failed);
        Assert.Equal(1, stats.Cancelled);
    }

    [Fact]
    public async Task GetStatsAsync_ReturnsZeroCountsWhenEmpty()
    {
        // Act
        var stats = await _sut.GetStatsAsync();

        // Assert
        Assert.Equal(0, stats.Pending);
        Assert.Equal(0, stats.Downloading);
        Assert.Equal(0, stats.Completed);
        Assert.Equal(0, stats.Failed);
        Assert.Equal(0, stats.Cancelled);
    }

    #endregion

    #region GetStatusAsync Tests

    [Fact]
    public async Task GetStatusAsync_ReturnsWorkerStatus()
    {
        // Act
        var status = await _sut.GetStatusAsync();

        // Assert - QueueService initializes to Idle
        Assert.Equal(Models.WorkerStatus.Idle, status);
    }

    #endregion
}
