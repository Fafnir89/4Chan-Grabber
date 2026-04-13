using FourChanGrabber.Models;

namespace FourChanGrabber.Services;

/// <summary>
/// Defines the contract for downloading files from remote sources.
/// </summary>
public interface IDownloadService
{
    /// <summary>
    /// Downloads a file from the specified source URL to the target path.
    /// </summary>
    /// <param name="sourceUrl">The URL to download from.</param>
    /// <param name="targetPath">The local path where the file should be saved.</param>
    /// <param name="expectedHash">The expected SHA256 hash of the file (optional).</param>
    /// <param name="config">The source configuration containing download settings.</param>
    /// <param name="progress">Optional progress reporter (0.0 to 1.0).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A DownloadResult indicating success or failure with details.</returns>
    Task<DownloadResult> DownloadFileAsync(
        string sourceUrl,
        string targetPath,
        string? expectedHash,
        SourceConfig config,
        IProgress<double>? progress = null,
        CancellationToken ct = default);
}
