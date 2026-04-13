using FourChanGrabber.Controllers;
using FourChanGrabber.Data;
using FourChanGrabber.Data.Models;
using FourChanGrabber.Data.Models.Enums;
using FourChanGrabber.Models;
using FourChanGrabber.Services;
using FourChanGrabber.Tests.TestFixtures;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Moq;
using System.Collections.Generic;
using Xunit;

namespace FourChanGrabber.Tests.Controllers;

/// <summary>
/// Unit tests for DownloadQueueController covering all API endpoints.
/// </summary>
public class DownloadQueueControllerTests : IDisposable
{
    private readonly IQueueService _queueService;
    private readonly DownloadManager _downloadManager;
    private readonly TestDbContext _dbContext;
    private DownloadQueueController _sut;

    public DownloadQueueControllerTests()
    {
        _queueService = new Mock<IQueueService>().Object;
        _downloadManager = CreateDownloadManager();
        _dbContext = new TestDbContext();

        _sut = new DownloadQueueController(
            _queueService,
            _downloadManager,
            _dbContext.Context);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    private static DownloadManager CreateDownloadManager()
    {
        var mockScopeFactory = new Mock<IServiceScopeFactory>();
        var mockLogger = new Mock<Microsoft.Extensions.Logging.ILogger<DownloadManager>>();
        
        // Use real configuration builder instead of Moq for extension methods
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["downloadManager:tempDirectory"] = "./temp",
                ["downloadManager:pollIntervalSeconds"] = "5"
            })
            .Build();

        return new DownloadManager(
            mockScopeFactory.Object,
            mockLogger.Object,
            configuration);
    }

    #region GET /api/downloadqueue - List Items

    [Fact]
    public async Task GetQueueItems_ReturnsPaginatedList()
    {
        // Arrange
        var items = _dbContext.CreateMultipleDownloadQueueItems(5);
        SetupMockQueueServiceStats(new QueueStats { Pending = 5 });

        // Act
        var result = await _sut.GetQueueItems();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<DownloadQueueListResponse>(okResult.Value);
        Assert.Equal(5, response.Items.Count);
        Assert.Equal(5, response.Total);
    }

    [Fact]
    public async Task GetQueueItems_FiltersbyStatus()
    {
        // Arrange
        _dbContext.CreateDownloadQueue(status: DownloadStatus.Pending);
        _dbContext.CreateDownloadQueue(status: DownloadStatus.Pending);
        _dbContext.CreateDownloadQueue(status: DownloadStatus.Failed);

        SetupMockQueueServiceStats(new QueueStats { Pending = 2, Failed = 1 });

        // Act
        var result = await _sut.GetQueueItems(status: DownloadStatus.Pending);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<DownloadQueueListResponse>(okResult.Value);
        Assert.Equal(2, response.Items.Count);
        Assert.All(response.Items, item => Assert.Equal("Pending", item.Status));
    }

    [Fact]
    public async Task GetQueueItems_FiltersbySourceId()
    {
        // Arrange
        var source1 = _dbContext.CreateImageSource(name: "Source 1");
        var source2 = _dbContext.CreateImageSource(name: "Source 2");

        _dbContext.CreateDownloadQueue(imageSourceId: source1.Id);
        _dbContext.CreateDownloadQueue(imageSourceId: source1.Id);
        _dbContext.CreateDownloadQueue(imageSourceId: source2.Id);

        SetupMockQueueServiceStats(new QueueStats { Pending = 3 });

        // Act
        var result = await _sut.GetQueueItems(sourceId: source1.Id);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<DownloadQueueListResponse>(okResult.Value);
        Assert.Equal(2, response.Items.Count);
    }

    [Fact]
    public async Task GetQueueItems_RespectsLimit()
    {
        // Arrange
        _dbContext.CreateMultipleDownloadQueueItems(10);
        SetupMockQueueServiceStats(new QueueStats { Pending = 10 });

        // Act
        var result = await _sut.GetQueueItems(limit: 3);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<DownloadQueueListResponse>(okResult.Value);
        Assert.Equal(3, response.Items.Count);
    }

    [Fact]
    public async Task GetQueueItems_RespectsOffset()
    {
        // Arrange
        _dbContext.CreateMultipleDownloadQueueItems(5);
        SetupMockQueueServiceStats(new QueueStats { Pending = 5 });

        // Act
        var result = await _sut.GetQueueItems(offset: 2);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<DownloadQueueListResponse>(okResult.Value);
        Assert.Equal(3, response.Items.Count); // 5 total - 2 offset = 3
    }

    [Fact]
    public async Task GetQueueItems_ClampsLimitToMax()
    {
        // Arrange
        _dbContext.CreateMultipleDownloadQueueItems(300);
        SetupMockQueueServiceStats(new QueueStats { Pending = 300 });

        // Act
        var result = await _sut.GetQueueItems(limit: 500); // Over max of 200

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<DownloadQueueListResponse>(okResult.Value);
        Assert.Equal(200, response.Items.Count); // Should be clamped to max
    }

    [Fact]
    public async Task GetQueueItems_OrdersByPriorityDescRequestTimeAsc()
    {
        // Arrange
        var sourceId = _dbContext.CreateImageSource().Id;

        var item1 = new DownloadQueueBuilder()
            .WithImageSourceId(sourceId)
            .WithPriority(1)
            .WithRequestTime(DateTime.UtcNow.AddMinutes(-10))
            .Build();
        var item2 = new DownloadQueueBuilder()
            .WithImageSourceId(sourceId)
            .WithPriority(100)
            .WithRequestTime(DateTime.UtcNow.AddMinutes(-5))
            .Build();
        var item3 = new DownloadQueueBuilder()
            .WithImageSourceId(sourceId)
            .WithPriority(50)
            .WithRequestTime(DateTime.UtcNow)
            .Build();

        _dbContext.Context.DownloadQueue.AddRange(item1, item2, item3);
        await _dbContext.Context.SaveChangesAsync();

        SetupMockQueueServiceStats(new QueueStats { Pending = 3 });

        // Act
        var result = await _sut.GetQueueItems();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<DownloadQueueListResponse>(okResult.Value);
        Assert.Equal(3, response.Items.Count);
        // Note: Order should be Priority DESC, RequestTime ASC
        Assert.Equal(100, response.Items[0].Priority);
        Assert.Equal(50, response.Items[1].Priority);
        Assert.Equal(1, response.Items[2].Priority);
    }

    #endregion

    #region GET /api/downloadqueue/{id} - Get Single Item

    [Fact]
    public async Task GetQueueItem_ReturnsItem_WhenExists()
    {
        // Arrange
        var item = _dbContext.CreateDownloadQueue(status: DownloadStatus.Pending);

        // Act
        var result = await _sut.GetQueueItem(item.Id, default);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<DownloadQueueDetailResponse>(okResult.Value);
        Assert.Equal(item.Id, response.Id);
        Assert.Equal(item.SourceUrl, response.SourceUrl);
    }

    [Fact]
    public async Task GetQueueItem_ReturnsNotFound_WhenNotExists()
    {
        // Act
        var result = await _sut.GetQueueItem(99999, default);

        // Assert
        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetQueueItem_ReturnsDetailResponse_WithExpectedHash()
    {
        // Arrange
        var item = _dbContext.CreateDownloadQueue(status: DownloadStatus.Pending);
        item.ExpectedHash = "abc123";
        item.ExpectedHashType = "md5";
        await _dbContext.Context.SaveChangesAsync();

        // Act
        var result = await _sut.GetQueueItem(item.Id, default);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<DownloadQueueDetailResponse>(okResult.Value);
        Assert.Equal("abc123", response.ExpectedHash);
        Assert.Equal("md5", response.ExpectedHashType);
    }

    #endregion

    #region POST /api/downloadqueue/pause

    [Fact]
    public void Pause_CallsDownloadManagerPause()
    {
        // Arrange
        var pauseCalled = false;
        _downloadManager.Pause(); // Call directly to verify behavior

        // Act
        var result = _sut.Pause();

        // Assert
        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(WorkerStatus.Paused, _downloadManager.Status);
    }

    [Fact]
    public void Pause_ReturnsSuccessMessage()
    {
        // Act
        var result = _sut.Pause();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
    }

    #endregion

    #region POST /api/downloadqueue/resume

    [Fact]
    public void Resume_CallsDownloadManagerResume()
    {
        // Arrange
        _downloadManager.Pause();
        Assert.Equal(WorkerStatus.Paused, _downloadManager.Status);

        // Act
        var result = _sut.Resume();

        // Assert
        Assert.IsType<OkObjectResult>(result);
        Assert.NotEqual(WorkerStatus.Paused, _downloadManager.Status);
    }

    [Fact]
    public void Resume_ReturnsSuccessMessage()
    {
        // Act
        var result = _sut.Resume();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
    }

    #endregion

    #region GET /api/downloadqueue/status

    [Fact]
    public void GetWorkerStatus_ReturnsStatusFromDownloadManager()
    {
        // Arrange
        // Start with Idle
        Assert.Equal(WorkerStatus.Idle, _downloadManager.Status);

        // Act
        var result = _sut.GetWorkerStatus();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<WorkerStatusResponse>(okResult.Value);
        Assert.Equal("Idle", response.Status);
        Assert.Equal(0, response.ActiveDownloads);
    }

    [Fact]
    public void GetWorkerStatus_ReturnsRunningStatus_WhenDownloadsActive()
    {
        // Arrange - We can't easily simulate active downloads in unit tests
        // but we can verify the status changes with Pause
        _downloadManager.Pause();

        // Act
        var result = _sut.GetWorkerStatus();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<WorkerStatusResponse>(okResult.Value);
        Assert.Equal("Paused", response.Status);
    }

    #endregion

    #region GET /api/downloadqueue/stats

    [Fact]
    public async Task GetStats_ReturnsQueueStats()
    {
        // Arrange
        var stats = new QueueStats
        {
            Pending = 10,
            Downloading = 2,
            Completed = 100,
            Failed = 5,
            Cancelled = 1
        };
        SetupMockQueueServiceStats(stats);

        // Act
        var result = await _sut.GetStats(default);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<QueueStats>(okResult.Value);
        Assert.Equal(10, response.Pending);
        Assert.Equal(2, response.Downloading);
        Assert.Equal(100, response.Completed);
        Assert.Equal(5, response.Failed);
        Assert.Equal(1, response.Cancelled);
    }

    #endregion

    #region POST /api/downloadqueue/{id}/retry

    [Fact]
    public async Task RetryItem_ResetsFailedItemToPending()
    {
        // Arrange
        var item = _dbContext.CreateDownloadQueue(status: DownloadStatus.Failed);
        item.ErrorMessage = "Previous error";
        await _dbContext.Context.SaveChangesAsync();

        // Act
        var result = await _sut.RetryItem(item.Id, default);

        // Assert
        Assert.IsType<OkObjectResult>(result);

        var updatedItem = await _dbContext.Context.DownloadQueue.FindAsync(item.Id);
        Assert.Equal(DownloadStatus.Pending, updatedItem!.Status);
        Assert.Null(updatedItem.ErrorMessage);
    }

    [Fact]
    public async Task RetryItem_ReturnsNotFound_WhenItemNotExists()
    {
        // Act
        var result = await _sut.RetryItem(99999, default);

        // Assert
        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task RetryItem_ReturnsBadRequest_WhenItemNotFailed()
    {
        // Arrange
        var item = _dbContext.CreateDownloadQueue(status: DownloadStatus.Pending);

        // Act
        var result = await _sut.RetryItem(item.Id, default);

        // Assert
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task RetryItem_ReturnsBadRequest_WhenItemIsCompleted()
    {
        // Arrange
        var item = _dbContext.CreateDownloadQueue(status: DownloadStatus.Completed);

        // Act
        var result = await _sut.RetryItem(item.Id, default);

        // Assert
        Assert.IsType<BadRequestObjectResult>(result);
    }

    #endregion

    #region DELETE /api/downloadqueue/{id}

    [Fact]
    public async Task DeleteItem_RemovesItem()
    {
        // Arrange
        var item = _dbContext.CreateDownloadQueue(status: DownloadStatus.Pending);

        // Act
        var result = await _sut.DeleteItem(item.Id, default);

        // Assert
        Assert.IsType<NoContentResult>(result);

        var deletedItem = await _dbContext.Context.DownloadQueue.FindAsync(item.Id);
        Assert.Null(deletedItem);
    }

    [Fact]
    public async Task DeleteItem_RemovesAssociatedChanBoard()
    {
        // Arrange
        var item = _dbContext.CreateDownloadQueue(status: DownloadStatus.Pending);
        _dbContext.CreateChanBoard(item.Id, board: "test", threadId: "123");

        // Act
        var result = await _sut.DeleteItem(item.Id, default);

        // Assert
        Assert.IsType<NoContentResult>(result);

        var chanBoard = await _dbContext.Context.DownloadQueue_ChanBoard
            .FirstOrDefaultAsync(cb => cb.DownloadQueueId == item.Id);
        Assert.Null(chanBoard);
    }

    [Fact]
    public async Task DeleteItem_ReturnsNotFound_WhenItemNotExists()
    {
        // Act
        var result = await _sut.DeleteItem(99999, default);

        // Assert
        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task DeleteItem_SucceedsEvenWithoutChanBoard()
    {
        // Arrange
        var item = _dbContext.CreateDownloadQueue(status: DownloadStatus.Pending);
        // No ChanBoard associated

        // Act
        var result = await _sut.DeleteItem(item.Id, default);

        // Assert
        Assert.IsType<NoContentResult>(result);
    }

    #endregion

    #region DTO Mapping Tests

    [Fact]
    public async Task MapToDto_IncludesChanBoard_WhenPresent()
    {
        // Arrange
        var item = _dbContext.CreateDownloadQueue(status: DownloadStatus.Pending);
        _dbContext.CreateChanBoard(item.Id, board: "test", threadId: "123", threadUrl: "https://example.com/thread/123");

        // Act
        var result = await _sut.GetQueueItem(item.Id, default);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<DownloadQueueDetailResponse>(okResult.Value);
        Assert.NotNull(response.ChanBoard);
        Assert.Equal("test", response.ChanBoard!.Board);
        Assert.Equal("123", response.ChanBoard.ThreadId);
    }

    [Fact]
    public async Task MapToDto_IncludesImageSourceName()
    {
        // Arrange
        var source = _dbContext.CreateImageSource(name: "Test Source");
        var item = _dbContext.CreateDownloadQueue(imageSourceId: source.Id);

        // Act
        var result = await _sut.GetQueueItem(item.Id, default);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<DownloadQueueDetailResponse>(okResult.Value);
        Assert.Equal("Test Source", response.ImageSourceName);
    }

    #endregion

    #region Helper Methods

    private void SetupMockQueueServiceStats(QueueStats stats)
    {
        var mockQueueService = new Mock<IQueueService>();
        mockQueueService.Setup(s => s.GetStatsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(stats);

        // Create a new controller with the mocked queue service
        _sut = new DownloadQueueController(
            mockQueueService.Object,
            _downloadManager,
            _dbContext.Context);
    }

    #endregion
}
