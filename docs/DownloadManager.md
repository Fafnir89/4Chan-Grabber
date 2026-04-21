# DownloadManager - Retry & Throttle System

## Overview

The `DownloadManager` handles parallel file downloads with automatic retry on failure and throttling to prevent server overload.

## Retry Mechanism

### Behavior

1. When a download fails with `HttpRequestException` (e.g., 429 rate limit, 404 not found):
   - `RetryCount` is incremented
   - `ErrorMessage` is recorded
   - If `RetryCount < maxRetries`:
     - `RequestTime` is set to `DateTime.UtcNow`
     - Status changes to `New` (requeued)
   - If `RetryCount >= maxRetries`:
     - Status changes to `Failed` (permanent)

2. When a download fails with `IOException`:
   - Same retry logic as above

3. When cancelled via `OperationCanceledException`:
   - Status changes to `Failed` (no retry)

### Requeue Priority

Failed items are requeued with `RequestTime = Now`, placing them at the front of the sorted queue (FIFO by request time). This ensures:
- Failed items are retried before newer items
- But not immediately (other items already in queue are processed first)

## Throttle System

### Behavior

When a 429 (Too Many Requests) response is received:
1. `throttleUntil` is set to `DateTime.UtcNow + throttleDurationSeconds`
2. `currentConcurrency` is set to `1`

While throttled:
- Only 1 download runs at a time
- Every `throttleDurationSeconds`, the throttle extends and `currentConcurrency` increments by 1
- Once `currentConcurrency` reaches `maxConcurrentDownloads`, throttle ends

### Throttle Flow Example

```
t=0s:   429 received → currentConcurrency=1, throttleUntil=+30s
t=30s:  throttle expires → currentConcurrency=2, throttleUntil=+30s
t=60s:  throttle expires → currentConcurrency=3, throttleUntil=null
```

### Concurrency Recovery

Unlike a simple timer-based throttle, this system:
- Starts at max concurrency (optimistic)
- Scales DOWN to 1 on failure
- Gradually scales UP as throttle periods pass
- Only scales to full max before ending throttle

## Configuration

### Configurable Values

| Setting | Default | Description |
|---------|---------|-------------|
| `maxConcurrentDownloads` | 3 | Maximum parallel downloads |
| `throttleDurationSeconds` | 30 | Throttle extension interval |
| `maxRetries` | 3 | Retries before permanent failure |
| `loopIntervalMs` | 1000 | Main loop polling interval |
| `shutdownWaitMs` | 500 | Wait time between shutdown checks |
| `httpClientTimeoutSeconds` | 30 | HTTP request timeout |

### Runtime Updates

```csharp
var manager = serviceProvider.GetRequiredService<DownloadManager>();
manager.UpdateSetting(5, DownloadManagerConfigKey.maxConcurrentDownloads);
```

### Available Config Keys

```csharp
public enum DownloadManagerConfigKey
{
    maxConcurrentDownloads,
    throttleDurationSeconds,
    maxRetries,
    loopIntervalMs,
    shutdownWaitMs
}
```

## Pause / Resume

```csharp
manager.Pause();   // Stops starting new downloads
manager.Unpause(); // Resumes downloading
```

Already active downloads complete before pausing. Status messages show current download count.

## Database Model

The `DownloadQueue` table tracks:

| Field | Type | Purpose |
|-------|------|---------|
| `Id` | int | Primary key |
| `DownloadUrl` | string | Source URL |
| `TargetPath` | string | Destination file path |
| `RequestTime` | DateTime | FIFO sort order (also updated on retry) |
| `Status` | enum | New, Pending, Downloading, Completed, Failed |
| `ErrorMessage` | string? | Last error if failed |
| `RetryCount` | int | Number of retry attempts |
| `ImageSourceId` | int | Foreign key to ImageSource |

## Logging Tags

| Tag | Description |
|-----|-------------|
| `[FETCH]` | Database queue fetch results |
| `[MANAGE]` | Download start and concurrency state |
| `[DOWNLOAD]` | Individual file download results |
| `[THROTTLE]` | Throttle activation, extension, and ending |
| `[CONFIG]` | Setting changes via UpdateSetting |
