using FourChanGrabber.Data.Models.Enums;

namespace FourChanGrabber.Data.Models;

public class DownloadQueue
{
    public int Id { get; set; }
    public string DownloadUrl { get; set; } = string.Empty;
    public string TargetPath { get; set; } = string.Empty;
    public DownloadStatus Status { get; set; } = DownloadStatus.Pending;
    public string? ErrorMessage { get; set; }
    public int ImageSourceId { get; set; }
    public ImageSource ImageSource { get; set; } = null!;
}
