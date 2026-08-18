using ContosoDashboard.Models;

namespace ContosoDashboard.Services;

public interface IDocumentService
{
    Task<Document> UploadAsync(int uploaderUserId, UploadDocumentRequest request);
    Task<IReadOnlyList<Document>> GetMyDocumentsAsync(int userId, DocumentListQuery query);
    Task<IReadOnlyList<Document>> GetProjectDocumentsAsync(int userId, int projectId, DocumentListQuery query);
    Task<IReadOnlyList<Document>> GetSharedWithMeAsync(int userId);
    Task<IReadOnlyList<Document>> SearchAsync(int userId, string searchTerm);
    Task<Stream> DownloadAsync(int userId, int documentId);
    Task<Document> UpdateMetadataAsync(int userId, int documentId, UpdateDocumentMetadataRequest request);
    Task ReplaceFileAsync(int userId, int documentId, Stream newContent, string fileName, string contentType);
    Task DeleteAsync(int userId, int documentId);
    Task ShareAsync(int userId, int documentId, IReadOnlyList<int> recipientUserIds);
}

public record UploadDocumentRequest(
    Stream FileStream,
    string FileName,
    string ContentType,
    long FileSizeBytes,
    string Title,
    string Category,
    string? Description,
    int? ProjectId,
    string? Tags);

public record DocumentListQuery(
    string? SortBy = null,
    bool SortDescending = false,
    string? Category = null,
    int? ProjectId = null,
    DateTime? FromDate = null,
    DateTime? ToDate = null);

public record UpdateDocumentMetadataRequest(
    string Title,
    string Category,
    string? Description,
    string? Tags,
    byte[] RowVersion);
