using System.Collections.Concurrent;
using FourChanGrabber.Data;
using FourChanGrabber.Data.Models;
using FourChanGrabber.Data.Models.Enums;
using FourChanGrabber.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FourChanGrabber.Services;

/// <summary>
/// BackgroundService that polls DownloadQueue for pending items and downloads them
/// concurrently, respecting per-source concurrency limits via semaphores.
/// Pause/Resume is in-memory only — the worker finishes active downloads before stopping.
/// </summary>
public class DownloadManager : BackgroundService
{
    private const int StaleDownloadThresholdMinutes = 5;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DownloadManager> _logger;
    private readonly string _tempDirectory;
    private readonly int _pollIntervalSeconds;

    // Limits concurrent downloads per ImageSource (key = ImageSourceId)
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _sourceSemaphores = new();

    // Volatile so the pause flag is visible across threads without locking
    private volatile bool _isPaused;

    // Updated via Interlocked for thread-safe read/increment/decrement
    private int _activeDownloadCount;

    /// <summary>Current worker status derived from pause and active-download state.</summary>
    public WorkerStatus Status =>
        _isPaused ? WorkerStatus.Paused :
        _activeDownloadCount > 0 ? WorkerStatus.Running :
        WorkerStatus.Idle;

    /// <summary>Number of downloads currently in flight.</summary>
    public int ActiveDownloadCount => _activeDownloadCount;

    public DownloadManager(
        IServiceScopeFactory scopeFactory,
        ILogger<DownloadManager> logger,
        IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _tempDirectory = configuration["downloadManager:tempDirectory"] ?? "./temp/downloads";
        _pollIntervalSeconds = configuration.GetValue<int>("downloadManager:pollIntervalSeconds", 5);
    }

    /// <summary>Stops picking up new items. Downloads already in progress will finish.</summary>
    public void Pause() => _isPaused = true;

    /// <summary>Resumes normal polling after a pause.</summary>
    public void Resume() => _isPaused = false;

    // ─── BackgroundService entry point ────────────────────────────────────────────

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        CleanupTempFiles();
        await RecoverStaleDownloadsAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!_isPaused)
                {
                    await StartPendingDownloadsAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during download poll loop");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_pollIntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    // ─── Startup tasks ────────────────────────────────────────────────────────────

    /// <summary>Deletes all *.tmp files left in TempDirectory from prior crashed sessions.</summary>
    private void CleanupTempFiles()
    {
        try
        {
            if (!Directory.Exists(_tempDirectory)) return;

            var tmpFiles = Directory.GetFiles(_tempDirectory, "*.tmp");
            foreach (var file in tmpFiles)
            {
                File.Delete(file);
            }

            if (tmpFiles.Length > 0)
            {
                _logger.LogInformation(
                    "Cleaned up {Count} orphaned temp file(s) in {Dir}",
                    tmpFiles.Length, _tempDirectory);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clean up temp files in {TempDir}", _tempDirectory);
        }
    }

    /// <summary>
    /// Finds items stuck in Downloading status (worker crashed mid-download)
    /// and resets them to Pending so they will be retried.
    /// </summary>
    private async Task RecoverStaleDownloadsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MediaDbContext>();

        var staleThreshold = DateTime.UtcNow.AddMinutes(-StaleDownloadThresholdMinutes);
        var staleItems = await dbContext.DownloadQueue
            .Where(q => q.Status == DownloadStatus.Downloading && q.StartedAt < staleThreshold)
            .ToListAsync(ct);

        foreach (var item in staleItems)
        {
            _logger.LogWarning(
                "Recovering stale download item {Id} (started at {StartedAt})",
                item.Id, item.StartedAt);

            item.Status = DownloadStatus.Pending;
            item.StartedAt = null;
        }

        if (staleItems.Count > 0)
        {
            await dbContext.SaveChangesAsync(ct);
            _logger.LogInformation("Recovered {Count} stale download item(s)", staleItems.Count);
        }
    }

    // ─── Poll loop ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Fetches pending items with their source configs, then fires a download task
    /// for each item. Tasks are constrained by per-source semaphores, not awaited inline.
    /// </summary>
    private async Task StartPendingDownloadsAsync(CancellationToken ct)
    {
        var (pendingItems, sourceConfigs) = await FetchPendingItemsWithConfigsAsync(ct);
        if (pendingItems.Count == 0) return;

        foreach (var item in pendingItems)
        {
            var config = ResolveSourceConfig(sourceConfigs, item.ImageSourceId);

            // Fire-and-forget: each task waits on its source semaphore before downloading.
            // Exceptions are caught inside RunDownloadWithSemaphoreAsync.
            _ = RunDownloadWithSemaphoreAsync(item, config, ct);
        }
    }

    /// <summary>Loads pending queue items and their corresponding ImageSource configs in one scope.</summary>
    private async Task<(List<DownloadQueue> Items, Dictionary<int, ImageSourceConfig?> SourceConfigs)>
        FetchPendingItemsWithConfigsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var queueService = scope.ServiceProvider.GetRequiredService<IQueueService>();
        var dbContext = scope.ServiceProvider.GetRequiredService<MediaDbContext>();

        var items = await queueService.GetPendingItemsAsync(maxItems: 50, ct);
        if (items.Count == 0)
        {
            return (items, new Dictionary<int, ImageSourceConfig?>());
        }

        var sourceIds = items.Select(i => i.ImageSourceId).Distinct().ToList();
        var sources = await dbContext.ImageSources
            .Where(s => sourceIds.Contains(s.Id))
            .ToListAsync(ct);

        var sourceConfigs = sources.ToDictionary(s => s.Id, ImageSource.GetConfig);
        return (items, sourceConfigs);
    }

    // ─── Per-item download ────────────────────────────────────────────────────────

    /// <summary>
    /// Acquires the per-source semaphore then runs the download.
    /// Ensures _activeDownloadCount and semaphore are always released.
    /// </summary>
    private async Task RunDownloadWithSemaphoreAsync(
        DownloadQueue item, ImageSourceConfig config, CancellationToken ct)
    {
        var semaphore = GetOrCreateSemaphore(item.ImageSourceId, config.MaxConcurrentDownloads);

        await semaphore.WaitAsync(ct);
        try
        {
            Interlocked.Increment(ref _activeDownloadCount);
            await ExecuteDownloadAsync(item, config, ct);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Download cancelled for queue item {Id}", item.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error in download task for queue item {Id}", item.Id);
        }
        finally
        {
            semaphore.Release();
            Interlocked.Decrement(ref _activeDownloadCount);
        }
    }

    /// <summary>
    /// Atomically locks a queue item then delegates to DownloadService.
    /// Routes the result to CompleteDownloadAsync or HandleFailureAsync.
    /// </summary>
    private async Task ExecuteDownloadAsync(
        DownloadQueue item, ImageSourceConfig config, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var queueService = scope.ServiceProvider.GetRequiredService<IQueueService>();
        var downloadService = scope.ServiceProvider.GetRequiredService<IDownloadService>();

        // Atomic lock: sets Status = Downloading via raw SQL WHERE Status = Pending.
        // Returns false if another worker already claimed this item.
        var locked = await queueService.TryLockItemAsync(item.Id, ct);
        if (!locked) return;

        _logger.LogInformation("Downloading queue item {Id}: {Url}", item.Id, item.SourceUrl);

        var sourceConfig = MapToSourceConfig(config);
        var result = await downloadService.DownloadFileAsync(
            item.SourceUrl,
            item.TargetPath,
            item.ExpectedHash,
            sourceConfig,
            progress: null,
            ct);

        if (result.Success)
        {
            await HandleDownloadSuccessAsync(queueService, item.Id, result, ct);
        }
        else
        {
            await HandleDownloadFailureAsync(queueService, item.Id, result, ct);
        }
    }

    private async Task HandleDownloadSuccessAsync(
        IQueueService queueService, int queueId, DownloadResult result, CancellationToken ct)
    {
        await queueService.CompleteDownloadAsync(queueId, result.FileHash!, result.FileSize, ct);
        _logger.LogInformation(
            "Completed queue item {Id} ({Size} bytes, hash: {Hash})",
            queueId, result.FileSize, result.FileHash);
    }

    private async Task HandleDownloadFailureAsync(
        IQueueService queueService, int queueId, DownloadResult result, CancellationToken ct)
    {
        // A corrupt hash means the file is bad and retrying won't help.
        // All other failures (network, HTTP errors) may be retried.
        var canRetry = result.HashMismatch != HashMismatchType.Corrupt;
        var errorMessage = result.ErrorMessage ?? "Unknown error";

        await queueService.HandleFailureAsync(queueId, errorMessage, canRetry, ct);
        _logger.LogWarning(
            "Failed queue item {Id} (canRetry={CanRetry}): {Error}",
            queueId, canRetry, errorMessage);
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────────

    /// <summary>Gets or creates a semaphore for the given source, sized to its MaxConcurrentDownloads.</summary>
    private SemaphoreSlim GetOrCreateSemaphore(int sourceId, int maxConcurrent)
    {
        return _sourceSemaphores.GetOrAdd(
            sourceId,
            _ => new SemaphoreSlim(maxConcurrent, maxConcurrent));
    }

    /// <summary>Returns source-specific config, falling back to defaults when config is missing.</summary>
    private static ImageSourceConfig ResolveSourceConfig(
        Dictionary<int, ImageSourceConfig?> sourceConfigs, int imageSourceId)
    {
        return sourceConfigs.TryGetValue(imageSourceId, out var cfg) && cfg != null
            ? cfg
            : new ImageSourceConfig();
    }

    /// <summary>Maps the EF-bound ImageSourceConfig to the service-layer SourceConfig DTO.</summary>
    private static SourceConfig MapToSourceConfig(ImageSourceConfig config) => new()
    {
        MaxConcurrentDownloads = config.MaxConcurrentDownloads,
        RateLimitPerSecond = config.RateLimitPerSecond,
        CloudFlareProxyUrl = config.CloudFlareProxyUrl,
        RetryAttempts = config.RetryAttempts,
        DownloadTimeoutSeconds = config.DownloadTimeoutSeconds,
    };
}
