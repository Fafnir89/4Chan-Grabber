# 4Chan Grabber - Specification Document

> **Status:** Draft - In Progress  
> **Last Updated:** 2026-04-13

---

## Table of Contents

1. [Project Overview](#1-project-overview)
2. [Feature Specifications](#2-feature-specifications)
3. [Architecture Decisions](#3-architecture-decisions)
4. [UI/UX Design](#4-uxui-design)
5. [Data Models](#5-data-models)
6. [Grabber Interface](#6-grabber-interface)
7. [Milestones & Phases](#7-milestones--phases)

---

## Architecture Documentation

**Detailed modular architecture is documented separately:**

- [Modular Architecture Overview](./docs/modular-architecture.md) - System-level module map and API design
- [DownloadQueue Module](./docs/modules/download-queue.md) - ⭐ **Next Discussion Topic**
- [DownloadManager Module](./docs/modules/download-manager.md) - Background worker (depends on DownloadQueue)

---

*When resuming work, continue from [DownloadQueue Module](./docs/modules/download-queue.md) open questions.*

---

## 1. Project Overview

### 1.1 Project Name
**4Chan Grabber**

### 1.2 Project Type
Personal media management and download tool - automatic media downloader with gallery organization

### 1.3 Core Value Proposition
An automatic downloader that monitors 4Chan boards via the API and archive sites, downloads only images and videos, tracks what it's downloaded to avoid duplicates, and provides a Tinder-style gallery for reviewing and organizing media with keyboard controls.

### 1.4 Target User
- Primary: Fafnir89 (owner)
- Single-user, local machine deployment
- One instance only, manually started/stopped as needed
- No cloud/server deployment planned

### 1.5 Core Features Summary

| Feature | Description |
|---------|-------------|
| 4Chan API Downloader | Download media from 4Chan via their official API with keyword filtering |
| Archive Downloader | Download from archive sites (includes /b/) with gap detection and recovery |
| Gallery | Browse and rate downloaded media with Tinder-style swipe interface |
| Two-Stage Rating | Like/dislike in main gallery, then rate recycle bin items again |

### 1.6 Out of Scope (v1)
- [ ] Cloud deployment
- [ ] Multi-user support
- [ ] Hydrus network sync
- [ ] Additional grabbers beyond 4Chan and one archive
- [ ] Plugin system for runtime-loaded grabbers

### 1.7 Running Behavior
- **Manual start/stop** - User runs app when they want it to run
- **Pause capability** - Can pause the entire downloader to avoid bandwidth slowdown
- **Single instance** - No service, no background running
- **Self-contained** - Runs as `dotnet run` or double-click executable

---

## 2. Feature Specifications

### 2.1 4Chan API Downloader

**Status:** Planned  
**Priority:** P0 (Must Have - v1)

#### Description
Downloads media (images and videos only) from 4Chan boards via the official JSON API. Monitors specified boards for new threads matching keyword filters. Tracks all downloaded threads and media to avoid downloading the same content twice.

Key behaviors:
- Only downloads media files (images/videos) - no text posts
- Tracks threads that have 404'd (deleted)
- Updates every few seconds when running
- Saves download records to SQLite database

#### User Flow
1. User configures boards to monitor and keyword filters in Settings
2. User clicks "Start" to begin monitoring
3. App checks API at configured interval for new threads
4. Matching threads are queued for download
5. Media is downloaded to local storage
6. User can view download progress in UI
7. User can pause/stop at any time

#### Configuration Options
| Option | Type | Default | Description |
|--------|------|---------|-------------|
| boards | list | [] (user configures) | Which boards to monitor (e.g., /w/, /wg/, /hr/) |
| check_interval | seconds | 30 | How often to check for new threads |
| keyword_filters | list | [] | Filter threads by keywords (include/exclude) |
| download_media_types | list | ["jpg", "png", "gif", "webm", "mp4"] | Which file types to download |
| max_threads_per_board | int | 50 | Max threads to track per board |

#### 4Chan API Behavior Details
- **Real-time updates**: When running, checks API every N seconds
- **Thread tracking**: Records last checked time per thread
- **404 handling**: Threads that return 404 are marked as deleted but kept in DB
- **No duplicate downloads**: FileHash or FileName+Size combination prevents re-downloading

#### Example Timeline
```
API Downloader running:
- 1:00 AM: Starts checking, finds thread #123 → downloads media
- 1:00 AM → 3:00 AM: Continues downloading new threads
- 3:00 AM: User stops app
- 6:00 AM: User starts app again
- 6:00 AM: Resumes from where it left off, checks for new threads since 3:00 AM
```

#### Questions/Clarifications Needed
- [x] What boards specifically? → User to configure (TBD)
- [x] Keyword filter logic? → Include/exclude patterns (detailed in later session)
- [x] 404 handling? → Mark as 404 in DB, continue tracking for gap-fill from archive

---

### 2.2 Archive Downloader

**Status:** Planned  
**Priority:** P0 (Must Have - v1)

#### Description
Downloads media from an archive site that has good retention including /b/ (the infamous board that 4Chan itself doesn't keep). The archive site maintains time-based coverage of threads.

Key behaviors:
- Works with a specific archive site (TBD - user knows one with good retention)
- Records the time range it checked
- Can detect gaps when computer was off
- Can recover missing media from archive for threads that 404'd on 4Chan
- Slowly moves backward in time when catching up

#### Archive vs API Relationship
```
API Downloader: Handles RECENT threads (real-time, forward)
Archive Downloader: Handles PAST threads (backward recovery)

Example timeline with computer off:
- API: 1:00 AM to 3:00 AM (downloading new threads)
- Archive: 0:00 AM to 1:00 AM (backward fill)
- Computer OFF
- Computer ON at 6:00 AM
- API: Checks threads from 3:00 AM to 6:00 AM (catches up recent)
- Archive: Checks threads that 404'd between 3:00 AM and 6:00 AM
- Archive: Then continues backward from where it left off
```

#### User Flow
1. User configures archive site URL in Settings
2. User clicks "Start Archive Recovery"
3. App checks for gaps since last run
4. App checks threads that 404'd for missing media
5. App slowly fills in historical gaps working backward
6. User can pause/stop at any time

#### Configuration Options
| Option | Type | Default | Description |
|--------|------|---------|-------------|
| archive_url | string | "" | Archive site base URL |
| check_interval | seconds | 300 | How often to check archive (less frequent than API) |
| gap_fill_enabled | bool | true | Automatically fill gaps when computer was off |
| 404_recovery_enabled | bool | true | Recover media from archive for 404'd threads |
| max_lookback_days | int | 365 | How far back to look for missing media |

#### Archive Behavior Details
- **Timeframe tracking**: Records last checked time range
- **Gap detection**: Identifies periods where computer was off
- **404 recovery**: For threads that 404'd on 4Chan, checks if archive has them
- **Backward fill**: When catching up, works backward from last checkpoint
- **Eventually reaches oldest**: Once all gaps filled, idles until new gaps appear

#### Questions/Clarifications Needed
- [ ] Which archive site specifically? → User to specify
- [ ] What filters does the archive support? → User to specify (will need to check archive capabilities)

---

### 2.3 Gallery

**Status:** Planned  
**Priority:** P0 (Must Have - v1)

#### Description
Browsable gallery for viewing and rating downloaded media. Two modes:
1. **Rating Mode (Tinder-style)**: One image at a time, keyboard-driven like/Dislike
2. **Browse Mode (Grid view)**: Thumbnail grid for browsing without making decisions

Visual inspiration: Picasa (old Google photo software) - clean, simple, efficient

#### User Flow (Rating Mode)
1. User navigates to Gallery → Rate
2. Media appears one at a time, large and centered
3. User presses Right Arrow or L to LIKE → file moves to liked folder
4. User presses Left Arrow or J to DISLIKE → file moves to recycle bin
5. User presses Space to SKIP → next image, no decision made
6. User presses F for fullscreen view
7. Repeat until recycle bin has items for Stage 2

#### User Flow (Browse Mode)
1. User navigates to Gallery → Browse
2. Grid of thumbnails displayed
3. User can filter by board, date, rating status
4. User can click to enlarge or rate directly
5. Keyboard navigation works here too

#### UI Layout - Rating Mode
```
┌─────────────────────────────────────────────────────────────┐
│                    ┌─────────────────┐                     │
│                    │                 │                     │
│                    │                 │                     │
│                    │   [IMAGE]       │                     │
│                    │                 │                     │
│                    │                 │                     │
│                    └─────────────────┘                     │
│                                                             │
│  /b/ - Thread #12345678                                     │
│  "looking for this specific thing"                          │
│                                                             │
│           ← LEFT (dislike)    RIGHT (like) →                │
│                                                             │
│  [Space] Skip  [L] Like  [J] Dislike  [F] Fullscreen       │
└─────────────────────────────────────────────────────────────┘
```

#### UI Layout - Browse Mode
```
┌─────────────────────────────────────────────────────────────┐
│ Filter: [All ▼] [Board ▼] [Date ▼] [Rating ▼]    [Grid ▼]  │
├─────────────────────────────────────────────────────────────┤
│ ┌─────┐ ┌─────┐ ┌─────┐ ┌─────┐ ┌─────┐ ┌─────┐ ┌─────┐    │
│ │     │ │     │ │     │ │     │ │     │ │     │ │     │    │
│ │ 📷  │ │ 📷  │ │ 📷  │ │ 📷  │ │ 📷  │ │ 📷  │ │ 📷  │    │
│ │     │ │     │ │     │ │     │ │     │ │     │ │     │    │
│ └─────┘ └─────┘ └─────┘ └─────┘ └─────┘ └─────┘ └─────┘    │
│ ┌─────┐ ┌─────┐ ┌─────┐ ┌─────┐ ┌─────┐ ┌─────┐ ┌─────┐    │
│ │     │ │     │ │     │ │     │ │     │ │     │ │     │    │
│ │ 📷  │ │ 📷  │ │ 📷  │ │ 📷  │ │ 📷  │ │ 📷  │ │ 📷  │    │
│ │     │ │     │ │     │ │     │ │     │ │     │ │     │    │
│ └─────┘ └─────┘ └─────┘ └─────┘ └─────┘ └─────┘ └─────┘    │
└─────────────────────────────────────────────────────────────┘
```

#### Questions/Clarifications Needed
- [x] Keyboard controls? → Arrow keys, L/J, Space, F, Esc (confirmed)
- [x] Sorting options? → By board, date, rating (to be detailed)
- [x] Visual style? → Picasa-inspired, clean (confirmed)

---

### 2.4 Two-Stage Rating System

**Status:** Planned  
**Priority:** P1 (Should Have)

#### Description
Rating happens in two stages to help user make better decisions:

**Stage 1 - Main Gallery:**
- User rates all new downloads
- LIKES → moved to `/media/liked/`
- DISLIKES → moved to `/media/recycle/`

**Stage 2 - Recycle Bin Review:**
- After some items accumulate in recycle bin
- User can do a second pass
- This time, likes are promoted (restored to liked)
- Dislikes are permanently deleted
- This prevents hasty rejections - user gets a "second chance"

#### User Flow - Stage 2
1. User navigates to Recycle Bin
2. Similar Tinder-style interface
3. Right Arrow/L = "Actually I like this" → moves back to liked
4. Left Arrow/J = "Still dislike" → marked for permanent deletion
5. Space = Skip
6. After review, user can "Empty Recycle Bin" to permanently delete marked items

#### Two-Stage Rationale
```
Problem: In dating apps, users sometimes swipe too fast and regret it later.

Solution: Two-stage review.
- First pass: Quick triage (like/dislike)
- Second pass on rejects: "Are you sure?"

This gives users confidence to be more decisive in the first pass.
```

#### Recycle Bin Behavior
- Items stay in recycle until user reviews them
- No automatic deletion
- User controls when to empty
- Permanent deletion only after Stage 2 review

#### Questions/Clarifications Needed
- [x] What happens in stage 1 (main gallery)? → Like = kept, Dislike = recycle (confirmed)
- [x] What happens in stage 2 (recycle bin)? → Like = restore, Dislike = permadelete (confirmed)
- [x] How long do items stay in recycle bin? → Until user reviews them, no auto-delete (confirmed)

---

### 2.5 Duplicate Detection

**Status:** Planned  
**Priority:** P2 (Nice to Have - Later Phase)

#### Description
Detects and handles duplicate media files. Two approaches:
1. **Hash-based**: SHA256 hash comparison (exact duplicates)
2. **Perceptual**: Image similarity comparison (future)

#### Configuration Options
| Option | Type | Default | Description |
|--------|------|---------|-------------|
| duplicate_detection | bool | false | Enable duplicate detection |
| duplicate_action | string | "mark" | "mark", "delete", "skip" |
| hash_algorithm | string | "sha256" | Hash algorithm to use |

#### Questions/Clarifications Needed
- [x] Hash-based or perceptual? → Start with hash-based (confirmed)
- [x] Auto-delete or mark for review? → Mark for review, user decides (confirmed)

---

## 3. Architecture Decisions

### 3.1 Technology Stack

| Layer | Technology | Rationale |
|-------|------------|-----------|
| UI Framework | Blazor Server | Local app, web UI, runs as regular app |
| Language | C# / .NET 8 | Primary language, owner is C# developer |
| Database | SQLite | Local, lightweight, sufficient for single user |
| ORM | Entity Framework Core | Standard .NET approach |
| HTTP Client | HttpClient | API calls, archive scraping |
| Real-time | SignalR (built into Blazor) | Live updates in UI |
| Deployment | Self-contained executable | No IIS, no Docker, dotnet run or double-click |

### 3.2 Project Structure

```
4Chan-Grabber/
├── Grabbers/                    # Grabber implementations
│   ├── IGrabber.cs             # Interface all grabbers implement
│   ├── FourChan/
│   │   ├── FourChanGrabber.cs  # 4Chan API implementation
│   │   └── FourChanModels.cs   # API response models
│   └── Archive/
│       └── ArchiveGrabber.cs   # Archive site implementation
├── Data/                       # Data layer
│   ├── MediaDbContext.cs        # EF Core context
│   └── Models/                 # Database entities
│       ├── MediaFile.cs
│       ├── Thread.cs
│       ├── Board.cs
│       └── DownloadLog.cs
├── Services/                   # Business logic
│   ├── DownloadOrchestrator.cs # Coordinates grabbers
│   ├── GalleryService.cs       # Gallery operations
│   └── DuplicateService.cs      # Duplicate detection
├── Pages/                      # Blazor pages
│   ├── Index.razor             # Dashboard
│   ├── Gallery/
│   │   ├── Rate.razor         # Tinder-style rating
│   │   └── Browse.razor       # Grid view browsing
│   ├── RecycleBin.razor       # Stage 2 rating
│   └── Settings.razor         # Configuration
├── Components/                 # Reusable Blazor components
│   ├── MediaCard.razor
│   ├── GalleryGrid.razor
│   └── RatingControls.razor
├── wwwroot/                    # Static assets
│   ├── css/
│   └── images/
├── config.json                  # User configuration
└── Program.cs                  # Entry point
```

### 3.3 Data Storage

| Data Type | Storage Location | Format |
|-----------|-----------------|--------|
| Database | `./data/4chan.db` | SQLite |
| Downloaded Media | `./media/downloads/` | Original files organized by board |
| Likes | `./media/liked/` | Organized by date imported |
| Recycle Bin | `./media/recycle/` | Temporary storage |
| Logs | `./logs/` | Text files |
| Config | `./config.json` | JSON |

### 3.4 Configuration

- **config.json** - User-editable configuration file
- **Settings UI** - Blazor page for editing configuration
- **No environment variables for sensitive data** - Single user, local app
- **Schema migrations** - EF Core migrations for schema versioning

---

## 4. UI/UX Design

### 4.1 Design Inspiration

**Visual Style:** Picasa-like (old Google photo manager)
- Clean, simple, efficient
- Focus on the media, not the chrome
- Hydrus Network as functional inspiration (tag-based browsing)

**Color Scheme:** Dark mode default
- Reduces eye strain for long browsing sessions
- Media colors pop against dark background
- Future: Light mode option

### 4.2 Application Layout

```
┌─────────────────────────────────────────────────────────────┐
│  [Logo] 4Chan Grabber              [▶ Run] [⏸ Pause] [⚙]   │
├────────────────┬────────────────────────────────────────────┤
│                │                                            │
│  📥 Downloads  │    Main Content Area                      │
│  🖼 Gallery     │                                            │
│     ↪ Rate      │    [Changes based on selected view]       │
│     ↪ Browse    │                                            │
│  ♻ Recycle Bin  │                                            │
│  📊 Stats       │                                            │
│                │                                            │
├────────────────┴────────────────────────────────────────────┤
│  Status: ● Running | Media: 1,234 | Likes: 567 | Disk: 45GB │
└─────────────────────────────────────────────────────────────┘
```

### 4.3 Gallery View - Rate Mode (Tinder-style)

```
┌─────────────────────────────────────────────────────────────┐
│                    ┌─────────────────┐                     │
│                    │                 │                     │
│                    │                 │                     │
│                    │   [IMAGE]       │                     │
│                    │                 │                     │
│                    │                 │                     │
│                    └─────────────────┘                     │
│                                                             │
│  /b/ - Thread #12345678                                     │
│  "looking for this specific thing"                          │
│                                                             │
│           ← DISLIKE        LIKE →                           │
│                                                             │
│  [Space] Skip   [←/J] Dislike   [→/L] Like   [F] Fullscreen│
└─────────────────────────────────────────────────────────────┘
```

### 4.4 Gallery View - Browse Mode (Grid)

```
┌─────────────────────────────────────────────────────────────┐
│ Filter: [All ▼] [Board: /b/ ▼] [Date: Today ▼]  [⭐ Liked] │
├─────────────────────────────────────────────────────────────┤
│ ┌─────┐ ┌─────┐ ┌─────┐ ┌─────┐ ┌─────┐ ┌─────┐ ┌─────┐   │
│ │     │ │     │ │     │ │     │ │     │ │     │ │     │   │
│ │ 📷  │ │ 📷  │ │ 📷  │ │ 📷  │ │ 📷  │ │ 📷  │ │ 📷  │   │
│ │     │ │     │ │     │ │     │ │     │ │     │ │     │   │
│ └─────┘ └─────┘ └─────┘ └─────┘ └─────┘ └─────┘ └─────┘   │
│                                                             │
│ ┌─────┐ ┌─────┐ ┌─────┐ ┌─────┐ ┌─────┐ ┌─────┐ ┌─────┐   │
│ │     │ │     │ │     │ │     │ │     │ │     │ │     │   │
│ │ 📷  │ │ 📷  │ │ 📷  │ │ 📷  │ │ 📷  │ │ 📷  │ │ 📷  │   │
│ │     │ │     │ │     │ │     │ │     │ │     │ │     │   │
│ └─────┘ └─────┘ └─────┘ └─────┘ └─────┘ └─────┘ └─────┘   │
│                                                             │
│  Showing 1-50 of 1,234                    [Load More]       │
└─────────────────────────────────────────────────────────────┘
```

### 4.5 Keyboard Controls

| Key | Action | Context |
|-----|--------|---------|
| ← Arrow | Dislike / Move left | Gallery - Rate |
| → Arrow | Like / Move right | Gallery - Rate |
| J | Dislike | Gallery - Rate (vim-style) |
| L | Like | Gallery - Rate (vim-style) |
| Space | Skip (no decision) | Gallery - Rate |
| F | Fullscreen view | Gallery - Any |
| Esc | Exit fullscreen / Close modal | Any |
| R | Refresh | Gallery - Browse |
| N | Next page | Gallery - Browse |

### 4.6 Questions/Clarifications - Already Resolved
- [x] Color scheme preference? → Dark mode default (confirmed)
- [x] Visual style? → Picasa-inspired clean UI (confirmed)
- [x] Tinder-style gallery? → Yes, confirmed by user
- [x] Grid view when NOT rating? → Yes, browse mode available

---

## 5. Data Models

### 5.1 Database Schema

```
┌─────────────────┐     ┌─────────────────┐
│     Board       │     │     Thread      │
├─────────────────┤     ├─────────────────┤
│ Id (PK, int)    │────<│ Id (PK, int)    │
│ Name            │     │ BoardId (FK)   │
│ IsActive        │     │ ExternalId      │
│ LastChecked     │     │ Title           │
│ CreatedAt       │     │ Url             │
└─────────────────┘     │ Is404           │
                        │ LastChecked     │
                        │ CreatedAt       │
                        │ ArchivedAt      │
                        └────────┬────────┘
                                 │
                                 │
                        ┌────────┴────────┐
                        │   MediaFile    │
                        ├─────────────────┤
                        │ Id (PK, int)   │
                        │ ThreadId (FK)  │
                        │ FileName       │
                        │ FilePath       │
                        │ FileSize       │
                        │ FileHash       │
                        │ MediaType      │
                        │ Width          │
                        │ Height         │
                        │ IsLiked        │
                        │ IsRecycled     │
                        │ IsReviewed     │
                        │ DownloadedAt   │
                        │ RatingAt       │
                        │ GrabberSource  │
                        └─────────────────┘

┌─────────────────┐
│  DownloadLog   │
├─────────────────┤
│ Id (PK, int)   │
│ GrabberType    │
│ BoardId (FK)   │
│ ThreadId (FK)  │
│ StartedAt      │
│ CompletedAt    │
│ Status         │
│ MediaCount     │
│ Errors         │
└─────────────────┘
```

### 5.2 Rating Implementation

Rating is stored directly on `MediaFile` as boolean fields:
- `IsLiked` - True if user liked (moved to liked folder)
- `IsRecycled` - True if user disliked (in recycle bin)
- `IsReviewed` - True if user has made a rating decision

**No separate Rating table needed** - Simple boolean fields suffice for two-stage rating.

### 5.3 Grabber Tracking

`GrabberSource` field on MediaFile tracks which grabber downloaded it:
- "FourChanApi" - Downloaded via 4Chan API
- "Archive" - Downloaded via archive site
- This helps with debugging and understanding download provenance

### 5.4 Schema Migrations

- Use EF Core migrations for version tracking
- migrations stored in `Data/Migrations/`
- Version history maintained in source control

---

## 6. Grabber Interface

### 6.1 IGrabber Interface

```csharp
public interface IGrabber
{
    string Name { get; }
    string Version { get; }
    GrabberType Type { get; }
    
    Task<bool> TestConnectionAsync();
    IAsyncEnumerable<ThreadInfo> GetThreadsAsync(Board board, CancellationToken ct);
    Task<MediaInfo?> GetMediaAsync(ThreadInfo thread, CancellationToken ct);
}

public enum GrabberType
{
    Api,
    Archive
}
```

### 6.2 Grabber Implementation Strategy

**Compile-time, not runtime plugins:**
- Simpler to build and maintain
- No security concerns with runtime loading
- Can expand later to plugin system if needed
- All grabbers in same solution, built together

### 6.3 Future Grabber Support

The modular design allows future grabbers:
```
Grabbers/
├── IGrabber.cs
├── FourChan/
│   └── FourChanApiGrabber.cs
├── Archive/
│   └── ArchiveGrabber.cs
└── Future/
    └── SomeOtherGrabber.cs  # Easy to add
```

---

## 7. Milestones & Phases

### Phase 1: Foundation
**Goal:** Basic 4Chan API downloader with simple gallery

- [ ] Project setup (Blazor Server, EF Core, SQLite)
- [ ] Database models and migrations
- [ ] 4Chan API grabber implementation
- [ ] Basic gallery view (grid mode)
- [ ] Download to local storage
- [ ] Track downloaded media (avoid duplicates)

### Phase 2: Archive Integration
**Goal:** Archive site downloader + gap detection + recovery

- [ ] Archive grabber implementation
- [ ] Timeframe tracking for archive
- [ ] Gap detection (when computer was off)
- [ ] 404 thread recovery
- [ ] Backward fill logic

### Phase 3: Gallery & Rating
**Goal:** Full gallery experience with two-stage rating

- [ ] Tinder-style rating mode
- [ ] Keyboard controls (L/J/Arrows/Space/F)
- [ ] Two-stage rating (main gallery → recycle bin)
- [ ] Recycle bin review interface
- [ ] Move files to liked/recycle folders

### Phase 4: Polish & Organization
**Goal:** Better organization and deduplication

- [ ] Duplicate detection (hash-based)
- [ ] Advanced filtering in browse mode
- [ ] Statistics dashboard
- [ ] Configurable settings UI

### Future (Out of Scope for now)
- [ ] Tag-based navigation
- [ ] Perceptual duplicate detection
- [ ] Plugin system for grabbers
- [ ] Hydrus network sync
- [ ] Cloud backup

---

## Appendix A: Glossary

| Term | Definition |
|------|------------|
| 4Chan API | Official JSON API provided by 4Chan (https://github.com/4chan/4chan-API) |
| Archive site | Third-party site that archives 4Chan threads (user-specified) |
| Gap detection | Finding missing time periods when computer was off |
| 404 recovery | Finding media from archive for threads that no longer exist on 4Chan |
| Tinder-style | Swipe/press-based rating interface (left=dislike, right=like) |
| Picasa | Old Google photo manager - visual design inspiration |
| Hydrus Network | Python-based media manager - functional inspiration |
| FlareSolverr | Proxy to bypass Cloudflare (may be needed for archive) |

---

## Appendix B: Reference Materials

- [4Chan API Documentation](https://github.com/4chan/4chan-API)
- [Hydrus Network](https://hydrusnetwork.github.io/hydrus/) - Inspiration for gallery/organization
- [Blazor Server Documentation](https://docs.microsoft.com/en-us/aspnet/core/blazor/)
- [EF Core + SQLite](https://docs.microsoft.com/en-us/ef/core/)

---

## Appendix C: User Requirements Summary

From conversation with user:

1. **Download 4Chan media** - via API, keyword filtering, multiple boards
2. **Only media files** - images and videos, no text
3. **Track downloads** - don't download twice
4. **Archive site support** - for /b/ and gap recovery
5. **Gap detection** - when computer is off, knows what to check
6. **Tinder-style gallery** - like/dislike with keyboard
7. **Two-stage rating** - main gallery + recycle bin review
8. **Picasa-like visuals** - clean, simple UI
9. **Self-contained app** - no Docker, IIS, runs as dotnet app
10. **Manual start/stop** - not a background service

---

*Document updated: 2026-04-13 - Filled from user requirements conversation*
