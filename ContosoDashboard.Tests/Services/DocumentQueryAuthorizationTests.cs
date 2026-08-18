using System.Text;
using ContosoDashboard.Data;
using ContosoDashboard.Models;
using ContosoDashboard.Services;
using Microsoft.EntityFrameworkCore;

namespace ContosoDashboard.Tests.Services;

public class DocumentQueryAuthorizationTests
{
    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var context = new ApplicationDbContext(options);

        context.Users.Add(new User { UserId = 1, Email = "owner@contoso.com", DisplayName = "Owner User" });
        context.Users.Add(new User { UserId = 2, Email = "member@contoso.com", DisplayName = "Member User" });
        context.Users.Add(new User { UserId = 3, Email = "outsider@contoso.com", DisplayName = "Outsider User" });
        context.Projects.Add(new Project { ProjectId = 1, Name = "Alpha Project", ProjectManagerId = 1 });
        context.ProjectMembers.Add(new ProjectMember { ProjectId = 1, UserId = 2, Role = "Member" });
        context.SaveChanges();

        return context;
    }

    private static UploadDocumentRequest BuildRequest(string title, int? projectId, string content = "Hello World")
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
            ProjectId: projectId,
            Tags: null);
    }

    [Fact]
    public async Task GetMyDocumentsAsync_ReturnsOnlyCallersDocuments()
    {
        await using var context = CreateContext();
        var service = new DocumentService(context, new FakeFileStorageService(), new FakeDocumentScanQueueClient());

        await service.UploadAsync(1, BuildRequest("Owner Doc", null));
        await service.UploadAsync(2, BuildRequest("Member Doc", null, content: "Other content"));

        var result = await service.GetMyDocumentsAsync(1, new DocumentListQuery());

        Assert.Single(result);
        Assert.Equal("Owner Doc", result[0].Title);
    }

    [Fact]
    public async Task GetProjectDocumentsAsync_ThrowsForNonMember()
    {
        await using var context = CreateContext();
        var service = new DocumentService(context, new FakeFileStorageService(), new FakeDocumentScanQueueClient());

        await service.UploadAsync(1, BuildRequest("Project Doc", 1));

        await Assert.ThrowsAsync<UnauthorizedDocumentAccessException>(
            () => service.GetProjectDocumentsAsync(3, 1, new DocumentListQuery()));
    }

    [Fact]
    public async Task GetProjectDocumentsAsync_ReturnsDocuments_ForCurrentMember()
    {
        await using var context = CreateContext();
        var service = new DocumentService(context, new FakeFileStorageService(), new FakeDocumentScanQueueClient());

        await service.UploadAsync(1, BuildRequest("Project Doc", 1));

        var result = await service.GetProjectDocumentsAsync(2, 1, new DocumentListQuery());

        Assert.Single(result);
        Assert.Equal("Project Doc", result[0].Title);
    }

    [Fact]
    public async Task SearchAsync_ExcludesDocuments_UserIsNotAuthorizedFor()
    {
        await using var context = CreateContext();
        var service = new DocumentService(context, new FakeFileStorageService(), new FakeDocumentScanQueueClient());

        await service.UploadAsync(1, BuildRequest("Confidential Report", null));
        await service.UploadAsync(1, BuildRequest("Confidential Project Report", 1, content: "Other content"));

        var outsiderResults = await service.SearchAsync(3, "Confidential");
        var memberResults = await service.SearchAsync(2, "Confidential");

        Assert.Empty(outsiderResults);
        Assert.Single(memberResults);
        Assert.Equal("Confidential Project Report", memberResults[0].Title);
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
