# HttpClientFactory

Manages reusable `HttpClient` instances per module with thread-safe access.

## Overview

Instead of creating new `HttpClient` instances per request (which can cause socket exhaustion), this factory creates and caches clients per module identity and timeout. Clients are reused across multiple requests.

## Usage

### Getting a Client

```csharp
var factory = serviceProvider.GetRequiredService<HttpClientFactory>();

// Get a client for "DownloadManager" with 30 second timeout
var client = factory.GetClient("DownloadManager", 30);
```

### Same Module + Same Timeout = Same Client

```csharp
// First call - creates new client
var client1 = factory.GetClient("DownloadManager", 30);

// Second call with same params - returns existing client
var client2 = factory.GetClient("DownloadManager", 30);

// client1 == client2 (same instance)
```

### Different Timeout = Different Client

```csharp
var fastClient = factory.GetClient("ThreadWatcher", 10);  // 10s timeout
var slowClient = factory.GetClient("ArchiveService", 120); // 120s timeout
```

## Module Cleanup

When a module shuts down, it should notify the factory:

```csharp
factory.DisposeModule("DownloadManager");
```

This disposes all `HttpClient` instances created for that module.

## Key Design

- **Thread-safe**: Uses `ConcurrentDictionary` for safe multi-thread access
- **Composite key**: `"ModuleId_timeoutInSeconds"` (e.g., `"DownloadManager_30s"`)
- **No sliding expiration**: Callers request client each time; factory holds reference until module disposal
- **Singleton lifetime**: One factory instance per application

## Integration

Register in `Program.cs`:

```csharp
builder.Services.AddSingleton<HttpClientFactory>();
```

Inject via constructor:

```csharp
public class MyService
{
    private readonly HttpClientFactory _clientFactory;

    public MyService(HttpClientFactory clientFactory)
    {
        _clientFactory = clientFactory;
    }
}
```
