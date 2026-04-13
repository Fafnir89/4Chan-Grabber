using FourChanGrabber.Data;
using FourChanGrabber.Data.Models;
using FourChanGrabber.Data.Models.Enums;
using FourChanGrabber.Models;
using FourChanGrabber.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FourChanGrabber.Controllers;

/// <summary>
/// REST API for managing the download queue and controlling the download worker.
/// </summary>
[ApiController]
[Route("api/downloadqueue")]
public class DownloadQueueController : ControllerBase
{
    private readonly IQueueService _queueService;
    private readonly DownloadManager _downloadManager;
    private readonly MediaDbContext _dbContext;

    private const int DefaultLimit = 50;
    private const int MaxLimit = 200;

    public DownloadQueueController(
        IQueueService queueService,
        DownloadManager downloadManager,
        MediaDbContext dbContext)
    {
        _queueService = queueService;
        _downloadManager = downloadManager;
        _dbContext = dbContext;
    }

    /// <summary>
    /// List queue items with optional filtering and pagination.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<DownloadQueueListResponse>> GetQueueItems(
        [FromQuery] DownloadStatus? status = null,
        [FromQuery] int? sourceId = null,
        [FromQuery] int limit = DefaultLimit,
        [FromQuery] int offset = 0,
        CancellationToken ct = default)
    {
        limit = Math.Clamp(limit, 1, MaxLimit);

        var query = _dbContext.DownloadQueue
            .Include(q => q.ImageSource)
            .Include(q => q.ChanBoard)
            .AsQueryable();

        if (status.HasValue)
            query = query.Where(q => q.Status == status.Value);

        if (sourceId.HasValue)
            query = query.Where(q => q.ImageSourceId == sourceId.Value);

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(q => q.Priority)
            .ThenBy(q => q.RequestTime)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(ct);

        var stats = await _queueService.GetStatsAsync(ct);

        var response = new DownloadQueueListResponse
        {
            Items = items.Select(MapToDto).ToList(),
            Total = total,
            Stats = stats
        };

        return Ok(response);
    }

    /// <summary>
    /// Get a specific queue item by ID.
    /// </summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<DownloadQueueDetailResponse>> GetQueueItem(int id, CancellationToken ct)
    {
        var item = await _dbContext.DownloadQueue
            .Include(q => q.ImageSource)
            .Include(q => q.ChanBoard)
            .FirstOrDefaultAsync(q => q.Id == id, ct);

        if (item == null)
            return NotFound(new { message = $"Queue item {id} not found" });

        return Ok(MapToDetailDto(item));
    }

    /// <summary>
    /// Pause the download worker. Active downloads will complete but no new ones will start.
    /// </summary>
    [HttpPost("pause")]
    public ActionResult Pause()
    {
        _downloadManager.Pause();
        return Ok(new { message = "Worker paused" });
    }

    /// <summary>
    /// Resume the download worker after a pause.
    /// </summary>
    [HttpPost("resume")]
    public ActionResult Resume()
    {
        _downloadManager.Resume();
        return Ok(new { message = "Worker resumed" });
    }

    /// <summary>
    /// Get the current worker status.
    /// </summary>
    [HttpGet("status")]
    public ActionResult<WorkerStatusResponse> GetWorkerStatus()
    {
        var status = _downloadManager.Status;
        var activeDownloads = _downloadManager.ActiveDownloadCount;

        // Get max concurrent from any configured source, default to 3
        var maxConcurrent = 3;

        return Ok(new WorkerStatusResponse
        {
            Status = status.ToString(),
            ActiveDownloads = activeDownloads,
            MaxConcurrent = maxConcurrent
        });
    }

    /// <summary>
    /// Get queue statistics.
    /// </summary>
    [HttpGet("stats")]
    public async Task<ActionResult<QueueStats>> GetStats(CancellationToken ct)
    {
        var stats = await _queueService.GetStatsAsync(ct);
        return Ok(stats);
    }

    /// <summary>
    /// Retry a failed queue item by resetting it to Pending status.
    /// </summary>
    [HttpPost("{id:int}/retry")]
    public async Task<ActionResult> RetryItem(int id, CancellationToken ct)
    {
        var item = await _dbContext.DownloadQueue.FindAsync(new object[] { id }, ct);
        if (item == null)
            return NotFound(new { message = $"Queue item {id} not found" });

        if (item.Status != DownloadStatus.Failed)
            return BadRequest(new { message = $"Can only retry failed items. Current status: {item.Status}" });

        item.Status = DownloadStatus.Pending;
        item.ErrorMessage = null;
        await _dbContext.SaveChangesAsync(ct);

        return Ok(new { message = $"Queue item {id} queued for retry" });
    }

    /// <summary>
    /// Delete a queue item.
    /// </summary>
    [HttpDelete("{id:int}")]
    public async Task<ActionResult> DeleteItem(int id, CancellationToken ct)
    {
        var item = await _dbContext.DownloadQueue.FindAsync(new object[] { id }, ct);
        if (item == null)
            return NotFound(new { message = $"Queue item {id} not found" });

        _dbContext.DownloadQueue.Remove(item);

        // Also remove associated ChanBoard record if exists
        var chanBoard = await _dbContext.DownloadQueue_ChanBoard
            .FirstOrDefaultAsync(cb => cb.DownloadQueueId == id, ct);
        if (chanBoard != null)
            _dbContext.DownloadQueue_ChanBoard.Remove(chanBoard);

        await _dbContext.SaveChangesAsync(ct);

        return NoContent();
    }

    // ─── DTO Mapping ────────────────────────────────────────────────────────────────

    private static DownloadQueueItemDto MapToDto(DownloadQueue item)
    {
        return new DownloadQueueItemDto
        {
            Id = item.Id,
            ImageSourceId = item.ImageSourceId,
            ImageSourceName = item.ImageSource?.Name,
            SourceUrl = item.SourceUrl,
            TargetPath = item.TargetPath,
            RequestTime = item.RequestTime,
            Status = item.Status.ToString(),
            Priority = item.Priority,
            Attempts = item.Attempts,
            ErrorMessage = item.ErrorMessage,
            CreatedAt = item.CreatedAt,
            ChanBoard = item.ChanBoard != null ? new ChanBoardDto
            {
                Board = item.ChanBoard.Board,
                ThreadUrl = item.ChanBoard.ThreadUrl,
                PostNumber = item.ChanBoard.PostNumber,
                ThreadId = item.ChanBoard.ThreadId
            } : null
        };
    }

    private static DownloadQueueDetailResponse MapToDetailDto(DownloadQueue item)
    {
        return new DownloadQueueDetailResponse
        {
            Id = item.Id,
            ImageSourceId = item.ImageSourceId,
            ImageSourceName = item.ImageSource?.Name,
            SourceUrl = item.SourceUrl,
            TargetPath = item.TargetPath,
            RequestTime = item.RequestTime,
            Status = item.Status.ToString(),
            Priority = item.Priority,
            Attempts = item.Attempts,
            ErrorMessage = item.ErrorMessage,
            ExpectedHash = item.ExpectedHash,
            ExpectedHashType = item.ExpectedHashType,
            CreatedAt = item.CreatedAt,
            StartedAt = item.StartedAt,
            CompletedAt = item.CompletedAt,
            ChanBoard = item.ChanBoard != null ? new ChanBoardDto
            {
                Board = item.ChanBoard.Board,
                ThreadUrl = item.ChanBoard.ThreadUrl,
                PostNumber = item.ChanBoard.PostNumber,
                ThreadId = item.ChanBoard.ThreadId
            } : null
        };
    }
}

// ─── Request DTOs ─────────────────────────────────────────────────────────────────

// No request DTOs currently needed (query parameters are used)

// ─── Response DTOs ────────────────────────────────────────────────────────────────

public class DownloadQueueListResponse
{
    public List<DownloadQueueItemDto> Items { get; set; } = new();
    public int Total { get; set; }
    public QueueStats Stats { get; set; } = new();
}

public class DownloadQueueItemDto
{
    public int Id { get; set; }
    public int ImageSourceId { get; set; }
    public string? ImageSourceName { get; set; }
    public string SourceUrl { get; set; } = string.Empty;
    public string TargetPath { get; set; } = string.Empty;
    public DateTime RequestTime { get; set; }
    public string Status { get; set; } = string.Empty;
    public int Priority { get; set; }
    public int Attempts { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; }
    public ChanBoardDto? ChanBoard { get; set; }
}

public class DownloadQueueDetailResponse : DownloadQueueItemDto
{
    public string? ExpectedHash { get; set; }
    public string? ExpectedHashType { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public class ChanBoardDto
{
    public string Board { get; set; } = string.Empty;
    public string ThreadUrl { get; set; } = string.Empty;
    public int? PostNumber { get; set; }
    public string ThreadId { get; set; } = string.Empty;
}

public class WorkerStatusResponse
{
    public string Status { get; set; } = string.Empty;
    public int ActiveDownloads { get; set; }
    public int MaxConcurrent { get; set; }
}
