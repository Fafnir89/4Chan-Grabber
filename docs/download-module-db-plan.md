# Download Module - Database Plan

> **Status:** For Implementation  
> **Created:** 2026-04-13

---

## Overview

Create the database structure for 4Chan Grabber. This is a **write-once structure** - Grabbers write to DownloadQueue tables, DownloadManager writes to FinishedFiles tables.

---

## Table Groups

### Group 1: DownloadQueue (Written by Grabbers)

### Group 2: FinishedFiles (Written by DownloadManager)

---

## Tables to Create

### 1. ImageSource

Core source registry. Seeded by DatabaseSeeder.

| Column | Type | Constraints | Description |
|--------|------|-------------|-------------|
| Id | int | PK, Identity | |
| Name | string | Required, Max 100 | "FourChan", "Archive" |
| BaseUrl | string | Required, Max 500 | API base URL |
| IsEnabled | bool | Default true | |

**Config JSON fields (stored as string):**
```json
{
  "maxConcurrentDownloads": 3,
  "rateLimitPerSecond": 2,
  "cloudFlareProxyUrl": null,
  "retryAttempts": 2,
  "downloadTimeoutSeconds": 120
}
```

---

### 2. DownloadQueue

Download tasks. Polled by DownloadManager.

| Column | Type | Constraints | Description |
|--------|------|-------------|-------------|
| Id | int | PK, Identity | |
| ImageSourceId | int | FK → ImageSource | Which source this belongs to |
| SourceUrl | string | Required, Max 2000 | Full URL to download from |
| TargetPath | string | Required, Max 1000 | Local path to save to |
| RequestTime | DateTime | Required | When grabber requested download (for FIFO) |
| Status | int | Required, Default 0 | 0=Pending, 1=Downloading, 2=Completed, 3=Failed, 4=Cancelled |
| Priority | int | Default 0 | Higher = more priority |
| Attempts | int | Default 0 | Retry count |
| ErrorMessage | string | Nullable | Last error description |
| ExpectedHash | string | Nullable, Max 128 | Expected hash (MD5 from source) |
| ExpectedHashType | string | Nullable, Max 20 | Hash type (e.g., "md5", "sha256") |
| CreatedAt | DateTime | Required | Row creation time |
| StartedAt | DateTime | Nullable | When download began |
| CompletedAt | DateTime | Nullable | When download finished |

**Indexes:**
- `IX_DownloadQueue_Status` ON (Status) WHERE Status = 0 (pending only)
- `IX_DownloadQueue_Priority_RequestTime` ON (Priority DESC, RequestTime ASC)
- `IX_DownloadQueue_ImageSourceId` ON (ImageSourceId)

---

### 3. DownloadQueue_ChanBoard

4Chan-specific metadata for download queue.

| Column | Type | Constraints | Description |
|--------|------|-------------|-------------|
| Id | int | PK, Identity | |
| DownloadQueueId | int | FK → DownloadQueue, Required | Link to parent queue item |
| Board | string | Required, Max 10 | "w", "wg", "b" |
| ThreadUrl | string | Required, Max 2000 | Full thread URL |
| PostNumber | int? | Nullable | Post number within thread |
| ThreadId | string | Required, Max 50 | External thread ID |
| SourceTimestamp | DateTime? | Nullable | When 4Chan says post was made |

**Indexes:**
- `IX_DownloadQueue_ChanBoard_DownloadQueueId` ON (DownloadQueueId)

---

### 4. MediaData

Completed downloads. Written by DownloadManager only.

| Column | Type | Constraints | Description |
|--------|------|-------------|-------------|
| Id | int | PK, Identity | |
| ImageSourceId | int | FK → ImageSource | Which source |
| FileName | string | Required, Max 500 | Original filename |
| FilePath | string | Required, Max 1000 | Actual path on storage |
| MimeType | string | Required, Max 100 | "image/jpeg", "video/webm" |
| FileSize | long | Required | Bytes |
| FileHash | string | Nullable, Max 128 | SHA256 hash |
| HashType | string | Nullable, Max 20 | "sha256" or "md5" |
| MediaType | int | Required, Default 0 | 0=Unknown, 1=Image, 2=Video |
| DownloadedAt | DateTime | Required | When download completed |

**Indexes:**
- `IX_MediaData_FileHash` ON (FileHash) WHERE FileHash IS NOT NULL
- `IX_MediaData_ImageSourceId` ON (ImageSourceId)

---

### 5. ChanBoardData

4Chan-specific metadata for completed downloads.

| Column | Type | Constraints | Description |
|--------|------|-------------|-------------|
| Id | int | PK, Identity | |
| MediaDataId | int | FK → MediaData, Required | Link to parent |
| Board | string | Required, Max 10 | "w", "wg", "b" |
| ThreadUrl | string | Required, Max 2000 | Full thread URL |
| PostNumber | int? | Nullable | Post number |
| ThreadId | string | Required, Max 50 | External thread ID |
| SourceTimestamp | DateTime? | Nullable | When source says it was posted |

**Indexes:**
- `IX_ChanBoardData_MediaDataId` ON (MediaDataId)

---

### 6. Tag (Future Expansion - Basic)

Simple tag table for future booru support.

| Column | Type | Constraints | Description |
|--------|------|-------------|-------------|
| MediaDataId | int | FK → MediaData, Required | Link to media |
| Tag | string | Required, Max 200 | Tag text |

**Note:** No separate Tag table (ID, Name). Simple (MediaDataId, Tag) for now. Expand later.

**Indexes:**
- `IX_Tag_MediaDataId` ON (MediaDataId)
- `IX_Tag_Tag` ON (Tag)

---

## Implementation Tasks

### Task 1: Create EF Core Models

Create files in `Data/Models/`:

- [ ] `ImageSource.cs`
- [ ] `DownloadQueue.cs`
- [ ] `DownloadQueue_ChanBoard.cs`
- [ ] `MediaData.cs`
- [ ] `ChanBoardData.cs`
- [ ] `Tag.cs`
- [ ] `Enums/DownloadStatus.cs` (Pending, Downloading, Completed, Failed, Cancelled)
- [ ] `Enums/MediaType.cs` (Unknown, Image, Video)

### Task 2: Create DbContext

Create `Data/MediaDbContext.cs`:

- [ ] Add DbSet for each table
- [ ] Configure relationships with Fluent API
- [ ] Configure decimal precision if needed
- [ ] Configure index for DownloadQueue (partial where clause for pending)

### Task 3: Create DatabaseSeeder

Create `Data/DatabaseSeeder.cs`:

- [ ] Seed ImageSource entries: "FourChan" (base: https://a.4cdn.org), "Archive" (user-configured)
- [ ] Check if seeds exist before inserting (idempotent)

### Task 4: Create Initial Migration

- [ ] Run `dotnet ef migrations add InitialCreate`
- [ ] Verify migration creates all tables correctly
- [ ] Apply migration to create database

---

## File Structure

```
Data/
├── MediaDbContext.cs
├── DatabaseSeeder.cs
├── Models/
│   ├── ImageSource.cs
│   ├── DownloadQueue.cs
│   ├── DownloadQueue_ChanBoard.cs
│   ├── MediaData.cs
│   ├── ChanBoardData.cs
│   ├── Tag.cs
│   └── Enums/
│       ├── DownloadStatus.cs
│       └── MediaType.cs
└── Migrations/
    └── (EF Core migrations)
```

---

## Notes

1. **DownloadQueue_ChanBoard.DownloadQueueId**: When DownloadManager completes a download, it reads this, creates ChanBoardData, then deletes this row.

2. **Config JSON on ImageSource**: Store as string in DB. Deserialize in application code when reading source config.

3. **No MediaData entry until download completes**: DownloadManager inserts to MediaData only after successful download.

4. **Future expansion**: DownloadQueue_Booru (same structure as DownloadQueue_ChanBoard) for future booru sources.
