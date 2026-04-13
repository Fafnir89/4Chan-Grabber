using FourChanGrabber.Data;
using FourChanGrabber.Data.Models;
using FourChanGrabber.Data.Models.Enums;
using FourChanGrabber.Models;
using Microsoft.EntityFrameworkCore;

namespace FourChanGrabber.Services;

public class QueueService : IQueueService
{
    private readonly MediaDbContext _context;
    private WorkerStatus _currentStatus = WorkerStatus.Idle;

    public QueueService(MediaDbContext context)
    {
        _context = context;
    }

    public Task<List<DownloadQueue>> GetPendingItemsAsync(int maxItems = 10, CancellationToken ct = default)
    {
        return _context.DownloadQueue
            .Where(q => q.Status == DownloadStatus.Pending)
            .OrderByDescending(q => q.Priority)
            .ThenBy(q => q.RequestTime)
            .Take(maxItems)
            .ToListAsync(ct);
    }

    public async Task<bool> TryLockItemAsync(int queueId, CancellationToken ct = default)
    {
        var rowsAffected = await _context.Database.ExecuteSqlInterpolatedAsync(
            $@"UPDATE DownloadQueue 
               SET Status = {(int)DownloadStatus.Downloading}, StartedAt = {DateTime.UtcNow} 
               WHERE Id = {queueId} 
               AND Status = {(int)DownloadStatus.Pending} 
               AND StartedAt IS NULL", ct);

        return rowsAffected > 0;
    }

    public async Task UpdateStatusAsync(int queueId, DownloadStatus status, string? errorMessage = null, CancellationToken ct = default)
    {
        var queueItem = await _context.DownloadQueue.FindAsync(new object[] { queueId }, ct);
        if (queueItem == null) return;

        queueItem.Status = status;
        queueItem.ErrorMessage = errorMessage;

        if (status == DownloadStatus.Downloading)
        {
            queueItem.StartedAt = DateTime.UtcNow;
            queueItem.Attempts++;
        }
        else if (status == DownloadStatus.Completed)
        {
            queueItem.CompletedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync(ct);
    }

    public async Task CompleteDownloadAsync(int queueId, string fileHash, long fileSize, CancellationToken ct = default)
    {
        var queueItem = await _context.DownloadQueue
            .Include(q => q.ChanBoard)
            .FirstOrDefaultAsync(q => q.Id == queueId, ct);

        if (queueItem == null) return;

        // 1. Create MediaData
        var mediaData = new MediaData
        {
            ImageSourceId = queueItem.ImageSourceId,
            FileName = Path.GetFileName(queueItem.TargetPath),
            FilePath = queueItem.TargetPath,
            MimeType = DetectMimeType(queueItem.TargetPath),
            FileSize = fileSize,
            FileHash = fileHash,
            HashType = "sha256",
            MediaType = DetectMediaType(queueItem.TargetPath),
            DownloadedAt = DateTime.UtcNow
        };
        _context.MediaData.Add(mediaData);
        await _context.SaveChangesAsync(ct);

        // 2. Create ChanBoardData if exists
        if (queueItem.ChanBoard != null)
        {
            var chanBoardData = new ChanBoardData
            {
                MediaDataId = mediaData.Id,
                Board = queueItem.ChanBoard.Board,
                ThreadUrl = queueItem.ChanBoard.ThreadUrl,
                PostNumber = queueItem.ChanBoard.PostNumber,
                ThreadId = queueItem.ChanBoard.ThreadId,
                SourceTimestamp = queueItem.ChanBoard.SourceTimestamp
            };
            _context.ChanBoardData.Add(chanBoardData);
            _context.DownloadQueue_ChanBoard.Remove(queueItem.ChanBoard);
        }

        // 3. Delete DownloadQueue item
        _context.DownloadQueue.Remove(queueItem);
        await _context.SaveChangesAsync(ct);
    }

    public async Task HandleFailureAsync(int queueId, string errorMessage, bool canRetry, CancellationToken ct = default)
    {
        var queueItem = await _context.DownloadQueue.FindAsync(new object[] { queueId }, ct);
        if (queueItem == null) return;

        var shouldRetry = canRetry && queueItem.Attempts < 3;  // Default max retries

        if (shouldRetry)
        {
            queueItem.Status = DownloadStatus.Pending;
            queueItem.ErrorMessage = errorMessage;
        }
        else
        {
            queueItem.Status = DownloadStatus.Failed;
            queueItem.ErrorMessage = errorMessage;
        }

        await _context.SaveChangesAsync(ct);
    }

    public Task<WorkerStatus> GetStatusAsync(CancellationToken ct = default)
    {
        return Task.FromResult(_currentStatus);
    }

    public Task<QueueStats> GetStatsAsync(CancellationToken ct = default)
    {
        var stats = _context.DownloadQueue
            .GroupBy(q => q.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToList()
            .ToDictionary(x => x.Status, x => x.Count);

        return Task.FromResult(new QueueStats
        {
            Pending = stats.GetValueOrDefault(DownloadStatus.Pending),
            Downloading = stats.GetValueOrDefault(DownloadStatus.Downloading),
            Completed = stats.GetValueOrDefault(DownloadStatus.Completed),
            Failed = stats.GetValueOrDefault(DownloadStatus.Failed),
            Cancelled = stats.GetValueOrDefault(DownloadStatus.Cancelled)
        });
    }

    private static string DetectMimeType(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".pdf" => "application/pdf",
            _ => "application/octet-stream"
        };
    }

    private static MediaType DetectMediaType(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension switch
        {
            ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp" => MediaType.Image,
            ".mp4" or ".webm" or ".avi" or ".mkv" => MediaType.Video,
            _ => MediaType.Unknown
        };
    }
}
