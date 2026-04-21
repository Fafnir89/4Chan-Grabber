using FourChanGrabber.Data;
using FourChanGrabber.Data.Models;
using FourChanGrabber.Data.Models.Enums;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;
using System.IO;
using System.Net;

namespace FourChanGrabber.DownloadManager;

public enum DownloadManagerConfigKey
{
    maxConcurrentDownloads,
    throttleDurationSeconds,
    maxRetries,
    loopIntervalMs,
    shutdownWaitMs
}

internal class DownloadManager : BackgroundService
{
    private IServiceScopeFactory scopeFactory;
    private ConcurrentBag<Task> currentDownloads = new();
    private bool isPaused;

    private DateTime? throttleUntil;
    private int currentConcurrency;

    private int maxConcurrentDownloads = 3;
    private int throttleDurationSeconds = 30;  // TODO: make configurable
    private int maxRetries = 3;  // TODO: make configurable
    private int loopIntervalMs = 1000;  // TODO: make configurable
    private int shutdownWaitMs = 500;  // TODO: make configurable

    public DownloadManager(IServiceScopeFactory scopeFactory)
    {
        this.scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        List<DownloadQueue> pendingDownloads = new();
        currentConcurrency = maxConcurrentDownloads;

        while (!stoppingToken.IsCancellationRequested)
        {
            // idle if paused
            if (isPaused)
            {
                await Task.Delay(loopIntervalMs, stoppingToken);
                continue;
            }

            // check if throttle end reached
            if (throttleUntil != null && DateTime.UtcNow >= throttleUntil)
            {
                if (currentConcurrency < maxConcurrentDownloads)
                {
                    throttleUntil = DateTime.UtcNow.AddSeconds(throttleDurationSeconds);
                    currentConcurrency++;
                    Console.WriteLine($"[THROTTLE] Extended. Concurrency: {currentConcurrency}/{maxConcurrentDownloads}");
                }
                else
                {
                    throttleUntil = null;
                    Console.WriteLine("[THROTTLE] Ended. Full concurrency restored.");
                }
            }

            // fetch tasks from db
            await fetchDownloadQueue(pendingDownloads, stoppingToken);

            // update current download Tasks
            manageDownloads(pendingDownloads, stoppingToken);

            // wait for next loop
            await Task.Delay(loopIntervalMs, stoppingToken);
        }

        // if IsCancellationRequested wait till all currentDownloads are done before exiting.
        while (currentDownloads.Count > 0)
        {
            await Task.Delay(shutdownWaitMs, stoppingToken);
        }
    }

    private async Task fetchDownloadQueue(List<DownloadQueue> pendingDownloads, CancellationToken stoppingToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MediaDbContext>();

        var items = await dbContext.DownloadQueue
            .Where(x => (x.Status == DownloadStatus.New || x.Status == DownloadStatus.Pending) && !pendingDownloads.Contains(x))
            .OrderBy(x => x.RequestTime)  // oldest first (FIFO)
            .Take((2 * maxConcurrentDownloads) - pendingDownloads.Count)
            .ToListAsync(stoppingToken);

        foreach (DownloadQueue item in items)
        {
            item.Status = DownloadStatus.Pending;
            pendingDownloads.Add(item);
        }

        if (items.Count > 0)
        {
            Console.WriteLine($"[FETCH] Retrieved {items.Count} items. Queue depth: {pendingDownloads.Count}");
        }

        await dbContext.SaveChangesAsync(stoppingToken);
    }

    private void manageDownloads(List<DownloadQueue> pendingDownloads, CancellationToken stoppingToken)
    {
        if (currentConcurrency != maxConcurrentDownloads)
        {
            Console.WriteLine($"[MANAGE] Throttled. current Concurrency: {currentConcurrency}, Active downloads: {currentDownloads.Count}");
        }

        while (currentDownloads.Count < currentConcurrency && pendingDownloads.Count > 0 && !stoppingToken.IsCancellationRequested)
        {
            var item = pendingDownloads.First();
            pendingDownloads.Remove(item);
            Console.WriteLine($"[MANAGE] Starting download {item.Id}. Active: {currentDownloads.Count + 1}/{currentConcurrency}");
            var downloadTask = Task.Run(() => downloadFile(item, stoppingToken));
            currentDownloads.Add(downloadTask);
        }
    }

    private async Task downloadFile(DownloadQueue item, CancellationToken stoppingToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MediaDbContext>();

        dbContext.Attach(item);

        try
        {
            item.Status = DownloadStatus.Downloading;
            await dbContext.SaveChangesAsync(stoppingToken);

            using var client = new HttpClient();
            var bytes = await client.GetByteArrayAsync(item.DownloadUrl, stoppingToken);

            var directory = Path.GetDirectoryName(item.TargetPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            var targetPath = GetUniqueFilePath(item.TargetPath);
            await System.IO.File.WriteAllBytesAsync(targetPath, bytes, stoppingToken);

            item.Status = DownloadStatus.Completed;
            await dbContext.SaveChangesAsync(stoppingToken);

            Console.WriteLine($"[DOWNLOAD] Item {item.Id} completed. Saved to: {targetPath}");
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == (HttpStatusCode)404)
            {
                throttleUntil = DateTime.UtcNow.AddSeconds(throttleDurationSeconds);
                currentConcurrency = 1;
                Console.WriteLine($"[DOWNLOAD] Item {item.Id} failed with 404. Throttle activated.");
                Console.WriteLine($"[THROTTLE] Activated. Concurrency reduced to 1.");
            }

            item.RetryCount++;
            item.ErrorMessage = ex.Message;

            if (item.RetryCount < maxRetries)
            {
                item.RequestTime = DateTime.UtcNow;
                item.Status = DownloadStatus.New;
                Console.WriteLine($"[DOWNLOAD] Item {item.Id} requeued. Retry {item.RetryCount}/{maxRetries}");
            }
            else
            {
                item.Status = DownloadStatus.Failed;
                Console.WriteLine($"[DOWNLOAD] Item {item.Id} permanently failed after {item.RetryCount} retries.");
            }

            await dbContext.SaveChangesAsync(stoppingToken);
        }

        currentDownloads.TryTake(out _);
    }

    private string GetUniqueFilePath(string targetPath)
    {
        var dir = Path.GetDirectoryName(targetPath) ?? "";
        var name = Path.GetFileNameWithoutExtension(targetPath);
        var ext = Path.GetExtension(targetPath);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return Path.Combine(dir, $"{name}_{timestamp}{ext}");
    }

    public void UpdateSetting(int value, DownloadManagerConfigKey key)
    {
        switch (key)
        {
            case DownloadManagerConfigKey.maxConcurrentDownloads:
                Console.WriteLine($"[CONFIG] maxConcurrentDownloads changed: {maxConcurrentDownloads} -> {value}");
                maxConcurrentDownloads = value;
                break;
            case DownloadManagerConfigKey.throttleDurationSeconds:
                Console.WriteLine($"[CONFIG] throttleDurationSeconds changed: {throttleDurationSeconds} -> {value}");
                throttleDurationSeconds = value;
                break;
            case DownloadManagerConfigKey.maxRetries:
                Console.WriteLine($"[CONFIG] maxRetries changed: {maxRetries} -> {value}");
                maxRetries = value;
                break;
            case DownloadManagerConfigKey.loopIntervalMs:
                Console.WriteLine($"[CONFIG] loopIntervalMs changed: {loopIntervalMs} -> {value}");
                loopIntervalMs = value;
                break;
            case DownloadManagerConfigKey.shutdownWaitMs:
                Console.WriteLine($"[CONFIG] shutdownWaitMs changed: {shutdownWaitMs} -> {value}");
                shutdownWaitMs = value;
                break;
        }
    }

    public void Pause()
    {
        if (isPaused)
        {
            Console.WriteLine($"[MANAGE] Already paused. {currentDownloads.Count} downloads still active.");
        }
        else
        {
            isPaused = true;
            Console.WriteLine($"[MANAGE] Pausing. {currentDownloads.Count} downloads will finish before idle.");
        }
    }

    public void Unpause()
    {
        if (!isPaused)
        {
            Console.WriteLine($"[MANAGE] Already running. Unpause not needed.");
        }
        else
        {
            isPaused = false;
            Console.WriteLine($"[MANAGE] Resumed. Concurrency: {currentConcurrency}/{maxConcurrentDownloads}");
        }
    }
}