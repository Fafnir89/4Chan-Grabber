namespace FourChanGrabber.Models;

public class QueueStats
{
    public int Pending { get; set; }
    public int Downloading { get; set; }
    public int Completed { get; set; }
    public int Failed { get; set; }
    public int Cancelled { get; set; }
}
