using ContosoDashboard.Data;
using ContosoDashboard.Models;
using ContosoDashboard.ScanFunction;
using ContosoDashboard.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace ContosoDashboard.ScanFunction.Tests;

public class DocumentScanFunctionTests
{
    private static ApplicationDbContext CreateContext(Document document)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var context = new ApplicationDbContext(options);

        context.Users.Add(new User { UserId = 1, Email = "employee@contoso.com", DisplayName = "Test Employee" });
        context.Documents.Add(document);
        context.SaveChanges();

        return context;
    }

    private static Document BuildPendingDocument() => new()
    {
        DocumentId = 1,
        Title = "Report",
        Category = "Reports",
        UploadedByUserId = 1,
        FileName = "report.pdf",
        FilePath = "1/personal/abc.pdf",
        FileSizeBytes = 100,
        FileType = "application/pdf",
        ContentHash = "hash",
        ScanStatus = DocumentScanStatus.PendingScan
    };

    [Fact]
    public async Task RunAsync_MarksDocumentAvailable_WhenScanIsClean()
    {
        var document = BuildPendingDocument();
        await using var context = CreateContext(document);
        var function = new DocumentScanFunction(
            context,
            new FakeFileStorageService(),
            new FakeMalwareScanner(isClean: true),
            NullLogger<DocumentScanFunction>.Instance);

        var message = System.Text.Json.JsonSerializer.Serialize(new { DocumentId = 1, StoragePath = document.FilePath });
        await function.RunAsync(message, CancellationToken.None);

        var updated = await context.Documents.FindAsync(1);
        Assert.Equal(DocumentScanStatus.Available, updated!.ScanStatus);
    }

    [Fact]
    public async Task RunAsync_RejectsDocumentAndLogsActivity_WhenScanDetectsThreat()
    {
        var document = BuildPendingDocument();
        await using var context = CreateContext(document);
        var fileStorage = new FakeFileStorageService();
        var function = new DocumentScanFunction(
            context,
            fileStorage,
            new FakeMalwareScanner(isClean: false),
            NullLogger<DocumentScanFunction>.Instance);

        var message = System.Text.Json.JsonSerializer.Serialize(new { DocumentId = 1, StoragePath = document.FilePath });
        await function.RunAsync(message, CancellationToken.None);

        var updated = await context.Documents.FindAsync(1);
        Assert.Equal(DocumentScanStatus.Rejected, updated!.ScanStatus);
        Assert.True(fileStorage.DeleteCalled);
        Assert.Contains(context.DocumentActivityLogs, log => log.ActivityType == DocumentActivityType.ScanRejected);
    }

    private class FakeFileStorageService : IFileStorageService
    {
        public bool DeleteCalled { get; private set; }

        public Task<string> UploadAsync(Stream fileStream, string fileName, string contentType, int userId, int? projectId) =>
            throw new NotSupportedException();

        public Task DeleteAsync(string filePath)
        {
            DeleteCalled = true;
            return Task.CompletedTask;
        }

        public Task<Stream> DownloadAsync(string filePath) => Task.FromResult<Stream>(new MemoryStream(new byte[] { 1, 2, 3 }));

        public Task<string> GetUrlAsync(string filePath, TimeSpan expiration) => throw new NotSupportedException();
    }

    private class FakeMalwareScanner : IMalwareScanner
    {
        private readonly bool _isClean;

        public FakeMalwareScanner(bool isClean) => _isClean = isClean;

        public Task<ScanResult> ScanAsync(Stream content, CancellationToken cancellationToken) =>
            Task.FromResult(new ScanResult(_isClean, _isClean ? null : "EICAR-Test-Signature"));
    }
}
