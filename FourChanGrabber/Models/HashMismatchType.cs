namespace FourChanGrabber.Models;

/// <summary>
/// Indicates the type of hash mismatch when a downloaded file's hash doesn't match the expected hash.
/// </summary>
public enum HashMismatchType
{
    /// <summary>
    /// No hash mismatch occurred.
    /// </summary>
    None,

    /// <summary>
    /// Hash mismatch due to a retryable network error. The download should be retried.
    /// </summary>
    Retryable,

    /// <summary>
    /// Hash mismatch indicates file corruption. The download should not be retried.
    /// </summary>
    Corrupt
}
