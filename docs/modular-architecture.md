# 4Chan Grabber - Modular Architecture

> **Status:** Draft - In Progress  
> **Last Updated:** 2026-04-13

---

## Overview

This document describes the modular architecture of 4Chan Grabber. Each module is designed to be independent, testable, and replaceable.

**Design Philosophy:**
- Modules communicate via **Internal API** (for control/UI) and **Database** (for actual work)
- Each module can be developed and tested separately
- Separation allows future extraction to microservices if needed

---

## Module Map

```
┌─────────────────────────────────────────────────────────────────────┐
│                           UI (Blazor Server)                        │
│                         localhost:5000                              │
└─────────────────────────────┬───────────────────────────────────────┘
                              │ HTTP API
┌─────────────────────────────▼───────────────────────────────────────┐
│                       API Gateway                                   │
│                    Internal HTTP API                                │
│                 (controls all modules)                              │
└────────────────┬─────────────────────────────┬─────────────────────┘
                 │                             │
        ┌────────▼────────┐           ┌────────▼────────┐
        │  DownloadQueue  │           │   GrabberHub    │
        │   (Database)    │           │   (Database)    │
        └────────┬────────┘           └────────┬────────┘
                 │                             │
        ┌────────▼────────┐           ┌────────▼────────┐
        │ DownloadManager │           │  Grabbers       │
        │ (Background Svc)│           │ (4Chan, Archive)│
        └─────────────────┘           └─────────────────┘
```

---

## Modules

### 1. [DownloadQueue](./modules/download-queue.md) ⭐ Next Discussion
**Status:** To be discussed  
**Purpose:** Database-backed job queue for media downloads

### 2. DownloadManager
**Status:** Planned  
**Purpose:** Background worker that processes DownloadQueue

### 3. GrabberHub
**Status:** Planned  
**Purpose:** Central registry for grabbers, coordinates what gets downloaded

### 4. FourChanGrabber
**Status:** Planned  
**Purpose:** 4Chan API implementation

### 5. ArchiveGrabber
**Status:** Planned  
**Purpose:** Archive site implementation

### 6. GalleryService
**Status:** Planned  
**Purpose:** Gallery browsing and rating operations

---

## Communication Patterns

### UI → Modules: HTTP API
All UI interactions go through the internal API:
```
UI ──HTTP──> API Gateway ──HTTP──> Module Controllers
```

### Grabbers → DownloadQueue: Database
Grabbers write download tasks directly to database:
```
Grabber ──Writes──> download_queue table ──Polls──> DownloadManager
```

### Rationale
- **API for control**: Start/stop, get status, configure (human-initiated actions)
- **DB for work**: Download tasks are machine-generated in bulk, polling is efficient

---

## Internal API Endpoints

### DownloadQueue API
```
GET    /api/queue              # List queued items
GET    /api/queue/{id}         # Get specific item
POST   /api/queue              # Add item(s) to queue
DELETE /api/queue/{id}         # Remove item from queue
POST   /api/queue/pause        # Pause downloader
POST   /api/queue/resume       # Resume downloader
GET    /api/queue/status       # Get worker status
```

### GrabberHub API
```
GET    /api/grabbers           # List registered grabbers
GET    /api/grabbers/{name}    # Get grabber status
POST   /api/grabbers/{name}/start   # Start grabber
POST   /api/grabbers/{name}/stop    # Stop grabber
GET    /api/grabbers/{name}/threads # List tracked threads
```

### Gallery API
```
GET    /api/media              # List media (with filters)
GET    /api/media/{id}         # Get media details
POST   /api/media/{id}/rate    # Rate media (like/dislike)
DELETE /api/media/{id}         # Delete media
GET    /api/media/recycle      # List recycle bin
POST   /api/media/recycle/empty # Empty recycle bin
```

---

## Next Module to Discuss

👉 **[DownloadQueue](./modules/download-queue.md)** - Database-backed job queue for media downloads

---
