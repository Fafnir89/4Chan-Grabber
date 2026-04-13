# Download Module - DownloadManager Plan

> **Status:** For Implementation  
> **Created:** 2026-04-13  
> **Depends On:** Database layer (completed)

---

## Overview

DownloadManager is a background worker that:
1. Polls `DownloadQueue` for pending items
2. Downloads files from `SourceUrl` to `TargetPath`
3. On success: writes to `MediaData` + `ChanBoardData`, cleans up queue
4. On failure: retries or marks as failed

---

## Project Structure

```
FourChanGrabber/
├── Services/
│   ├── DownloadManager.cs          # Background worker (BackgroundService)
│   ├── IDownloadService.cs         # Interface for testability
│   ├── DownloadService.cs          # HTTP download logic
│   └── QueueService.cs             # Queue operations (add, get, update)
├── Controllers/
│   └── DownloadQueueController.cs  # REST API for queue control
├── Models/
│   ├── SourceConfig.cs              # Deserialized ImageSource.ConfigJson
│   └── WorkerStatus.cs             # Running/Paused/Idle state
└── (Data/ from database layer)
```

---

## SourceConfig (ImageSource.ConfigJson)

```csharp
public class SourceConfig
{
    public int MaxConcurrentDownloads { get; set; } = 3;
    public int RateLimitPerSecond { get; set; } = 2;
    public string? CloudFlareProxyUrl { get; set; }
    public int RetryAttempts { get; set; } = 2;
    public int DownloadTimeoutSeconds { get; set; } = 120;
}
```

---

## WorkerStatus Enum

```csharp
public enum WorkerStatus
{
    Idle,       // Nothing to do, waiting for items
    Running,    // Actively processing
    Paused      // Paused by user
}
```

---

## QueueService

Provides all queue operations.

### Methods

```csharp
public interface IQueueService
{
    // Get next item(s) to download
    // Orders by: Priority DESC, RequestTime ASC
    // Filters by: Status = Pending
    // Respects: per-source MaxConcurrentDownloads
    Task<List<DownloadQueue>> GetPendingItemsAsync(int maxItems = 10, CancellationToken ct = default);
    
    // Lock item for download
    Task<bool> TryLockItemAsync(int queueId, CancellationToken ct = default);
    
    // Update status
    Task UpdateStatusAsync(int queueId, DownloadStatus status, string? errorMessage = null, CancellationToken ct = default);
    
    // On success: move to MediaData + ChanBoardData, delete queue item
    Task CompleteDownloadAsync(int queueId, string fileHash, long fileSize, CancellationToken ct = default);
    
    // On failure: reset to pending for retry or mark failed
    Task HandleFailureAsync(int queueId, string errorMessage, bool canRetry, CancellationToken ct = default);
    
    // Get worker status
    Task<WorkerStatus> GetStatusAsync(CancellationToken ct = default);
    
    // Stats
    Task<QueueStats> GetStatsAsync(CancellationToken ct = default);
}
```

### QueueStats

```csharp
public class QueueStats
{
    public int Pending { get; set; }
    public int Downloading { get; set; }
    public int Completed { get; set; }
    public int Failed { get; set; }
    public int Cancelled { get; set; }
}
```

---

## DownloadService

Handles HTTP downloads.

### Methods

```csharp
public interface IDownloadService
{
    Task<DownloadResult> DownloadFileAsync(
        string sourceUrl,
        string targetPath,
        string? expectedHash,
        SourceConfig config,
        IProgress<double>? progress = null,
        CancellationToken ct = default);
}

public class DownloadResult
{
    public bool Success { get; set; }
    public string? FileHash { get; set; }
    public long FileSize { get; set; }
    public string? ErrorMessage { get; set; }
    public HashMismatchType? HashMismatch { get; set; }
}

public enum HashMismatchType
{
    None,
    Retryable,      // Network error, should retry
    Corrupt         // File corrupted, don't retry
}
```

### Download Process

```
1. Create temp file: {TempDirectory}/{Guid}.tmp
2. HTTP GET to SourceUrl
   - Follow redirects
   - Apply rate limit (config.RateLimitPerSecond)
   - Use proxy if config.CloudFlareProxyUrl set
   - Timeout: config.DownloadTimeoutSeconds
   - User-Agent: "4Chan-Grabber/1.0"
3. Stream to temp file
4. Compute SHA256 hash of temp file
5. If expectedHash provided and computed != expected:
   - If config allows retry: return HashMismatchType.Retryable
   - Else: return HashMismatchType.Corrupt
6. Move temp file to TargetPath
7. Return result
```

---

## DownloadManager (BackgroundService)

```csharp
public class DownloadManager : BackgroundService
{
    // Polls every 5 seconds (configurable)
    // On each poll:
    //   1. If paused, skip
    //   2. Get pending items from queue
    //   3. For each item (respecting MaxConcurrentDownloads):
    //      - Lock item (Status = Downloading)
    //      - Download file
    //      - On success: CompleteDownloadAsync()
    //      - On failure: HandleFailureAsync()
    
    // Startup recovery:
    //   - Find items with Status = Downloading AND StartedAt < Now - 5min
    //   - Reset to Status = Pending (crashed worker recovery)
    
    // Pause behavior:
    //   - Stop picking up new items
    //   - Finish current downloads
    //   - Status = Paused (in-memory, not persisted)
}
```

### Configuration

```json
{
  "downloadManager": {
    "pollIntervalSeconds": 5,
    "tempDirectory": "./temp/downloads",
    "maxRetries": 3
  }
}
```

---

## DownloadQueueController (REST API)

Base path: `/api/downloadqueue`

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/downloadqueue` | List queue items (query: status, sourceId, limit, offset) |
| GET | `/api/downloadqueue/{id}` | Get specific item |
| POST | `/api/downloadqueue/pause` | Pause worker |
| POST | `/api/downloadqueue/resume` | Resume worker |
| GET | `/api/downloadqueue/status` | Get worker status (Idle/Running/Paused) |
| GET | `/api/downloadqueue/stats` | Get queue statistics |
| POST | `/api/downloadqueue/{id}/retry` | Retry failed item (resets to Pending) |
| DELETE | `/api/downloadqueue/{id}` | Delete queue item |

### GET /api/downloadqueue Response

```json
{
  "items": [
    {
      "id": 1,
      "imageSourceId": 1,
      "imageSourceName": "FourChan",
      "sourceUrl": "https://i.4cdn.org/w/123456.jpg",
      "targetPath": "./media/downloads/w/123456.jpg",
      "requestTime": "2026-04-13T10:00:00Z",
      "status": "Pending",
      "priority": 10,
      "attempts": 0,
      "errorMessage": null,
      "createdAt": "2026-04-13T10:00:00Z",
      "chanBoard": {
        "board": "w",
        "threadUrl": "https://boards.4chan.org/w/thread/123456",
        "postNumber": 12345,
        "threadId": "123456"
      }
    }
  ],
  "total": 150,
  "stats": {
    "pending": 100,
    "downloading": 5,
    "completed": 45,
    "failed": 0,
    "cancelled": 0
  }
}
```

### GET /api/downloadqueue/status Response

```json
{
  "status": "Running",
  "activeDownloads": 3,
  "maxConcurrent": 5
}
```

---

## CompleteDownloadAsync Flow

When a download succeeds:

```csharp
public async Task CompleteDownloadAsync(int queueId, string fileHash, long fileSize, CancellationToken ct)
{
    // 1. Get queue item + chanboard data
    var queueItem = await _context.DownloadQueue
        .Include(q => q.ChanBoard)
        .FirstOrDefaultAsync(q => q.Id == queueId, ct);
    
    if (queueItem == null) return;
    
    // 2. Create MediaData
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
    
    // 3. Create ChanBoardData if exists
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
        
        // 4. Delete DownloadQueue_ChanBoard
        _context.DownloadQueue_ChanBoard.Remove(queueItem.ChanBoard);
    }
    
    // 5. Delete DownloadQueue item
    _context.DownloadQueue.Remove(queueItem);
    
    // 6. Or mark as Completed (UI/grabber cleanup later)
    // Comment: Per spec, UI/grabber handles cleanup. Alternatively:
    // queueItem.Status = DownloadStatus.Completed;
    // queueItem.CompletedAt = DateTime.UtcNow;
    
    await _context.SaveChangesAsync(ct);
}
```

---

## Error Handling

| Error Type | Behavior |
|------------|----------|
| Network timeout | Retry up to RetryAttempts, then flag as corrupt |
| HTTP 404 | Mark Failed (resource gone) |
| HTTP 5xx | Retry (server error) |
| Hash mismatch | Retry 1-2x, then flag as corrupt |
| File write error | Mark Failed (permissions/disk full) |
| Rate limited | Wait and retry (built-in rate limiter) |

---

## Temp File Cleanup

- On startup: Delete all `*.tmp` files in TempDirectory
- On crash recovery: Orphaned temp files remain (acceptable)

---

## Implementation Tasks

### Task 1: SourceConfig + WorkerStatus Models
- [ ] `Models/SourceConfig.cs` - Deserialize from ImageSource.ConfigJson
- [ ] `Models/WorkerStatus.cs` - Idle/Running/Paused enum

### Task 2: QueueService
- [ ] `Services/IQueueService.cs` - Interface
- [ ] `Services/QueueService.cs` - All queue operations
- [ ] `Models/QueueStats.cs`

### Task 3: DownloadService
- [ ] `Services/IDownloadService.cs` - Interface
- [ ] `Services/DownloadService.cs` - HTTP download with hash verification
- [ ] `Models/DownloadResult.cs`
- [ ] Temp file handling
- [ ] Rate limiting

### Task 4: DownloadManager BackgroundService
- [ ] `Services/DownloadManager.cs` - BackgroundService
- [ ] Polling loop
- [ ] Concurrent download handling (SemaphoreSlim per source)
- [ ] Startup recovery
- [ ] Pause/Resume support

### Task 5: API Controller
- [ ] `Controllers/DownloadQueueController.cs`
- [ ] All endpoints in table above
- [ ] Response DTOs

### Task 6: Configuration
- [ ] Read from appsettings.json
- [ ] Inject IConfiguration into services

---

## Notes

1. **Concurrent downloads per source**: Use SemaphoreSlim per ImageSourceId
2. **Rate limiting**: Token bucket or simple delay between requests per source
3. **Hash verification**: 4Chan provides MD5, we compute SHA256 after download
4. **No persistence for pause**: Pause state is in-memory only (per spec)
5. **Cleanup**: DownloadQueue items are deleted on success (per spec: "UI or grabber will do the cleanup")
