<!-- Context: project-intelligence/technical | Priority: critical | Version: 1.1 | Updated: 2026-04-13 -->

# Technical Domain

> Blazor Server media grabber using 4chan API with SQLite persistence.

## Quick Reference

- **Stack**: Blazor Server, .NET 8, SQLite, Entity Framework Core
- **Purpose**: Download and catalog media from 4chan boards
- **Update When**: Database schema changes, new entities, migration applied, new services added

## Tech Stack

| Layer | Technology | Version | Notes |
|-------|-----------|---------|-------|
| Framework | Blazor Server | .NET 8 | Interactive web UI |
| Database | SQLite | 3.x | Local file-based storage |
| ORM | Entity Framework Core | 8.x | Fluent API configuration |
| Language | C# | 12 | Nullable reference types |

## Data Model

```
ImageSource (1) ─────< DownloadQueue (N)
    │                      │
    │                      ├────1 DownloadQueue_ChanBoard
    │                      
    └────< MediaData (N)
                 │
                 ├────1 ChanBoardData
                 │
                 └────< Tag (N)
```

### Entities

| Entity | Purpose | Key Fields |
|--------|---------|------------|
| `ImageSource` | Media source (e.g., FourChan) with config | Name, BaseUrl, ConfigJson |
| `DownloadQueue` | Download task queue | SourceUrl, TargetPath, Status, Priority |
| `DownloadQueue_ChanBoard` | Board-specific queue metadata | Board, ThreadUrl, ThreadId |
| `MediaData` | Downloaded file record | FileName, FilePath, FileHash, MediaType |
| `ChanBoardData` | Board-specific media metadata | Board, ThreadUrl, ThreadId |
| `Tag` | Media tags (composite key) | MediaDataId, TagText |

### Enums

| Enum | Values |
|------|--------|
| `DownloadStatus` | Pending, Downloading, Completed, Failed, Cancelled |
| `MediaType` | Unknown, Image, Video |

## Project Structure

```
FourChanGrabber/
├── Data/
│   ├── Models/
│   │   ├── Enums/
│   │   │   ├── DownloadStatus.cs
│   │   │   └── MediaType.cs
│   │   ├── ImageSource.cs
│   │   ├── DownloadQueue.cs
│   │   ├── DownloadQueue_ChanBoard.cs
│   │   ├── MediaData.cs
│   │   ├── ChanBoardData.cs
│   │   └── Tag.cs
│   ├── MediaDbContext.cs
│   ├── DatabaseSeeder.cs
│   └── Migrations/
│       └── 20260413174710_InitialCreate.cs
├── Models/
│   ├── SourceConfig.cs
│   ├── WorkerStatus.cs
│   ├── QueueStats.cs
│   ├── DownloadResult.cs
│   └── HashMismatchType.cs
├── Services/
│   ├── IQueueService.cs
│   ├── QueueService.cs
│   ├── IDownloadService.cs
│   ├── DownloadService.cs
│   └── DownloadManager.cs
├── Controllers/
│   └── DownloadQueueController.cs
└── Pages/
    └── Error.cshtml.cs
```

## EF Core Configuration

**Location**: `FourChanGrabber/Data/MediaDbContext.cs`

- Fluent API for all entity configuration
- Cascade delete on all foreign keys
- Composite key on `Tag` entity
- Unique index on `DownloadQueue_ChanBoard.DownloadQueueId`
- Named indexes with `HasDatabaseName()`

## Database Seeder

**Location**: `FourChanGrabber/Data/DatabaseSeeder.cs`

Seeds default `ImageSource` entries:
- **FourChan**: `https://a.4cdn.org`, 3 concurrent downloads, 2 req/s rate limit
- **Archive**: disabled, fallback for archived content

## Migrations

**Location**: `FourChanGrabber/Data/Migrations/`

Initial migration: `20260413174710_InitialCreate.cs`

## Download Manager

**Location**: `FourChanGrabber/Services/DownloadManager.cs`

Background service that polls `DownloadQueue` for pending items and downloads them concurrently.

### Architecture

| Component | Responsibility |
|-----------|----------------|
| `DownloadManager` | `BackgroundService` - polling loop, pause/resume, stale recovery |
| `QueueService` | Queue operations (lock, complete, fail, stats) |
| `DownloadService` | HTTP downloads with FlareSolverr + hash verification |

### Configuration

**Location**: `FourChanGrabber/appsettings.json`

```json
"downloadManager": {
  "pollIntervalSeconds": 5,
  "tempDirectory": "./temp/downloads",
  "maxRetries": 3
}
```

### Concurrency Model

- Per-source concurrency via `ConcurrentDictionary<int, SemaphoreSlim>`
- `MaxConcurrentDownloads` per `ImageSource` (default: 3)
- `RateLimitPerSecond` per source (default: 2 req/s)
- Active download count tracked via `Interlocked`

### States

`WorkerStatus` enum: `Idle`, `Running`, `Paused`

### FlareSolverr Integration

- Enabled when `CloudFlareProxyUrl` is set in `SourceConfig`
- Proxy URL: `http://localhost:8191` (FlareSolverr container)
- Bypasses CloudFlare challenges for 4chan requests

### API Endpoints

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/downloadqueue` | List queue items with filtering |
| GET | `/api/downloadqueue/{id}` | Get queue item details |
| GET | `/api/downloadqueue/status` | Worker status |
| GET | `/api/downloadqueue/stats` | Queue statistics |
| POST | `/api/downloadqueue/pause` | Pause worker |
| POST | `/api/downloadqueue/resume` | Resume worker |
| POST | `/api/downloadqueue/{id}/retry` | Retry failed item |
| DELETE | `/api/downloadqueue/{id}` | Delete queue item |

## 📂 Codebase References

**Core Infrastructure**:
- `FourChanGrabber/Data/MediaDbContext.cs` - DbContext with Fluent API
- `FourChanGrabber/Data/DatabaseSeeder.cs` - Database initialization

**Models**:
- `FourChanGrabber/Data/Models/Enums/DownloadStatus.cs` - Download state enum
- `FourChanGrabber/Data/Models/Enums/MediaType.cs` - Media type enum
- `FourChanGrabber/Data/Models/ImageSource.cs` - Source entity with config
- `FourChanGrabber/Data/Models/DownloadQueue.cs` - Queue entity
- `FourChanGrabber/Data/Models/DownloadQueue_ChanBoard.cs` - Board metadata
- `FourChanGrabber/Data/Models/MediaData.cs` - Media file entity
- `FourChanGrabber/Data/Models/ChanBoardData.cs` - Board-specific media data
- `FourChanGrabber/Data/Models/Tag.cs` - Tag entity (composite key)

**Download Manager Models**:
- `FourChanGrabber/Models/SourceConfig.cs` - Download config DTO (MaxConcurrent, RateLimit, Proxy)
- `FourChanGrabber/Models/WorkerStatus.cs` - Worker state enum (Idle/Running/Paused)
- `FourChanGrabber/Models/QueueStats.cs` - Queue statistics (Pending/Downloading/Completed/Failed)
- `FourChanGrabber/Models/DownloadResult.cs` - Download outcome with hash info
- `FourChanGrabber/Models/HashMismatchType.cs` - Hash mismatch classification

**Download Manager Services**:
- `FourChanGrabber/Services/IQueueService.cs` - Queue operations interface
- `FourChanGrabber/Services/QueueService.cs` - Queue operations implementation
- `FourChanGrabber/Services/IDownloadService.cs` - Download operations interface
- `FourChanGrabber/Services/DownloadService.cs` - HTTP download with FlareSolverr
- `FourChanGrabber/Services/DownloadManager.cs` - BackgroundService for polling

**Controllers**:
- `FourChanGrabber/Controllers/DownloadQueueController.cs` - REST API for queue management

**Migrations**:
- `FourChanGrabber/Data/Migrations/20260413174710_InitialCreate.cs` - Initial schema

## Related Files

- `.opencode/context/project-intelligence/navigation.md` - Project overview
- `.opencode/context/project-intelligence/business-domain.md` - Business context
