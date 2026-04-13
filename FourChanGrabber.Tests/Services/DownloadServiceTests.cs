using System.Net;
using System.Text.Json;
using FourChanGrabber.Models;
using FourChanGrabber.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Xunit;

namespace FourChanGrabber.Tests.Services;

/// <summary>
/// Unit tests for DownloadService covering download logic, hash verification, and FlareSolverr.
/// </summary>
public class DownloadServiceTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly Mock<IHttpClientFactory> _mockHttpClientFactory;
    private readonly Mock<ILogger<DownloadService>> _mockLogger;
    private readonly DownloadService _sut;

    public DownloadServiceTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        _mockHttpClientFactory = new Mock<IHttpClientFactory>();
        _mockLogger = new Mock<ILogger<DownloadService>>();

        _sut = new DownloadService(
            _mockHttpClientFactory.Object,
            _mockLogger.Object,
            _tempDirectory);
    }

    public void Dispose()
    {
        // Clean up temp directory
        if (Directory.Exists(_tempDirectory))
        {
            try
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    #region DownloadFileAsync - Basic Download Tests

    [Fact]
    public async Task DownloadFileAsync_SuccessfulDownload_ReturnsSuccessResult()
    {
        // Arrange
        var testContent = "test file content"u8.ToArray();
        SetupMockHttpClientFactory("Download", CreateHttpResponseMessage(testContent));

        var sourceUrl = "https://example.com/image.jpg";
        var targetPath = Path.Combine(_tempDirectory, "image.jpg");
        var config = new SourceConfig();

        // Act
        var result = await _sut.DownloadFileAsync(sourceUrl, targetPath, null, config);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.FileHash);
        Assert.True(result.FileSize > 0);
        Assert.True(File.Exists(targetPath));
    }

    [Fact]
    public async Task DownloadFileAsync_DownloadCreatesTempFileAndDeletesOnFailure()
    {
        // Arrange
        SetupMockHttpClientFactoryToThrow("Download", new HttpRequestException("Network error"));

        var sourceUrl = "https://example.com/image.jpg";
        var targetPath = Path.Combine(_tempDirectory, "image.jpg");
        var config = new SourceConfig();

        // Act
        var result = await _sut.DownloadFileAsync(sourceUrl, targetPath, null, config);

        // Assert
        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
        Assert.False(File.Exists(targetPath));

        // Verify temp file was cleaned up
        var tempFiles = Directory.GetFiles(_tempDirectory, "*.tmp");
        Assert.Empty(tempFiles);
    }

    [Fact]
    public async Task DownloadFileAsync_CreatesTargetDirectoryIfNotExists()
    {
        // Arrange
        var testContent = "test file content"u8.ToArray();
        SetupMockHttpClientFactory("Download", CreateHttpResponseMessage(testContent));

        var sourceUrl = "https://example.com/image.jpg";
        var targetPath = Path.Combine(_tempDirectory, "subdir", "image.jpg");
        var config = new SourceConfig();

        // Act
        var result = await _sut.DownloadFileAsync(sourceUrl, targetPath, null, config);

        // Assert
        Assert.True(result.Success);
        Assert.True(File.Exists(targetPath));
    }

    [Fact]
    public async Task DownloadFileAsync_OverwritesExistingTargetFile()
    {
        // Arrange
        var testContent = "new file content"u8.ToArray();
        SetupMockHttpClientFactory("Download", CreateHttpResponseMessage(testContent));

        var sourceUrl = "https://example.com/image.jpg";
        var targetPath = Path.Combine(_tempDirectory, "image.jpg");

        // Create existing file
        await File.WriteAllTextAsync(targetPath, "old content");
        Assert.True(File.Exists(targetPath));

        var config = new SourceConfig();

        // Act
        var result = await _sut.DownloadFileAsync(sourceUrl, targetPath, null, config);

        // Assert
        Assert.True(result.Success);
        var content = await File.ReadAllTextAsync(targetPath);
        Assert.Equal("new file content", content);
    }

    [Fact]
    public async Task DownloadFileAsync_HttpError_ReturnsFailureResult()
    {
        // Arrange
        var response = new HttpResponseMessage(HttpStatusCode.NotFound);
        response.EnsureSuccessStatusCode(); // Will throw
        SetupMockHttpClientFactory("Download", response);

        var sourceUrl = "https://example.com/image.jpg";
        var targetPath = Path.Combine(_tempDirectory, "image.jpg");
        var config = new SourceConfig();

        // Act
        var result = await _sut.DownloadFileAsync(sourceUrl, targetPath, null, config);

        // Assert
        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    #endregion

    #region DownloadFileAsync - Hash Verification Tests

    [Fact]
    public async Task DownloadFileAsync_MatchingMd5Hash_ReturnsSuccess()
    {
        // Arrange - Create a file with known MD5 hash
        var testContent = "hello world"u8.ToArray(); // MD5: 5eb63bbbe01eeed093cb22bb8f5acdc3
        SetupMockHttpClientFactory("Download", CreateHttpResponseMessage(testContent));

        var sourceUrl = "https://example.com/image.jpg";
        var targetPath = Path.Combine(_tempDirectory, "image.jpg");
        var config = new SourceConfig();
        var expectedMd5 = "5eb63bbbe01eeed093cb22bb8f5acdc3";

        // Act
        var result = await _sut.DownloadFileAsync(sourceUrl, targetPath, expectedMd5, config);

        // Assert
        Assert.True(result.Success);
        Assert.Null(result.HashMismatch);
        Assert.NotNull(result.FileHash); // SHA256 for storage
    }

    [Fact]
    public async Task DownloadFileAsync_MismatchedMd5Hash_ReturnsRetryableWhenRetryConfigured()
    {
        // Arrange
        var testContent = "different content"u8.ToArray();
        SetupMockHttpClientFactory("Download", CreateHttpResponseMessage(testContent));

        var sourceUrl = "https://example.com/image.jpg";
        var targetPath = Path.Combine(_tempDirectory, "image.jpg");
        var config = new SourceConfig { RetryAttempts = 2 };
        var expectedMd5 = "5eb63bbbe01eeed093cb22bb8f5acdc3"; // Wrong hash

        // Act
        var result = await _sut.DownloadFileAsync(sourceUrl, targetPath, expectedMd5, config);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(HashMismatchType.Retryable, result.HashMismatch);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("Hash mismatch", result.ErrorMessage);
    }

    [Fact]
    public async Task DownloadFileAsync_MismatchedMd5Hash_ReturnsCorruptWhenNoRetryConfigured()
    {
        // Arrange
        var testContent = "different content"u8.ToArray();
        SetupMockHttpClientFactory("Download", CreateHttpResponseMessage(testContent));

        var sourceUrl = "https://example.com/image.jpg";
        var targetPath = Path.Combine(_tempDirectory, "image.jpg");
        var config = new SourceConfig { RetryAttempts = 0 }; // No retries
        var expectedMd5 = "5eb63bbbe01eeed093cb22bb8f5acdc3"; // Wrong hash

        // Act
        var result = await _sut.DownloadFileAsync(sourceUrl, targetPath, expectedMd5, config);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(HashMismatchType.Corrupt, result.HashMismatch);
    }

    [Fact]
    public async Task DownloadFileAsync_CaseInsensitiveHashComparison()
    {
        // Arrange - Same hash but uppercase
        var testContent = "hello world"u8.ToArray();
        SetupMockHttpClientFactory("Download", CreateHttpResponseMessage(testContent));

        var sourceUrl = "https://example.com/image.jpg";
        var targetPath = Path.Combine(_tempDirectory, "image.jpg");
        var config = new SourceConfig();
        var expectedMd5Uppercase = "5EB63BBBE01EEED093CB22BB8F5ACDC3";

        // Act
        var result = await _sut.DownloadFileAsync(sourceUrl, targetPath, expectedMd5Uppercase, config);

        // Assert
        Assert.True(result.Success);
    }

    [Fact]
    public async Task DownloadFileAsync_NullExpectedHash_SkipsHashVerification()
    {
        // Arrange - Content with any hash
        var testContent = "any content"u8.ToArray();
        SetupMockHttpClientFactory("Download", CreateHttpResponseMessage(testContent));

        var sourceUrl = "https://example.com/image.jpg";
        var targetPath = Path.Combine(_tempDirectory, "image.jpg");
        var config = new SourceConfig();

        // Act
        var result = await _sut.DownloadFileAsync(sourceUrl, targetPath, null, config);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.FileHash);
    }

    #endregion

    #region DownloadFileAsync - FlareSolverr Tests

    [Fact]
    public async Task DownloadFileAsync_WithCloudFlareProxy_UsesFlareSolverrPath()
    {
        // Arrange - Base64 encoded "test content"
        var testContent = "test content"u8.ToArray();
        var base64Content = Convert.ToBase64String(testContent);

        var flareSolverrResponse = new
        {
            solution = new
            {
                response = base64Content,
                url = "https://example.com/image.jpg"
            }
        };

        SetupMockHttpClientFactory("FlareSolverr",
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(flareSolverrResponse))
            });

        var sourceUrl = "https://example.com/image.jpg";
        var targetPath = Path.Combine(_tempDirectory, "image.jpg");
        var config = new SourceConfig { CloudFlareProxyUrl = "http://localhost:8191" };

        // Act
        var result = await _sut.DownloadFileAsync(sourceUrl, targetPath, null, config);

        // Assert
        Assert.True(result.Success);
        Assert.True(File.Exists(targetPath));
    }

    [Fact]
    public async Task DownloadFileAsync_FlareSolverrInvalidResponse_ThrowsException()
    {
        // Arrange - Invalid JSON response
        SetupMockHttpClientFactory("FlareSolverr",
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("invalid json")
            });

        var sourceUrl = "https://example.com/image.jpg";
        var targetPath = Path.Combine(_tempDirectory, "image.jpg");
        var config = new SourceConfig { CloudFlareProxyUrl = "http://localhost:8191" };

        // Act
        var result = await _sut.DownloadFileAsync(sourceUrl, targetPath, null, config);

        // Assert
        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task DownloadFileAsync_FlareSolverrEmptyResponse_ThrowsException()
    {
        // Arrange - Response with null solution
        var flareSolverrResponse = new { solution = (object?)null };
        SetupMockHttpClientFactory("FlareSolverr",
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(flareSolverrResponse))
            });

        var sourceUrl = "https://example.com/image.jpg";
        var targetPath = Path.Combine(_tempDirectory, "image.jpg");
        var config = new SourceConfig { CloudFlareProxyUrl = "http://localhost:8191" };

        // Act
        var result = await _sut.DownloadFileAsync(sourceUrl, targetPath, null, config);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("invalid", result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DownloadFileAsync_FlareSolverrEmptyResponseString_ThrowsException()
    {
        // Arrange - Response with empty response string
        var flareSolverrResponse = new
        {
            solution = new
            {
                response = "",
                url = "https://example.com/image.jpg"
            }
        };
        SetupMockHttpClientFactory("FlareSolverr",
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(flareSolverrResponse))
            });

        var sourceUrl = "https://example.com/image.jpg";
        var targetPath = Path.Combine(_tempDirectory, "image.jpg");
        var config = new SourceConfig { CloudFlareProxyUrl = "http://localhost:8191" };

        // Act
        var result = await _sut.DownloadFileAsync(sourceUrl, targetPath, null, config);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("empty", result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DownloadFileAsync_FlareSolverrResponseTooLarge_ThrowsException()
    {
        // Arrange - Response larger than 100MB
        var largeContent = new string('x', 100_000_001); // Just over 100MB limit
        var flareSolverrResponse = new
        {
            solution = new
            {
                response = Convert.ToBase64String(largeContent.ToArray().Select(x => (byte)x).ToArray()),
                url = "https://example.com/image.jpg"
            }
        };
        SetupMockHttpClientFactory("FlareSolverr",
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(flareSolverrResponse))
            });

        var sourceUrl = "https://example.com/image.jpg";
        var targetPath = Path.Combine(_tempDirectory, "image.jpg");
        var config = new SourceConfig { CloudFlareProxyUrl = "http://localhost:8191" };

        // Act
        var result = await _sut.DownloadFileAsync(sourceUrl, targetPath, null, config);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("large", result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region DownloadFileAsync - Rate Limiting Tests

    [Fact]
    public async Task DownloadFileAsync_RateLimiting_AppliesDelayBetweenRequestsToSameHost()
    {
        // Arrange
        var requestTimes = new List<DateTime>();
        var testContent = "test"u8.ToArray();

        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((_, _) => requestTimes.Add(DateTime.UtcNow))
            .ReturnsAsync(() => CreateHttpResponseMessage(testContent));

        _mockHttpClientFactory
            .Setup(x => x.CreateClient("Download"))
            .Returns(new HttpClient(mockHandler.Object));

        var config = new SourceConfig { RateLimitPerSecond = 10 }; // 100ms between requests
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

        var sut = new DownloadService(_mockHttpClientFactory.Object, _mockLogger.Object, tempDir);

        // Act - Make multiple requests to same host
        for (int i = 0; i < 3; i++)
        {
            await sut.DownloadFileAsync(
                $"https://example.com/image{i}.jpg",
                Path.Combine(tempDir, $"image{i}.jpg"),
                null,
                config);
        }

        // Assert - Verify delay between requests
        Assert.Equal(3, requestTimes.Count);
        for (int i = 1; i < requestTimes.Count; i++)
        {
            var elapsed = (requestTimes[i] - requestTimes[i - 1]).TotalMilliseconds;
            Assert.True(elapsed >= 50); // At least 50ms for rate of 10/sec (100ms min, allow some tolerance)
        }

        // Cleanup
        Directory.Delete(tempDir, true);
    }

    [Fact]
    public async Task DownloadFileAsync_ZeroRateLimit_SkipsRateLimiting()
    {
        // Arrange
        var requestCount = 0;
        var testContent = "test"u8.ToArray();

        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((_, _) => requestCount++)
            .ReturnsAsync(() => CreateHttpResponseMessage(testContent));

        _mockHttpClientFactory
            .Setup(x => x.CreateClient("Download"))
            .Returns(new HttpClient(mockHandler.Object));

        var config = new SourceConfig { RateLimitPerSecond = 0 }; // No rate limiting
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

        var sut = new DownloadService(_mockHttpClientFactory.Object, _mockLogger.Object, tempDir);

        // Act
        await sut.DownloadFileAsync(
            "https://example.com/image.jpg",
            Path.Combine(tempDir, "image.jpg"),
            null,
            config);

        // Assert - Request was made without delay
        Assert.Equal(1, requestCount);

        // Cleanup
        Directory.Delete(tempDir, true);
    }

    #endregion

    #region DownloadFileAsync - Cancellation Tests

    [Fact]
    public async Task DownloadFileAsync_Cancellation_ThrowsOperationCanceledException()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        var testContent = "test"u8.ToArray();
        SetupMockHttpClientFactory("Download", CreateHttpResponseMessage(testContent));

        var sourceUrl = "https://example.com/image.jpg";
        var targetPath = Path.Combine(_tempDirectory, "image.jpg");
        var config = new SourceConfig();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _sut.DownloadFileAsync(sourceUrl, targetPath, null, config, ct: cts.Token));
    }

    #endregion

    #region Helper Methods

    private void SetupMockHttpClientFactory(string clientName, HttpResponseMessage response)
    {
        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);

        var httpClient = new HttpClient(mockHandler.Object);
        _mockHttpClientFactory
            .Setup(x => x.CreateClient(clientName))
            .Returns(httpClient);
    }

    private void SetupMockHttpClientFactoryToThrow(string clientName, Exception exception)
    {
        var mockHandler = new Mock<HttpMessageHandler>();
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(exception);

        var httpClient = new HttpClient(mockHandler.Object);
        _mockHttpClientFactory
            .Setup(x => x.CreateClient(clientName))
            .Returns(httpClient);
    }

    private static HttpResponseMessage CreateHttpResponseMessage(byte[] content)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(content)
        };
    }

    #endregion
}
