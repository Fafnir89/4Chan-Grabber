using FourChanGrabber.Data;
using FourChanGrabber.Data.Models;
using FourChanGrabber.Data.Models.Enums;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;
using System.IO;

namespace FourChanGrabber.DownloadManager;

internal class DownloadManager : BackgroundService
{
    private bool isPaused;
    private IServiceScopeFactory scopeFactory;
    private int maxConcurrentDownloads = 3;
    private ConcurrentBag<Task> currentDownloads = new();

    public DownloadManager(IServiceScopeFactory scopeFactory)
    {
        this.scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        List<DownloadQueue> pendingDownloads = new();

        while (!stoppingToken.IsCancellationRequested)
        {
            // idle if paused
            if (isPaused)
            {
                await Task.Delay(1000, stoppingToken);
                continue;
            }

            // fetch tasks from db
            await fetchDownloadQueue(pendingDownloads, stoppingToken);

            // update current download Tasks
            manageDownloads(pendingDownloads, stoppingToken);

            // wait for next loop // TODO make interval configurable
            await Task.Delay(1000, stoppingToken);
        }

        // if IsCancellationRequested wait till all currentDownloads are done before exiting.
        while (currentDownloads.Count > 0)
        {
            await Task.Delay(500, stoppingToken);
        }
    }

    private async Task fetchDownloadQueue(List<DownloadQueue> pendingDownloads, CancellationToken stoppingToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MediaDbContext>();

        // fetch as much items needed to give each concurrentdownload a file (if possible)      
        // buffer by twice as much to show the user the files are being seen
        var items = await dbContext.DownloadQueue
            .Where(x => x.Status == DownloadStatus.New)
            .OrderBy(x => x.Id)  // for FIFO
            .Take((2 * maxConcurrentDownloads) - pendingDownloads.Count)
            .ToListAsync(stoppingToken);

        // change status to Pending
        foreach (DownloadQueue item in items)
        {
            item.Status = DownloadStatus.Pending;
            pendingDownloads.Add(item);
        }

        await dbContext.SaveChangesAsync(stoppingToken);
    }

    private void manageDownloads(List<DownloadQueue> pendingDownloads, CancellationToken stoppingToken)
    {
        while (currentDownloads.Count < maxConcurrentDownloads && pendingDownloads.Count > 0 && !stoppingToken.IsCancellationRequested)
        {
            var item = pendingDownloads.First();
            pendingDownloads.Remove(item);
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
        }
        catch
        {
            item.Status = DownloadStatus.Failed;
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
}