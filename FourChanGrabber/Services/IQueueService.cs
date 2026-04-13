using FourChanGrabber.Data.Models;
using FourChanGrabber.Data.Models.Enums;
using FourChanGrabber.Models;

namespace FourChanGrabber.Services;

public interface IQueueService
{
    Task<List<DownloadQueue>> GetPendingItemsAsync(int maxItems = 10, CancellationToken ct = default);
    Task<bool> TryLockItemAsync(int queueId, CancellationToken ct = default);
    Task UpdateStatusAsync(int queueId, DownloadStatus status, string? errorMessage = null, CancellationToken ct = default);
    Task CompleteDownloadAsync(int queueId, string fileHash, long fileSize, CancellationToken ct = default);
    Task HandleFailureAsync(int queueId, string errorMessage, bool canRetry, CancellationToken ct = default);
    Task<WorkerStatus> GetStatusAsync(CancellationToken ct = default);
    Task<QueueStats> GetStatsAsync(CancellationToken ct = default);
}
