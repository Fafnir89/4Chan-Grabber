using FourChanGrabber.Data.Models.Enums;

namespace FourChanGrabber.Data.Models;

public class MediaData
{
    public int Id { get; set; }
    public string SavePath { get; set; } = string.Empty;
    public int ImageSourceId { get; set; }
    public ImageSource ImageSource { get; set; } = null!;
}
