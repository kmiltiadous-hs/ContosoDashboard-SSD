using Microsoft.Extensions.Configuration;

namespace ContosoDashboard.Services;

// System.IO-based storage under App_Data/uploads, outside wwwroot (training implementation)
public class LocalFileStorageService : IFileStorageService
{
    private readonly string _rootPath;

    public LocalFileStorageService(IConfiguration configuration, Microsoft.Extensions.Hosting.IHostEnvironment environment)
    {
        var configuredRoot = configuration["DocumentStorage:RootPath"] ?? "App_Data/uploads";
        _rootPath = Path.IsPathRooted(configuredRoot)
            ? configuredRoot
            : Path.Combine(environment.ContentRootPath, configuredRoot);
    }

    public async Task<string> UploadAsync(Stream fileStream, string fileName, string contentType, int userId, int? projectId)
    {
        var extension = Path.GetExtension(fileName);
        var projectSegment = projectId?.ToString() ?? "personal";
        // GUID-based storage filename — never the user-supplied name (FR-008)
        var relativePath = Path.Combine(userId.ToString(), projectSegment, $"{Guid.NewGuid()}{extension}");
        var fullPath = Path.Combine(_rootPath, relativePath);

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        await using (var destination = File.Create(fullPath))
        {
            await fileStream.CopyToAsync(destination);
        }

        return relativePath.Replace('\\', '/');
    }

    public Task DeleteAsync(string filePath)
    {
        var fullPath = Path.Combine(_rootPath, filePath);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }

        return Task.CompletedTask;
    }

    public Task<Stream> DownloadAsync(string filePath)
    {
        var fullPath = Path.Combine(_rootPath, filePath);
        Stream stream = File.OpenRead(fullPath);
        return Task.FromResult(stream);
    }

    public Task<string> GetUrlAsync(string filePath, TimeSpan expiration)
    {
        // Reserved for a future signed-URL-capable implementation (e.g., Azure Blob SAS)
        throw new NotSupportedException("Direct URLs are not supported by LocalFileStorageService; use the authorized download controller route.");
    }
}
