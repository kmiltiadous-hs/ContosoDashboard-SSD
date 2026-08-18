using System.Text;
using ContosoDashboard.Data;
using ContosoDashboard.Models;
using ContosoDashboard.Services;
using Microsoft.EntityFrameworkCore;

namespace ContosoDashboard.Tests.Services;

public class DuplicateDetectionTests
{
    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var context = new ApplicationDbContext(options);

        context.Users.Add(new User { UserId = 1, Email = "employee@contoso.com", DisplayName = "Test Employee" });
        context.SaveChanges();

        return context;
    }

    private static UploadDocumentRequest BuildRequest(string title = "Report", string content = "Hello World")
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        return new UploadDocumentRequest(
            FileStream: new MemoryStream(bytes),
            FileName: "report.txt",
            ContentType: "text/plain",
            FileSizeBytes: bytes.Length,
            Title: title,
            Category: "Reports",
            Description: null,
            ProjectId: null,
            Tags: null);
    }

    [Fact]
    public async Task UploadAsync_RejectsDuplicate_WhenSameTitleProjectAndContent()
    {
        await using var context = CreateContext();
        var service = new DocumentService(context, new FakeFileStorageService(), new FakeDocumentScanQueueClient());

        await service.UploadAsync(1, BuildRequest());

        await Assert.ThrowsAsync<DuplicateDocumentException>(() => service.UploadAsync(1, BuildRequest()));
    }

    [Fact]
    public async Task UploadAsync_Allows_WhenContentDiffers()
    {
        await using var context = CreateContext();
        var service = new DocumentService(context, new FakeFileStorageService(), new FakeDocumentScanQueueClient());

        await service.UploadAsync(1, BuildRequest(content: "First version"));
        var second = await service.UploadAsync(1, BuildRequest(content: "Second version"));

        Assert.NotNull(second);
    }

    private class FakeFileStorageService : IFileStorageService
    {
        public Task<string> UploadAsync(Stream fileStream, string fileName, string contentType, int userId, int? projectId) =>
            Task.FromResult($"{userId}/personal/{Guid.NewGuid()}{Path.GetExtension(fileName)}");

        public Task DeleteAsync(string filePath) => Task.CompletedTask;

        public Task<Stream> DownloadAsync(string filePath) => Task.FromResult<Stream>(new MemoryStream());

        public Task<string> GetUrlAsync(string filePath, TimeSpan expiration) => throw new NotSupportedException();
    }

    private class FakeDocumentScanQueueClient : IDocumentScanQueueClient
    {
        public Task EnqueueScanRequestAsync(int documentId, string storagePath, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
