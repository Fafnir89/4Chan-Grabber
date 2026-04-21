using FourChanGrabber.Data.Models;
using FourChanGrabber.Data.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace FourChanGrabber.Data;

public class DatabaseSeeder
{
    private readonly MediaDbContext _context;

    public DatabaseSeeder(MediaDbContext context)
    {
        _context = context;
    }

    public async Task SeedAsync()
    {
        await EnsureImageSourcesAsync();
        await AddTestDownloadAsync();
    }

    private async Task EnsureImageSourcesAsync()
    {
        var fourChanExists = await _context.ImageSources.AnyAsync(s => s.Name == "4Chan");
        if (!fourChanExists)
        {
            var fourChan = new ImageSource
            {
                Name = "4Chan"
            };

            _context.ImageSources.Add(fourChan);
        }

        await _context.SaveChangesAsync();
    }

    private async Task AddTestDownloadAsync()
    {
        var fourChan = await _context.ImageSources.FirstAsync(s => s.Name == "4Chan");
        var download = new DownloadQueue
        {
            DownloadUrl = "https://thumb-cdn77.xvideos-cdn.com/f2f39725-0f0a-44a4-abd0-082bd350b5d8/0/xv_14_p.jpg",
            TargetPath = @"C:\Users\fafni\source\repos\4Chan Grabber\Downloads\test.jpg",
            ImageSourceId = fourChan.Id,
            Status = DownloadStatus.New,
            RequestTime = DateTime.Now
        };

        _context.DownloadQueue.Add(download);

        download = new DownloadQueue
        {
            DownloadUrl = "https://thumb-cdn77.xvideos-cdn.com/f2f39725-0f0a-44a4-abd0-082bd350b5d8/0/xv_14_p.jpg",
            TargetPath = @"C:\Users\fafni\source\repos\4Chan Grabber\Downloads\test.jpg",
            ImageSourceId = fourChan.Id,
            Status = DownloadStatus.New,
            RequestTime = DateTime.Now - new TimeSpan(0,0,1)
        };

        _context.DownloadQueue.Add(download);

        download = new DownloadQueue
        {
            DownloadUrl = "https://thumb-cdn77.xvideos-cdn.com/f2f39725-0f0a-44a4-abd0-082bd350b5d8/0/xv.jpg",
            TargetPath = @"C:\Users\fafni\source\repos\4Chan Grabber\Downloads\test.jpg",
            ImageSourceId = fourChan.Id,
            Status = DownloadStatus.New,
            RequestTime = DateTime.Now - new TimeSpan(0,0,2)
        };

        _context.DownloadQueue.Add(download);

        download = new DownloadQueue
        {
            DownloadUrl = "https://thumb-cdn77.xvideos-cdn.com/f2f39725-0f0a-44a4-abd0-082bd350b5d8/0/xv_14_p.jpg",
            TargetPath = @"C:\Users\fafni\source\repos\4Chan Grabber\Downloads\test.jpg",
            ImageSourceId = fourChan.Id,
            Status = DownloadStatus.New,
            RequestTime = DateTime.Now - new TimeSpan(0,0,3)
        };

        _context.DownloadQueue.Add(download);

        download = new DownloadQueue
        {
            DownloadUrl = "https://thumb-cdn77.xvideos-cdn.com/f2f39725-0f0a-44a4-abd0-082bd350b5d8/0/xv_14_p.jpg",
            TargetPath = @"C:\Users\fafni\source\repos\4Chan Grabber\Downloads\test.jpg",
            ImageSourceId = fourChan.Id,
            Status = DownloadStatus.New,
            RequestTime = DateTime.Now - new TimeSpan(0,0,4)
        };

        _context.DownloadQueue.Add(download);

        await _context.SaveChangesAsync();
    }
}
