namespace FourChanGrabber.Models;

/// <summary>
/// Represents the result of a download operation.
/// </summary>
public class DownloadResult
{
    /// <summary>
    /// Gets or sets whether the download was successful.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Gets or sets the SHA256 hash of the downloaded file.
    /// </summary>
    public string? FileHash { get; set; }

    /// <summary>
    /// Gets or sets the size of the downloaded file in bytes.
    /// </summary>
    public long FileSize { get; set; }

    /// <summary>
    /// Gets or sets the error message if the download failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Gets or sets the type of hash mismatch if the file hash did not match expected.
    /// </summary>
    public HashMismatchType? HashMismatch { get; set; }
}
