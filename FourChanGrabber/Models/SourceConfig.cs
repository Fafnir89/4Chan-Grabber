namespace FourChanGrabber.Models;

public class SourceConfig
{
    public int MaxConcurrentDownloads { get; set; } = 3;
    public int RateLimitPerSecond { get; set; } = 2;
    public string? CloudFlareProxyUrl { get; set; }
    public int RetryAttempts { get; set; } = 2;
    public int DownloadTimeoutSeconds { get; set; } = 120;
}
