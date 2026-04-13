using FourChanGrabber.Data;
using FourChanGrabber.Data.Models;
using FourChanGrabber.Data.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace FourChanGrabber.Tests.TestFixtures;

/// <summary>
/// Provides an in-memory database for testing with helper methods for test data setup.
/// </summary>
public class TestDbContext : IDisposable
{
    public MediaDbContext Context { get; }

    public TestDbContext()
    {
        var options = new DbContextOptionsBuilder<MediaDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        Context = new MediaDbContext(options);
    }

    public void Dispose()
    {
        Context.Dispose();
    }

    /// <summary>
    /// Creates a DownloadQueue item with default test values.
    /// </summary>
    public DownloadQueue CreateDownloadQueue(
        DownloadStatus status = DownloadStatus.Pending,
        int? imageSourceId = null,
        Action<DownloadQueue>? configure = null)
    {
        var sourceId = imageSourceId ?? CreateImageSource().Id;

        var item = new DownloadQueue
        {
            ImageSourceId = sourceId,
            SourceUrl = "https://example.com/image.jpg",
            TargetPath = "/downloads/image.jpg",
            RequestTime = DateTime.UtcNow,
            Status = status,
            Priority = 0,
            Attempts = 0,
            CreatedAt = DateTime.UtcNow
        };

        configure?.Invoke(item);
        Context.DownloadQueue.Add(item);
        Context.SaveChanges();

        return item;
    }

    /// <summary>
    /// Creates an ImageSource with optional config JSON.
    /// </summary>
    public ImageSource CreateImageSource(
        string name = "Test Source",
        string baseUrl = "https://example.com",
        string? configJson = null)
    {
        var source = new ImageSource
        {
            Name = name,
            BaseUrl = baseUrl,
            IsEnabled = true,
            ConfigJson = configJson
        };

        Context.ImageSources.Add(source);
        Context.SaveChanges();

        return source;
    }

    /// <summary>
    /// Creates an ImageSource with a full ImageSourceConfig object.
    /// </summary>
    public ImageSource CreateImageSourceWithConfig(
        string name = "Test Source",
        string baseUrl = "https://example.com",
        Action<ImageSourceConfig>? configure = null)
    {
        var config = new ImageSourceConfig();
        configure?.Invoke(config);

        var source = new ImageSource
        {
            Name = name,
            BaseUrl = baseUrl,
            IsEnabled = true
        };
        ImageSource.SetConfig(source, config);

        Context.ImageSources.Add(source);
        Context.SaveChanges();

        return source;
    }

    /// <summary>
    /// Creates a DownloadQueue_ChanBoard linked to a DownloadQueue item.
    /// </summary>
    public DownloadQueue_ChanBoard CreateChanBoard(
        int downloadQueueId,
        string board = "test",
        string threadUrl = "https://example.com/thread/123",
        string threadId = "123")
    {
        var chanBoard = new DownloadQueue_ChanBoard
        {
            DownloadQueueId = downloadQueueId,
            Board = board,
            ThreadUrl = threadUrl,
            ThreadId = threadId,
            PostNumber = 1,
            SourceTimestamp = DateTime.UtcNow
        };

        Context.DownloadQueue_ChanBoard.Add(chanBoard);
        Context.SaveChanges();

        return chanBoard;
    }

    /// <summary>
    /// Adds multiple DownloadQueue items for testing ordering/pagination.
    /// </summary>
    public List<DownloadQueue> CreateMultipleDownloadQueueItems(int count, DownloadStatus status = DownloadStatus.Pending)
    {
        var sourceId = CreateImageSource().Id;
        var items = new List<DownloadQueue>();

        for (int i = 0; i < count; i++)
        {
            var item = new DownloadQueue
            {
                ImageSourceId = sourceId,
                SourceUrl = $"https://example.com/image{i}.jpg",
                TargetPath = $"/downloads/image{i}.jpg",
                RequestTime = DateTime.UtcNow.AddMinutes(i),
                Status = status,
                Priority = i,
                Attempts = 0,
                CreatedAt = DateTime.UtcNow.AddMinutes(i)
            };

            Context.DownloadQueue.Add(item);
            items.Add(item);
        }

        Context.SaveChanges();
        return items;
    }
}
