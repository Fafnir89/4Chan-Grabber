# Database Schema

> **Status:** Current  
> **Updated:** 2026-04-13

---

## Overview

SQLite database (`./data/4chan.db`) with EF Core 8 migrations. Write-once structure:
- **Grabbers** → `DownloadQueue` tables
- **DownloadManager** → `MediaData` tables

---

## Tables

### ImageSource

Source registry for media grabbers.

| Column | Type | Constraints | Description |
|--------|------|-------------|-------------|
| Id | int | PK, IDENTITY | |
| Name | string | Required, Max 100 | "FourChan", "Archive" |
| BaseUrl | string | Required, Max 500 | API base URL |
| IsEnabled | bool | Default true | |
| ConfigJson | string | Nullable | JSON configuration |

**ConfigJson Structure:**
```json
{
  "MaxConcurrentDownloads": 3,
  "RateLimitPerSecond": 2,
  "CloudFlareProxyUrl": null,
  "RetryAttempts": 2,
  "DownloadTimeoutSeconds": 120
}
```

---

### DownloadQueue

Download tasks polled by DownloadManager.

| Column | Type | Constraints | Description |
|--------|------|-------------|-------------|
| Id | int | PK, IDENTITY | |
| ImageSourceId | int | FK → ImageSource | Source reference |
| SourceUrl | string | Required, Max 2000 | Full URL to download |
| TargetPath | string | Required, Max 1000 | Local save path |
| RequestTime | DateTime | Required | FIFO ordering |
| Status | int | Required, Default 0 | 0=Pending, 1=Downloading, 2=Completed, 3=Failed, 4=Cancelled |
| Priority | int | Default 0 | Higher = more priority |
| Attempts | int | Default 0 | Retry count |
| ErrorMessage | string | Nullable | Last error |
| CreatedAt | DateTime | Required | Row creation |
| StartedAt | DateTime | Nullable | Download started |
| CompletedAt | DateTime | Nullable | Download finished |

**Indexes:**
- `IX_DownloadQueue_Status` ON (Status)
- `IX_DownloadQueue_Priority_RequestTime` ON (Priority DESC, RequestTime ASC)
- `IX_DownloadQueue_ImageSourceId` ON (ImageSourceId)

**Cascade:** ImageSourceId → ImageSource(Id)

---

### DownloadQueue_ChanBoard

4Chan-specific queue metadata.

| Column | Type | Constraints | Description |
|--------|------|-------------|-------------|
| Id | int | PK, IDENTITY | |
| DownloadQueueId | int | FK → DownloadQueue, Unique | Parent queue item |
| Board | string | Required, Max 10 | "w", "wg", "b" |
| ThreadUrl | string | Required, Max 2000 | Full thread URL |
| PostNumber | int? | Nullable | Post number |
| ThreadId | string | Required, Max 50 | External thread ID |
| SourceTimestamp | DateTime? | Nullable | When 4Chan posted |

**Indexes:**
- `IX_DownloadQueue_ChanBoard_DownloadQueueId` ON (DownloadQueueId) UNIQUE

**Cascade:** DownloadQueueId → DownloadQueue(Id)

---

### MediaData

Completed downloads written by DownloadManager.

| Column | Type | Constraints | Description |
|--------|------|-------------|-------------|
| Id | int | PK, IDENTITY | |
| ImageSourceId | int | FK → ImageSource | Source reference |
| FileName | string | Required, Max 500 | Original filename |
| FilePath | string | Required, Max 1000 | Actual storage path |
| MimeType | string | Required, Max 100 | "image/jpeg", "video/webm" |
| FileSize | long | Required | Bytes |
| FileHash | string | Nullable, Max 128 | SHA256 hash |
| HashType | string | Nullable, Max 20 | "sha256" or "md5" |
| MediaType | int | Required, Default 0 | 0=Unknown, 1=Image, 2=Video |
| DownloadedAt | DateTime | Required | Completion time |

**Indexes:**
- `IX_MediaData_FileHash` ON (FileHash)
- `IX_MediaData_ImageSourceId` ON (ImageSourceId)

**Cascade:** ImageSourceId → ImageSource(Id)

---

### ChanBoardData

4Chan-specific metadata for completed downloads.

| Column | Type | Constraints | Description |
|--------|------|-------------|-------------|
| Id | int | PK, IDENTITY | |
| MediaDataId | int | FK → MediaData, Unique | Parent media |
| Board | string | Required, Max 10 | "w", "wg", "b" |
| ThreadUrl | string | Required, Max 2000 | Full thread URL |
| PostNumber | int? | Nullable | Post number |
| ThreadId | string | Required, Max 50 | External thread ID |
| SourceTimestamp | DateTime? | Nullable | When source posted |

**Indexes:**
- `IX_ChanBoardData_MediaDataId` ON (MediaDataId) UNIQUE

**Cascade:** MediaDataId → MediaData(Id)

---

### Tag

Simple tags for media (future booru support).

| Column | Type | Constraints | Description |
|--------|------|-------------|-------------|
| MediaDataId | int | FK → MediaData | Parent media |
| Tag | string | Required, Max 200 | Tag text |

**Primary Key:** (MediaDataId, Tag) - composite

**Indexes:**
- `IX_Tag_MediaDataId` ON (MediaDataId)
- `IX_Tag_Tag` ON (Tag)

**Cascade:** MediaDataId → MediaData(Id)

---

## Enums

### DownloadStatus
```
0 = Pending
1 = Downloading
2 = Completed
3 = Failed
4 = Cancelled
```

### MediaType
```
0 = Unknown
1 = Image
2 = Video
```

---

## Relationships

```
ImageSource (1)
    │
    ├───< DownloadQueue (N)
    │         │
    │         └───1 DownloadQueue_ChanBoard
    │
    └───< MediaData (N)
              │
              ├───1 ChanBoardData
              │
              └───< Tag (N)
```

---

## Seeded Data

| Id | Name | BaseUrl | IsEnabled |
|----|------|---------|-----------|
| 1 | FourChan | https://a.4cdn.org | true |
| 2 | Archive | (empty) | false |

---

## File Locations

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
└── data/
    └── 4chan.db
```

---

## Notes

1. **FileHash on queue** - Used to verify downloads against expected hash (4Chan provides MD5)
2. **FileHash on MediaData** - Actual computed hash after download completes
3. **Workflow** - DownloadManager reads `DownloadQueue_ChanBoard`, creates `ChanBoardData`, deletes `DownloadQueue_ChanBoard`
4. **Partial indexes** - Not implemented (SQLite limitation with EF Core). Consider raw SQL if needed.
