using ContosoDashboard.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace ContosoDashboard.Tests.Storage;

public class LocalFileStorageServiceTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly LocalFileStorageService _service;

    public LocalFileStorageServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "ContosoDashboardTests", Guid.NewGuid().ToString());

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["DocumentStorage:RootPath"] = _tempRoot })
            .Build();

        _service = new LocalFileStorageService(configuration, new FakeHostEnvironment());
    }

    [Fact]
    public async Task UploadAsync_GeneratesPathBeforeWriting_AndNeverUsesOriginalFileName()
    {
        var content = new MemoryStream(new byte[] { 1, 2, 3 });

        var relativePath = await _service.UploadAsync(content, "secret-plan.pdf", "application/pdf", userId: 42, projectId: 7);

        Assert.StartsWith("42/7/", relativePath);
        Assert.DoesNotContain("secret-plan", relativePath);
        Assert.True(File.Exists(Path.Combine(_tempRoot, relativePath)));
    }

    [Fact]
    public async Task UploadAsync_UsesPersonalSegment_WhenNoProject()
    {
        var content = new MemoryStream(new byte[] { 1 });

        var relativePath = await _service.UploadAsync(content, "notes.txt", "text/plain", userId: 5, projectId: null);

        Assert.StartsWith("5/personal/", relativePath);
    }

    [Fact]
    public async Task DeleteAsync_IsIdempotent_WhenFileAlreadyAbsent()
    {
        var exception = await Record.ExceptionAsync(() => _service.DeleteAsync("does/not/exist.pdf"));

        Assert.Null(exception);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    private class FakeHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "ContosoDashboard.Tests";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
