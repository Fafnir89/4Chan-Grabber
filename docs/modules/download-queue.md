# Module: DownloadQueue

> **Status:** Draft - For Discussion  
> **Module Type:** Core Infrastructure  
> **Discussion Needed:** Yes - see open questions

---

## Purpose

Database-backed job queue for media downloads. Provides a reliable, persistent queue that survives app restarts and allows download workers to crash gracefully.

**What it is:**
- A table in SQLite that holds pending download tasks
- An internal API for UI to query and control the queue
- A polling source for the DownloadManager background service

**What it is NOT:**
- A full message queue (RabbitMQ, etc.)
- A downloader itself
- Source-specific (knows nothing about 4Chan or archives)

---

## Data Flow

```
┌──────────────┐         ┌──────────────┐         ┌──────────────┐
│   Grabber    │         │  Queue Table │         │ DownloadMgr │
│              │ ──Writes──>              │         │              │
│ (4Chan, etc) │         │ (SQLite)      │ ──Polls──> (Background)│
└──────────────┘         └──────────────┘         └──────┬───────┘
                                                          │
                                                          ▼
                                                   ┌──────────────┐
                                                   │  Filesystem  │
                                                   │  (media/)    │
                                                   └──────────────┘
```

---

## Database Schema (Proposed)

```csharp
public class DownloadTask
{
    public int Id { get; set; }
    
    // Source info (grabber fills this)
    public string SourceUrl { get; set; }        // "https://i.4cdn.org/w/1708033509123.jpg"
    public string SourceModule { get; set; }    // "FourChanApi", "Archive"
    public string SourceThreadId { get; set; }   // External thread reference
    
    // Target info (grabber specifies, queue respects)
    public string TargetPath { get; set; }       // "/media/downloads/w/1708033509123.jpg"
    public string FileName { get; set; }        // "1708033509123.jpg"
    public string FileHash { get; set; }        // SHA256 for deduplication
    public long FileSize { get; set; }          // Expected size
    
    // Media type
    public MediaType MediaType { get; set; }    // Image, Video, Unknown
    public string MimeType { get; set; }         // "image/jpeg", "video/webm"
    
    // Queue control
    public DownloadStatus Status { get; set; }   // Pending, Downloading, Completed, Failed, Cancelled
    public int Priority { get; set; }             // Higher = more priority
    public int Attempts { get; set; }             // Retry count
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    
    // Error handling
    public string? ErrorMessage { get; set; }
    public string? LastAttemptError { get; set; }
}

public enum MediaType
{
    Unknown = 0,
    Image = 1,
    Video = 2
}

public enum DownloadStatus
{
    Pending = 0,
    Downloading = 1,
    Completed = 2,
    Failed = 3,
    Cancelled = 4
}
```

---

## Queue Table Indexes

```sql
CREATE INDEX IX_DownloadTasks_Status ON DownloadTasks(Status) WHERE Status = 0;
CREATE INDEX IX_DownloadTasks_Priority ON DownloadTasks(Priority DESC, CreatedAt ASC);
CREATE INDEX IX_DownloadTasks_FileHash ON DownloadTasks(FileHash);
CREATE INDEX IX_DownloadTasks_SourceUrl ON DownloadTasks(SourceUrl);
```

---

## Internal API (For UI/Control)

### Endpoints

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/queue` | List queue items (with filters) |
| GET | `/api/queue/{id}` | Get specific item |
| POST | `/api/queue` | Add item to queue |
| POST | `/api/queue/batch` | Add multiple items |
| DELETE | `/api/queue/{id}` | Remove item |
| POST | `/api/queue/{id}/cancel` | Cancel item |
| POST | `/api/queue/{id}/retry` | Retry failed item |
| POST | `/api/queue/pause` | Pause downloader |
| POST | `/api/queue/resume` | Resume downloader |
| GET | `/api/queue/status` | Worker status |
| GET | `/api/queue/stats` | Queue statistics |

### GET /api/queue (Response Example)

```json
{
  "items": [
    {
      "id": 1,
      "sourceUrl": "https://i.4cdn.org/w/1708033509123.jpg",
      "targetPath": "/media/downloads/w/1708033509123.jpg",
      "fileName": "1708033509123.jpg",
      "mediaType": "Image",
      "status": "Completed",
      "priority": 0,
      "createdAt": "2026-04-13T10:00:00Z"
    }
  ],
  "total": 150,
  "pending": 100,
  "downloading": 5,
  "completed": 45,
  "failed": 0
}
```

---

## DownloadManager (Background Worker)

### Behavior

1. **Polling Loop**
   - Polls queue every N seconds (configurable, default 5s)
   - Fetches items with `Status = Pending`, ordered by Priority DESC, CreatedAt ASC
   - Locks items by setting `Status = Downloading`, `StartedAt = Now`

2. **Download Process**
   ```
   For each task:
     1. Check if file already exists at TargetPath
        - If exists AND hash matches → Mark Completed, skip download
        - If exists AND hash mismatch → Mark Failed (path conflict)
     2. Download from SourceUrl to temporary file
     3. Verify hash of downloaded file
        - If hash matches FileHash → Move to TargetPath, Mark Completed
        - If hash mismatch → Mark Failed, delete temp file
     4. On error:
        - Increment Attempts
        - If Attempts < MaxAttempts → Set back to Pending
        - If Attempts >= MaxAttempts → Mark Failed
   ```

3. **Crash Recovery**
   - On startup, find items with `Status = Downloading` and `StartedAt < Now - 5min`
   - Set them back to `Status = Pending` (incomplete download)
   - Temp files from crashed downloads are orphaned (can clean up later)

### Configuration

```json
{
  "downloadManager": {
    "pollIntervalSeconds": 5,
    "maxConcurrentDownloads": 3,
    "maxRetryAttempts": 3,
    "downloadTimeoutSeconds": 120,
    "tempDirectory": "./temp/downloads"
  }
}
```

---

## Open Questions (For Discussion)

### 1. What data should be in the queue item?

We need to decide all fields a grabber must/can provide. Questions:
- Should we store `ExpectedFileHash` from the API (4Chan provides MD5)?
- Should we store `Width`, `Height` if known?
- Should we store any post/metadata reference or just file info?
- What about duplicate detection - check hash before or after download?

### 2. How does the grabber specify the target path?

Options:
- Grabber provides full `TargetPath` - grabber decides everything
- Grabber provides `Board` + `FileName`, system builds path
- Config-driven path template: `"{board}/{year}/{month}/{filename}"`

### 3. Priority logic

Should priority be:
- Always FIFO (first in, first out)?
- Grabber-settable priority?
- Based on media type (videos first? or last)?
- Based on source (API first, archive second)?

### 4. Concurrent downloads

How many simultaneous downloads?
- Configurable max (e.g., 3)
- Should this be per-grabber or total?

### 5. What happens on hash mismatch?

If downloaded file's hash doesn't match expected:
- Re-download (network error)?
- Mark as failed (data corruption)?
- Accept anyway and update hash?

### 6. Temp file handling

- Where to store incomplete downloads?
- Clean up on restart?
- Clean up after X days?

---

## Next Steps

1. **Discuss and decide** the open questions above
2. **Finalize schema** based on decisions
3. **Define API contracts** precisely
4. **Write unit tests** for queue logic

---

## Related Documents

- [Modular Architecture Overview](../modular-architecture.md) - System-level view
- [DownloadManager](./download-manager.md) - Background worker spec (to be written)

---

*Discuss this module to finalize details before implementation.*
