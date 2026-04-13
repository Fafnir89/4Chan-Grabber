# Module: DownloadManager

> **Status:** Planned  
> **Module Type:** Core Infrastructure  
> **Depends On:** DownloadQueue  

---

## Purpose

Background service that processes the DownloadQueue. Polls for pending items, downloads files, updates queue status.

**What it is:**
- `BackgroundService` in Blazor Server
- Polls `download_queue` table
- Downloads files from URLs to local storage
- Updates queue status on success/failure

**What it is NOT:**
- A separate process (runs in same app)
- Responsible for deciding what to download (that's GrabberHub's job)

---

## Related Documents

- [DownloadQueue](./download-queue.md) - Queue schema and behavior
- [Modular Architecture Overview](../modular-architecture.md) - System-level view

---

*To be detailed after DownloadQueue is finalized.*
