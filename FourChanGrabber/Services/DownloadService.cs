using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FourChanGrabber.Models;
using Microsoft.Extensions.Logging;

namespace FourChanGrabber.Services;

/// <summary>
/// Service for downloading files with support for rate limiting, proxy, and hash verification.
/// </summary>
public class DownloadService : IDownloadService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<DownloadService> _logger;
    private readonly string _tempDirectory;

    // Rate limiting: tracks last request time per source (host)
    private static readonly ConcurrentDictionary<string, DateTime> _lastRequestTimes = new();

    private const string UserAgent = "4Chan-Grabber/1.0";
    private const int FlareSolverrTimeout = 60000;

    public DownloadService(
        IHttpClientFactory httpClientFactory,
        ILogger<DownloadService> logger,
        string tempDirectory)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _tempDirectory = tempDirectory;
    }

    public async Task<DownloadResult> DownloadFileAsync(
        string sourceUrl,
        string targetPath,
        string? expectedHash,
        SourceConfig config,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        // Ensure temp directory exists
        Directory.CreateDirectory(_tempDirectory);

        var tempFilePath = Path.Combine(_tempDirectory, $"{Guid.NewGuid()}.tmp");

        try
        {
            // Apply rate limiting based on source host
            await ApplyRateLimitingAsync(sourceUrl, config.RateLimitPerSecond, ct);

            // Download to temp file
            long fileSize;
            if (!string.IsNullOrEmpty(config.CloudFlareProxyUrl))
            {
                fileSize = await DownloadViaFlareSolverrAsync(sourceUrl, tempFilePath, config, progress, ct);
            }
            else
            {
                fileSize = await DownloadViaHttpAsync(sourceUrl, tempFilePath, config, progress, ct);
            }

            // Compute SHA256 hash
            var fileHash = await ComputeSha256HashAsync(tempFilePath, ct);

            // Verify hash if expected hash provided
            if (!string.IsNullOrEmpty(expectedHash))
            {
                var expectedHashLower = expectedHash.ToLowerInvariant();
                var actualHashLower = fileHash.ToLowerInvariant();

                if (!string.Equals(expectedHashLower, actualHashLower, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning(
                        "Hash mismatch for {Url}: expected {Expected}, got {Actual}",
                        sourceUrl, expectedHashLower, actualHashLower);

                    // Determine if this is retryable or corrupt based on retry config
                    var mismatchType = config.RetryAttempts > 0
                        ? HashMismatchType.Retryable
                        : HashMismatchType.Corrupt;

                    return new DownloadResult
                    {
                        Success = false,
                        FileHash = fileHash,
                        FileSize = fileSize,
                        HashMismatch = mismatchType,
                        ErrorMessage = $"Hash mismatch: expected {expectedHashLower}, got {actualHashLower}"
                    };
                }
            }

            // Move temp file to target path
            var targetDirectory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(targetDirectory))
            {
                Directory.CreateDirectory(targetDirectory);
            }

            // Handle case where target file already exists
            if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }

            File.Move(tempFilePath, targetPath);

            _logger.LogInformation(
                "Successfully downloaded {Url} to {TargetPath} ({FileSize} bytes, hash: {Hash})",
                sourceUrl, targetPath, fileSize, fileHash);

            return new DownloadResult
            {
                Success = true,
                FileHash = fileHash,
                FileSize = fileSize
            };
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Download cancelled for {Url}", sourceUrl);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download {Url}", sourceUrl);

            return new DownloadResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
        finally
        {
            // Clean up temp file if it still exists (download failed)
            if (File.Exists(tempFilePath) && !File.Exists(targetPath))
            {
                try
                {
                    File.Delete(tempFilePath);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }
    }

    private async Task ApplyRateLimitingAsync(string sourceUrl, int rateLimitPerSecond, CancellationToken ct)
    {
        if (rateLimitPerSecond <= 0)
        {
            return;
        }

        var delayMs = 1000 / rateLimitPerSecond;
        var host = GetHostFromUrl(sourceUrl);
        var now = DateTime.UtcNow;

        // Get last request time inside lock
        DateTime lastRequest;
        lock (_lastRequestTimes)
        {
            lastRequest = _lastRequestTimes.TryGetValue(host, out var stored) ? stored : now;
        }

        // Calculate wait time and delay if needed (outside lock)
        var elapsed = now - lastRequest;
        if (elapsed.TotalMilliseconds < delayMs)
        {
            var waitTime = delayMs - (int)elapsed.TotalMilliseconds;
            await Task.Delay(waitTime, ct);
        }

        // Update last request time
        _lastRequestTimes[host] = DateTime.UtcNow;
    }

    private async Task<long> DownloadViaHttpAsync(
        string sourceUrl,
        string tempFilePath,
        SourceConfig config,
        IProgress<double>? progress,
        CancellationToken ct)
    {
        using var client = _httpClientFactory.CreateClient("Download");
        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        client.Timeout = TimeSpan.FromSeconds(config.DownloadTimeoutSeconds);

        using var response = await client.GetAsync(sourceUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1;
        var downloadedBytes = 0L;

        await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
        await using var fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None);

        var buffer = new byte[81920];
        int bytesRead;

        while ((bytesRead = await contentStream.ReadAsync(buffer, ct)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
            downloadedBytes += bytesRead;

            if (totalBytes > 0)
            {
                progress?.Report((double)downloadedBytes / totalBytes);
            }
        }

        return downloadedBytes;
    }

    private async Task<long> DownloadViaFlareSolverrAsync(
        string sourceUrl,
        string tempFilePath,
        SourceConfig config,
        IProgress<double>? progress,
        CancellationToken ct)
    {
        // FlareSolverr request format
        var flareSolverrRequest = new
        {
            cmd = "request.get",
            url = sourceUrl,
            userAgent = UserAgent,
            maxTimeout = FlareSolverrTimeout
        };

        using var client = _httpClientFactory.CreateClient("FlareSolverr");
        client.Timeout = TimeSpan.FromSeconds(config.DownloadTimeoutSeconds + 10);

        var jsonContent = JsonSerializer.Serialize(flareSolverrRequest);
        using var httpContent = new StringContent(jsonContent, Encoding.UTF8, "application/json");

        _logger.LogDebug("Sending request to FlareSolverr at {ProxyUrl}", config.CloudFlareProxyUrl);

        using var response = await client.PostAsync(config.CloudFlareProxyUrl, httpContent, ct);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(ct);
        var flareSolverrResponse = JsonSerializer.Deserialize<FlareSolverrResponse>(responseJson);

        if (flareSolverrResponse?.Solution == null)
        {
            throw new InvalidOperationException("FlareSolverr returned invalid response");
        }

        // Decode base64 response and write to temp file
        var decodedBytes = Convert.FromBase64String(flareSolverrResponse.Solution.Response);

        await File.WriteAllBytesAsync(tempFilePath, decodedBytes, ct);

        progress?.Report(1.0); // FlareSolverr doesn't support partial progress

        _logger.LogDebug("Downloaded {ByteCount} bytes via FlareSolverr from {Url}", decodedBytes.Length, sourceUrl);

        return decodedBytes.Length;
    }

    private static async Task<string> ComputeSha256HashAsync(string filePath, CancellationToken ct)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var hashBytes = await sha256.ComputeHashAsync(stream, ct);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private static string GetHostFromUrl(string url)
    {
        try
        {
            var uri = new Uri(url);
            return uri.Host;
        }
        catch
        {
            return url;
        }
    }

    private class FlareSolverrResponse
    {
        public FlareSolverrSolution? Solution { get; set; }
    }

    private class FlareSolverrSolution
    {
        public string Response { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
    }
}
