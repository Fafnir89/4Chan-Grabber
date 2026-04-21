# DownloadManager TODOs

## High Priority

### HttpClient Reuse
**File:** `FourChanGrabber/DownloadManager/DownloadManager.cs`
**Issue:** Currently creates new `HttpClient` per download (line 141). Can cause socket exhaustion.
**Note:** Must work with parallel downloads from multiple sources (including flaresolverr proxy). Consider `IHttpClientFactory` to manage lifecycle per destination/host.

### Request Timeout
**File:** `FourChanGrabber/DownloadManager/DownloadManager.cs`
**Issue:** No timeout configured. Requests can hang indefinitely.
**Fix:** Add `HttpClient.Timeout` or per-request `CancellationToken` with timeout.

### Global Settings Integration
**File:** `FourChanGrabber/DownloadManager/DownloadManager.cs`
**Issue:** `UpdateSetting()` modifies runtime values, but hardcoded defaults in constructor (lines 33-37) are lost on restart.
**Fix:** When global `SettingsManager` is implemented, use it in `DownloadManager` constructor to initialize configurable values instead of hardcoded defaults.

---

## Medium Priority

### Exception Handling
**File:** `FourChanGrabber/DownloadManager/DownloadManager.cs`
**Issue:** `TaskCanceledException`, `IOException`, and other non-HttpRequestException errors are silently swallowed in `downloadFile()` catch block (only handles HttpRequestException).
**Fix:** Either expand catch or add a general outer catch-all that marks item as failed.

### CancellationToken Propagation
**File:** `FourChanGrabber/DownloadManager/DownloadManager.cs`
**Issue:** `File.WriteAllBytesAsync(targetPath, bytes, stoppingToken)` on line 150 doesn't pass the token, so cancellation during disk write may not be honored.
**Fix:** Pass `stoppingToken` to `WriteAllBytesAsync`.

### 4chan Rate Limit Headers
**File:** `FourChanGrabber/DownloadManager/DownloadManager.cs`
**Issue:** Should respect 4chan's `X-Reqs-More` and `Retry-After` headers for better throttle timing.
**Note:** When using flaresolverr, these headers may be modified. Need to investigate how flaresolverr exposes rate limit info.

---

## Low Priority

### Statistics
Track download success rate, average speed, retry counts per item.
**File:** `FourChanGrabber/DownloadManager/DownloadManager.cs`
**Note:** Add to this TODO when UI dashboard is ready.

### Duplicate Detection
Detect and handle duplicate URLs before downloading.
**Requirements:**
- Hash-based duplicate detection (compute hash of URL or content)
- Check against already-downloaded items in DB
- Decision: Skip, rename, or re-download with new timestamp
**File:** `FourChanGrabber/DownloadManager/DownloadManager.cs`
**Note:** Related to future hash handling system.

---

## Completed

- [x] Retry mechanism with exponential-style throttle
- [x] FIFO queue based on RequestTime
- [x] Pause/Unpause functionality
- [x] Configurable settings via UpdateSetting/ConfigKey enum
- [x] Exception handling for OperationCanceledException and IOException
- [x] CancellationToken propagation (was already correct)
