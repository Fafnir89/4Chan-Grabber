# Documentation Index

## Services

| Document | Description |
|----------|-------------|
| [HttpClientFactory](HttpClientFactory.md) | Per-module HttpClient management and reuse |

## Download Manager

| Document | Description |
|----------|-------------|
| [DownloadManager](DownloadManager.md) | Retry/throttle system, configuration, and usage |
| [DownloadManager-TODO](DownloadManager-TODO.md) | Future improvements and known issues |

## Architecture Overview

```
┌─────────────────────────────────────────────────────────┐
│                    Program.cs                            │
│  - Service Registration (Singleton/Scoped)              │
│  - Database Migrations & Seeding                       │
└─────────────────────────────────────────────────────────┘
                           │
         ┌─────────────────┼─────────────────┐
         ▼                 ▼                 ▼
┌─────────────────┐ ┌───────────────┐ ┌──────────────┐
│ HttpClientFactory│ │ DownloadManager│ │ Other Services│
│                 │ │               │ │              │
│ GetClient()     │ │ Background    │ │              │
│ DisposeModule() │ │ Service       │ │              │
└─────────────────┘ │               │ └──────────────┘
                    │ - Retry logic │
                    │ - Throttle    │
                    │ - Pause/Unpause│
                    └───────────────┘
                           │
                           ▼
                    ┌───────────────┐
                    │  MediaDbContext│
                    │  (SQLite)     │
                    └───────────────┘
```

## Key Concepts

### HttpClient Reuse
Services request HttpClients from the factory instead of creating new ones. This prevents socket exhaustion while maintaining connection pooling.

### FIFO with Retry Priority
Downloads are processed oldest-first by `RequestTime`. When a download fails and is retried, its `RequestTime` is updated to now, moving it toward the front of the queue without skipping ahead of all waiting items.

### Adaptive Throttling
On 429 errors, concurrency drops to 1 and gradually recovers over time. This prevents hammering a rate-limited server while still completing other work.
