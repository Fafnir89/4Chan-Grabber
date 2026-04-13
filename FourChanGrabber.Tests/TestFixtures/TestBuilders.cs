using FourChanGrabber.Data.Models;
using FourChanGrabber.Data.Models.Enums;

namespace FourChanGrabber.Tests.TestFixtures;

/// <summary>
/// Builder for creating test DownloadQueue items with fluent API.
/// </summary>
public class DownloadQueueBuilder
{
    private readonly DownloadQueue _item = new()
    {
        SourceUrl = "https://example.com/image.jpg",
        TargetPath = "/downloads/image.jpg",
        RequestTime = DateTime.UtcNow,
        Status = DownloadStatus.Pending,
        Priority = 0,
        Attempts = 0,
        CreatedAt = DateTime.UtcNow
    };

    public DownloadQueueBuilder WithId(int id)
    {
        _item.Id = id;
        return this;
    }

    public DownloadQueueBuilder WithStatus(DownloadStatus status)
    {
        _item.Status = status;
        return this;
    }

    public DownloadQueueBuilder WithImageSourceId(int imageSourceId)
    {
        _item.ImageSourceId = imageSourceId;
        return this;
    }

    public DownloadQueueBuilder WithPriority(int priority)
    {
        _item.Priority = priority;
        return this;
    }

    public DownloadQueueBuilder WithSourceUrl(string sourceUrl)
    {
        _item.SourceUrl = sourceUrl;
        return this;
    }

    public DownloadQueueBuilder WithTargetPath(string targetPath)
    {
        _item.TargetPath = targetPath;
        return this;
    }

    public DownloadQueueBuilder WithExpectedHash(string? hash, string? hashType = "md5")
    {
        _item.ExpectedHash = hash;
        _item.ExpectedHashType = hashType;
        return this;
    }

    public DownloadQueueBuilder WithAttempts(int attempts)
    {
        _item.Attempts = attempts;
        return this;
    }

    public DownloadQueueBuilder WithErrorMessage(string? errorMessage)
    {
        _item.ErrorMessage = errorMessage;
        return this;
    }

    public DownloadQueueBuilder WithStartedAt(DateTime? startedAt)
    {
        _item.StartedAt = startedAt;
        return this;
    }

    public DownloadQueueBuilder WithRequestTime(DateTime requestTime)
    {
        _item.RequestTime = requestTime;
        return this;
    }

    public DownloadQueue Build() => _item;
}

/// <summary>
/// Builder for creating test ImageSource items with fluent API.
/// </summary>
public class ImageSourceBuilder
{
    private readonly ImageSource _source = new()
    {
        Name = "Test Source",
        BaseUrl = "https://example.com",
        IsEnabled = true
    };

    public ImageSourceBuilder WithId(int id)
    {
        _source.Id = id;
        return this;
    }

    public ImageSourceBuilder WithName(string name)
    {
        _source.Name = name;
        return this;
    }

    public ImageSourceBuilder WithBaseUrl(string baseUrl)
    {
        _source.BaseUrl = baseUrl;
        return this;
    }

    public ImageSourceBuilder WithConfig(Action<ImageSourceConfig> configure)
    {
        var config = new ImageSourceConfig();
        configure(config);
        ImageSource.SetConfig(_source, config);
        return this;
    }

    public ImageSourceBuilder IsEnabled(bool enabled)
    {
        _source.IsEnabled = enabled;
        return this;
    }

    public ImageSource Build() => _source;
}
