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

## 1. Project Overview

### 1.1 Project Name
**4Chan Grabber** (working title - subject to change?)

### 1.2 Project Type
Personal media management and download tool

### 1.3 Core Value Proposition
*A short description of what this tool does and why someone would use it*

### 1.4 Target User
- Primary: Fafnir89 (owner)
- Single-user, local machine deployment
- No cloud/server deployment planned

### 1.5 Core Features Summary

| Feature | Description |
|---------|-------------|
| 4Chan API Downloader | Download media from 4Chan via their official API |
| Archive Downloader | Download media from archive sites |
| Gallery | Browse and rate downloaded media |
| Tag/Duplicate Management | Organize and filter large collections |

### 1.6 Out of Scope (v1)
- [ ] Cloud deployment
- [ ] Multi-user support
- [ ] Hydrus network sync
- [ ] Other grabbers beyond 4Chan

---

## 2. Feature Specifications

*This section will be filled out feature by feature in detail.*

### 2.1 4Chan API Downloader

**Status:** Planned  
**Priority:** P0 (Must Have - v1)

#### Description
*[What it does - to be detailed]*

#### User Flow
*[How the user interacts with this feature]*

#### Configuration Options
| Option | Type | Default | Description |
|--------|------|---------|-------------|
| boards | list | [] | Which boards to monitor |
| check_interval | seconds | 60 | How often to check for new threads |
| keyword_filters | list | [] | Filter threads by keywords |

#### Questions/Clarifications Needed
- [ ] What boards specifically?
- [ ] Keyword filter logic (AND/OR/exclude)?
- [ ] What happens to threads that 404?

---

### 2.2 Archive Downloader

**Status:** Planned  
**Priority:** P0 (Must Have - v1)

#### Description
*[What it does - to be detailed]*

#### User Flow
*[How the user interacts with this feature]*

#### Configuration Options
| Option | Type | Default | Description |
|--------|------|---------|-------------|
| archive_url | string | "" | Archive site URL |
| check_interval | seconds | 300 | How often to check archive |

#### Questions/Clarifications Needed
- [ ] Which archive site specifically?
- [ ] What filters does the archive support?

---

### 2.3 Gallery

**Status:** Planned  
**Priority:** P0 (Must Have - v1)

#### Description
*[What it does - to be detailed]*

#### User Flow
*[How the user interacts with this feature]*

#### UI Layout
*[Describe the gallery layout]*

#### Questions/Clarifications Needed
- [ ] Keyboard controls details?
- [ ] Sorting options?
- [ ] Folder vs tag-based navigation?

---

### 2.4 Two-Stage Rating System

**Status:** Planned  
**Priority:** P1 (Should Have)

#### Description
*[What it does - to be detailed]*

#### User Flow
*[How the user interacts with this feature]*

#### Questions/Clarifications Needed
- [ ] What happens in stage 1 (main gallery)?
- [ ] What happens in stage 2 (recycle bin)?
- [ ] How long do items stay in recycle bin?

---

### 2.5 Duplicate Detection

**Status:** Planned  
**Priority:** P2 (Nice to Have)

#### Description
*[What it does - to be detailed]*

#### Questions/Clarifications Needed
- [ ] Hash-based or perceptual?
- [ ] Auto-delete or mark for review?

---

## 3. Architecture Decisions

### 3.1 Technology Stack

| Layer | Technology | Rationale |
|-------|------------|-----------|
| UI Framework | Blazor Server | Local app, web UI, no WASM complexity |
| Language | C# / .NET 8 | Primary language |
| Database | SQLite | Local, lightweight, sufficient |
| ORM | Entity Framework Core | Standard .NET approach |
| HTTP Client | HttpClient | API calls, archive scraping |
| Real-time | SignalR (built into Blazor) | Live updates in UI |

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
│       └── Rating.cs
├── Services/                   # Business logic
│   ├── DownloadService.cs      # Orchestrates grabbing
│   ├── GalleryService.cs       # Gallery operations
│   └── DuplicateService.cs     # Duplicate detection
├── Pages/                      # Blazor pages
│   ├── Index.razor             # Home/dashboard
│   ├── Gallery.razor           # Main gallery view
│   ├── Settings.razor          # Configuration
│   └── Downloads.razor         # Download status
├── Components/                 # Reusable Blazor components
│   ├── MediaCard.razor
│   ├── GalleryGrid.razor
│   └── RatingControls.razor
├── wwwroot/                    # Static assets
│   ├── css/
│   └── images/
└── Program.cs                  # Entry point
```

### 3.3 Data Storage

| Data Type | Storage Location | Format |
|-----------|-----------------|--------|
| Database | `./data/4chan.db` | SQLite |
| Downloaded Media | `./media/` | Original files |
| Likes | `./media/liked/` | Organized by date/rating |
| Dislikes/Recycle | `./media/recycle/` | Temporary storage |
| Logs | `./logs/` | Text files |

### 3.4 Configuration

- [ ] Configuration stored in `appsettings.json` or `config.json`?
- [ ] Environment variables for sensitive data?
- [ ] UI for editing configuration?

---

## 4. UI/UX Design

### 4.1 Application Layout

```
┌─────────────────────────────────────────────────────────────┐
│  [Logo] 4Chan Grabber          [Status] [Settings] [Help]  │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│  ┌─────────┐                                               │
│  │ Downloads│  ← Navigation sidebar?                       │
│  │ Gallery  │                                              │
│  │ Liked    │     Main content area                        │
│  │ Recycle  │                                              │
│  │          │                                               │
│  └─────────┘                                               │
│                                                             │
├─────────────────────────────────────────────────────────────┤
│  Status: Running | Media: 1,234 | Likes: 567 | Disk: 45GB │
└─────────────────────────────────────────────────────────────┘
```

### 4.2 Gallery View (Tinder-style)

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
│  [Space] Next   [L] Like   [J] Dislike   [F] Fullscreen   │
└─────────────────────────────────────────────────────────────┘
```

### 4.3 Keyboard Controls

| Key | Action |
|-----|--------|
| ← / J | Dislike / Move to recycle |
| → / L | Like / Move to liked |
| Space | Skip (no decision) |
| F | Fullscreen view |
| Esc | Exit fullscreen / Close modal |
| R | Refresh gallery |

### 4.4 Questions/Clarifications Needed
- [ ] Color scheme preference?
- [ ] Dark mode / light mode / both?
- [ ] Single image view or grid view as default?
- [ ] How does the user browse when NOT in rating mode?

---

## 5. Data Models

### 5.1 Database Schema (Proposed)

```
┌─────────────────┐     ┌─────────────────┐
│     Board       │     │     Thread      │
├─────────────────┤     ├─────────────────┤
│ Id (PK)         │────<│ Id (PK)         │
│ Name            │     │ BoardId (FK)    │
│ LastChecked     │     │ ExternalId      │
│ IsActive        │     │ Title           │
└─────────────────┘     │ Url             │
                        │ Is404           │
                        │ LastChecked     │
                        └────────┬────────┘
                                 │
                                 │
                        ┌────────┴────────┐
                        │    MediaFile    │
                        ├─────────────────┤
                        │ Id (PK)         │
                        │ ThreadId (FK)   │
                        │ FileName        │
                        │ FilePath        │
                        │ FileSize        │
                        │ FileHash        │
                        │ MediaType       │
                        │ IsLiked         │
                        │ IsRecycled      │
                        │ DownloadedAt     │
                        │ Rating          │
                        └─────────────────┘
```

### 5.2 Questions/Clarifications Needed
- [ ] Do we need a separate Rating table or just a field on MediaFile?
- [ ] Track which grabber downloaded each file?
- [ ] Version history if we migrate schemas?

---

## 6. Grabber Interface

### 6.1 IGrabber Interface

```csharp
public interface IGrabber
{
    string Name { get; }
    string Version { get; }
    
    Task<bool> TestConnectionAsync();
    IAsyncEnumerable<ThreadInfo> GetThreadsAsync(Board board, CancellationToken ct);
    IAsyncEnumerable<MediaInfo> GetMediaAsync(ThreadInfo thread, CancellationToken ct);
}
```

### 6.2 Questions/Clarifications Needed
- [ ] Should grabbers be plugins (loaded at runtime) or compile-time?
- [ ] Shared interface for configuration per grabber?
- [ ] How to handle grabber-specific UI settings?

---

## 7. Milestones & Phases

### Phase 1: Foundation
**Goal:** Basic 4Chan API downloader with gallery

- [ ] Project setup (Blazor Server)
- [ ] Database models and EF Core setup
- [ ] 4Chan API grabber implementation
- [ ] Basic gallery view
- [ ] Download to local storage

### Phase 2: Archive Integration
**Goal:** Archive site downloader + recovery logic

- [ ] Archive grabber implementation
- [ ] Gap detection and recovery
- [ ] 404 tracking

### Phase 3: Gallery & Rating
**Goal:** Full gallery experience with rating

- [ ] Grid view gallery
- [ ] Tinder-style rating
- [ ] Two-stage rating system
- [ ] Keyboard controls

### Phase 4: Polish & Organization
**Goal:** Better organization and deduplication

- [ ] Tag-based navigation (like Hydrus)
- [ ] Duplicate detection
- [ ] Advanced search/filtering

### Future Ideas (Out of Scope for now)
- [ ] Hydrus-style server sync
- [ ] Additional grabbers (other imageboards)
- [ ] Plugin system
- [ ] Cloud backup

---

## Appendix A: Glossary

| Term | Definition |
|------|------------|
| 4Chan API | Official JSON API provided by 4Chan |
| Archive site | Third-party site that archives 4Chan threads |
| FlareSolverr | Proxy server to bypass Cloudflare protection |
| Gap detection | Finding missing time periods in archive coverage |

---

## Appendix B: Reference Materials

- [4Chan API Documentation](https://github.com/4chan/4chan-API)
- [Hydrus Network](https://hydrusnetwork.github.io/hydrus/) - Inspiration
- [Blazor Server Documentation](https://docs.microsoft.com/en-us/aspnet/core/blazor/)

---

*Document will be updated as features are detailed out.*
