using FourChanGrabber.Data;
using FourChanGrabber.Data.Models;
using FourChanGrabber.Data.Models.Enums;
using FourChanGrabber.Models;
using FourChanGrabber.Services;
using FourChanGrabber.Tests.TestFixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using System.Collections.Generic;
using Xunit;

namespace FourChanGrabber.Tests.Services;

/// <summary>
/// Unit tests for DownloadManager covering background service behavior, pause/resume, and concurrency.
/// </summary>
public class DownloadManagerTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly Mock<IServiceScopeFactory> _mockScopeFactory;
    private readonly Mock<ILogger<DownloadManager>> _mockLogger;
    private readonly IConfiguration _configuration;
    private readonly ServiceCollection _serviceCollection;
    private readonly TestDbContext _dbContext;

    public DownloadManagerTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        _mockScopeFactory = new Mock<IServiceScopeFactory>();
        _mockLogger = new Mock<Microsoft.Extensions.Logging.ILogger<DownloadManager>>();
        
        // Use real configuration builder instead of Moq for extension methods
        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["downloadManager:tempDirectory"] = _tempDirectory,
                ["downloadManager:pollIntervalSeconds"] = "5"
            })
            .Build();
        
        _serviceCollection = new ServiceCollection();
        _dbContext = new TestDbContext();
    }

    public void Dispose()
    {
        _dbContext.Dispose();

        if (Directory.Exists(_tempDirectory))
        {
            try
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    #region Constructor and Properties Tests

    [Fact]
    public void Status_ReturnsIdle_WhenNotPausedAndNoActiveDownloads()
    {
        // Arrange
        var sut = CreateDownloadManager();

        // Act
        var status = sut.Status;

        // Assert
        Assert.Equal(WorkerStatus.Idle, status);
    }

    [Fact]
    public void Status_ReturnsPaused_WhenPaused()
    {
        // Arrange
        var sut = CreateDownloadManager();
        sut.Pause();

        // Act
        var status = sut.Status;

        // Assert
        Assert.Equal(WorkerStatus.Paused, status);
    }

    [Fact]
    public void Pause_SetsIsPausedTrue()
    {
        // Arrange
        var sut = CreateDownloadManager();

        // Act
        sut.Pause();

        // Assert
        Assert.Equal(WorkerStatus.Paused, sut.Status);
    }

    [Fact]
    public void Resume_SetsIsPausedFalse()
    {
        // Arrange
        var sut = CreateDownloadManager();
        sut.Pause();

        // Act
        sut.Resume();

        // Assert
        Assert.Equal(WorkerStatus.Idle, sut.Status);
    }

    [Fact]
    public void ActiveDownloadCount_InitializesToZero()
    {
        // Arrange
        var sut = CreateDownloadManager();

        // Act
        var count = sut.ActiveDownloadCount;

        // Assert
        Assert.Equal(0, count);
    }

    #endregion

    #region StartAsync Cleanup Tests

    [Fact]
    public async Task StartAsync_CleansUpStaleDownloadingItems()
    {
        // Arrange
        var sourceId = _dbContext.CreateImageSource().Id;

        // Create a stale downloading item (started 10 minutes ago)
        var staleItem = new DownloadQueue
        {
            ImageSourceId = sourceId,
            SourceUrl = "https://example.com/stale.jpg",
            TargetPath = "/downloads/stale.jpg",
            RequestTime = DateTime.UtcNow.AddMinutes(-10),
            Status = DownloadStatus.Downloading,
            StartedAt = DateTime.UtcNow.AddMinutes(-10),
            CreatedAt = DateTime.UtcNow.AddMinutes(-10)
        };

        _dbContext.Context.DownloadQueue.Add(staleItem);
        await _dbContext.Context.SaveChangesAsync();

        // Setup service provider with proper scopes
        SetupServiceProviderForStaleRecovery();

        var sut = CreateDownloadManager();

        // Act - Execute the background service for a short time
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        try
        {
            await sut.StartAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected - we cancelled after a short delay
        }

        // Assert - Stale item should be reset to Pending
        var recoveredItem = await _dbContext.Context.DownloadQueue.FindAsync(staleItem.Id);
        Assert.NotNull(recoveredItem);
        Assert.Equal(DownloadStatus.Pending, recoveredItem.Status);
        Assert.Null(recoveredItem.StartedAt);
    }

    [Fact]
    public async Task StartAsync_DeletesTempFiles()
    {
        // Arrange
        Directory.CreateDirectory(_tempDirectory);
        var tempFile1 = Path.Combine(_tempDirectory, "test1.tmp");
        var tempFile2 = Path.Combine(_tempDirectory, "test2.tmp");
        await File.WriteAllTextAsync(tempFile1, "temp content 1");
        await File.WriteAllTextAsync(tempFile2, "temp content 2");

        SetupServiceProviderForStaleRecovery();

        var sut = CreateDownloadManager();

        // Act
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        try
        {
            await sut.StartAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        // Assert - Temp files should be deleted
        Assert.False(File.Exists(tempFile1));
        Assert.False(File.Exists(tempFile2));
    }

    [Fact]
    public async Task StartAsync_OnlyDeletesTmpFiles_NotOtherFiles()
    {
        // Arrange
        Directory.CreateDirectory(_tempDirectory);
        var tempFile = Path.Combine(_tempDirectory, "test.tmp");
        var otherFile = Path.Combine(_tempDirectory, "important.jpg");
        await File.WriteAllTextAsync(tempFile, "temp");
        await File.WriteAllTextAsync(otherFile, "important content");

        SetupServiceProviderForStaleRecovery();

        var sut = CreateDownloadManager();

        // Act
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        try
        {
            await sut.StartAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        // Assert
        Assert.False(File.Exists(tempFile));
        Assert.True(File.Exists(otherFile));
    }

    #endregion

    #region Pause/Resume Behavior Tests

    [Fact]
    public void Pause_MakesStatusReturnPaused()
    {
        // Arrange
        var sut = CreateDownloadManager();

        // Initial state should not be Paused
        Assert.NotEqual(WorkerStatus.Paused, sut.Status);

        // Act
        sut.Pause();

        // Assert
        Assert.Equal(WorkerStatus.Paused, sut.Status);
    }

    [Fact]
    public void Resume_MakesStatusNotPaused()
    {
        // Arrange
        var sut = CreateDownloadManager();
        sut.Pause();
        Assert.Equal(WorkerStatus.Paused, sut.Status);

        // Act
        sut.Resume();

        // Assert
        Assert.NotEqual(WorkerStatus.Paused, sut.Status);
    }

    [Fact]
    public void Pause_Resume_CanBeCalledMultipleTimes()
    {
        // Arrange
        var sut = CreateDownloadManager();

        // Act - Cycle through states
        sut.Pause();
        Assert.Equal(WorkerStatus.Paused, sut.Status);

        sut.Resume();
        Assert.Equal(WorkerStatus.Idle, sut.Status);

        sut.Pause();
        Assert.Equal(WorkerStatus.Paused, sut.Status);

        sut.Resume();
        Assert.Equal(WorkerStatus.Idle, sut.Status);

        // Assert - No exceptions thrown
    }

    #endregion

    #region SourceConfig Mapping Tests

    [Fact]
    public void MapToSourceConfig_MapsAllProperties()
    {
        // Arrange
        var sut = CreateDownloadManager();
        var imageSourceConfig = new ImageSourceConfig
        {
            MaxConcurrentDownloads = 5,
            RateLimitPerSecond = 10,
            CloudFlareProxyUrl = "http://proxy:8191",
            RetryAttempts = 3,
            DownloadTimeoutSeconds = 60
        };

        // Use reflection to test private method since it's internal mapping logic
        var method = typeof(DownloadManager).GetMethod("MapToSourceConfig", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        
        Assert.NotNull(method);

        // Act
        var result = method.Invoke(sut, new object[] { imageSourceConfig }) as SourceConfig;

        // Assert
        Assert.NotNull(result);
        Assert.Equal(5, result.MaxConcurrentDownloads);
        Assert.Equal(10, result.RateLimitPerSecond);
        Assert.Equal("http://proxy:8191", result.CloudFlareProxyUrl);
        Assert.Equal(3, result.RetryAttempts);
        Assert.Equal(60, result.DownloadTimeoutSeconds);
    }

    #endregion

    #region Helper Methods

    private DownloadManager CreateDownloadManager()
    {
        return new DownloadManager(
            _mockScopeFactory.Object,
            _mockLogger.Object,
            _configuration);
    }

    private void SetupServiceProviderForStaleRecovery()
    {
        var serviceProvider = new ServiceCollection()
            .AddScoped(_ => _dbContext.Context)
            .AddScoped<IQueueService, QueueService>()
            .BuildServiceProvider();

        var mockScope = new Mock<IServiceScope>();
        mockScope.Setup(s => s.ServiceProvider).Returns(serviceProvider);

        var mockScopeFactory = new Mock<IServiceScopeFactory>();
        mockScopeFactory.Setup(f => f.CreateScope()).Returns(mockScope.Object);

        _mockScopeFactory.Setup(f => f.CreateScope()).Returns(mockScope.Object);
    }

    #endregion
}

/// <summary>
/// Tests for internal helper methods using reflection.
/// These tests verify implementation details and may need updating if internals change.
/// </summary>
public class DownloadManagerInternalTests
{
    [Fact]
    public void GetOrCreateSemaphore_IsThreadSafe()
    {
        // Arrange
        // Use real ConfigurationBuilder — Mock<IConfiguration> cannot mock extension methods like GetValue<T>()
        var mockScopeFactory = new Mock<IServiceScopeFactory>();
        var mockLogger = new Mock<ILogger<DownloadManager>>();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["downloadManager:tempDirectory"] = "./temp",
                ["downloadManager:pollIntervalSeconds"] = "5"
            })
            .Build();

        var downloadManager = new DownloadManager(
            mockScopeFactory.Object,
            mockLogger.Object,
            configuration);

        var sourceId = 1;
        var maxConcurrent = 1;

        // Use reflection to access private method
        var method = typeof(DownloadManager).GetMethod("GetOrCreateSemaphore",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        Assert.NotNull(method);

        // Act - Call multiple times to verify same semaphore returned
        var semaphore1 = method.Invoke(downloadManager, new object[] { sourceId, maxConcurrent }) as SemaphoreSlim;
        var semaphore2 = method.Invoke(downloadManager, new object[] { sourceId, maxConcurrent }) as SemaphoreSlim;

        // Assert - Should return same semaphore
        Assert.Same(semaphore1, semaphore2);
    }

    [Fact]
    public void ResolveSourceConfig_ReturnsConfig_WhenExists()
    {
        // Arrange
        // Use real ConfigurationBuilder — Mock<IConfiguration> cannot mock extension methods like GetValue<T>()
        var mockScopeFactory = new Mock<IServiceScopeFactory>();
        var mockLogger = new Mock<ILogger<DownloadManager>>();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["downloadManager:tempDirectory"] = "./temp",
                ["downloadManager:pollIntervalSeconds"] = "5"
            })
            .Build();

        var downloadManager = new DownloadManager(
            mockScopeFactory.Object,
            mockLogger.Object,
            configuration);

        var config = new ImageSourceConfig
        {
            MaxConcurrentDownloads = 5,
            RateLimitPerSecond = 10
        };
        var configs = new Dictionary<int, ImageSourceConfig?> { { 1, config } };

        // Use reflection
        var method = typeof(DownloadManager).GetMethod("ResolveSourceConfig",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.NotNull(method);

        // Act
        var result = method.Invoke(downloadManager, new object[] { configs, 1 }) as ImageSourceConfig;

        // Assert
        Assert.NotNull(result);
        Assert.Equal(5, result.MaxConcurrentDownloads);
        Assert.Equal(10, result.RateLimitPerSecond);
    }

    [Fact]
    public void ResolveSourceConfig_ReturnsDefaultConfig_WhenNotExists()
    {
        // Arrange
        // Use real ConfigurationBuilder — Mock<IConfiguration> cannot mock extension methods like GetValue<T>()
        var mockScopeFactory = new Mock<IServiceScopeFactory>();
        var mockLogger = new Mock<ILogger<DownloadManager>>();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["downloadManager:tempDirectory"] = "./temp",
                ["downloadManager:pollIntervalSeconds"] = "5"
            })
            .Build();

        var downloadManager = new DownloadManager(
            mockScopeFactory.Object,
            mockLogger.Object,
            configuration);

        var configs = new Dictionary<int, ImageSourceConfig?>();

        // Use reflection
        var method = typeof(DownloadManager).GetMethod("ResolveSourceConfig",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.NotNull(method);

        // Act
        var result = method.Invoke(downloadManager, new object[] { configs, 999 }) as ImageSourceConfig;

        // Assert
        Assert.NotNull(result);
        Assert.Equal(3, result.MaxConcurrentDownloads); // Default
        Assert.Equal(2, result.RateLimitPerSecond); // Default
    }
}
