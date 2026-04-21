using System.Collections.Concurrent;

namespace FourChanGrabber.Services;

public class HttpClientFactory
{
    private readonly ConcurrentDictionary<string, List<TrackedClient>> _clients = new();

    public HttpClient GetClient(string moduleId, int timeoutSeconds)
    {
        var key = $"{moduleId}_{timeoutSeconds}s";

        var trackedClients = _clients.GetOrAdd(key, _ => new List<TrackedClient>());

        lock (trackedClients)
        {
            var existing = trackedClients.FirstOrDefault(c => c.TimeoutSeconds == timeoutSeconds);
            if (existing != null)
            {
                existing.LastUsed = DateTime.UtcNow;
                return existing.Client;
            }

            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
            trackedClients.Add(new TrackedClient(client, timeoutSeconds));
            return client;
        }
    }

    public void DisposeModule(string moduleId)
    {
        var keysToRemove = _clients.Keys.Where(k => k.StartsWith($"{moduleId}_")).ToList();

        foreach (var key in keysToRemove)
        {
            if (_clients.TryRemove(key, out var trackedClients))
            {
                lock (trackedClients)
                {
                    foreach (var tracked in trackedClients)
                    {
                        tracked.Client.Dispose();
                    }
                    trackedClients.Clear();
                }
            }
        }
    }

    public void Dispose()
    {
        foreach (var kvp in _clients)
        {
            foreach (var tracked in kvp.Value)
            {
                tracked.Client.Dispose();
            }
            kvp.Value.Clear();
        }
        _clients.Clear();
    }

    private class TrackedClient
    {
        public HttpClient Client { get; }
        public int TimeoutSeconds { get; }
        public DateTime LastUsed { get; set; }

        public TrackedClient(HttpClient client, int timeoutSeconds)
        {
            Client = client;
            TimeoutSeconds = timeoutSeconds;
            LastUsed = DateTime.UtcNow;
        }
    }
}
