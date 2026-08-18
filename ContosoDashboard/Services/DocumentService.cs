using System.Security.Cryptography;
using ContosoDashboard.Data;
using ContosoDashboard.Models;
using Microsoft.EntityFrameworkCore;

namespace ContosoDashboard.Services;

public class DocumentService : IDocumentService
{
    private const long MaxFileSizeBytes = 25 * 1024 * 1024;

    private readonly ApplicationDbContext _context;
    private readonly IFileStorageService _fileStorageService;
    private readonly IDocumentScanQueueClient _scanQueueClient;

    public DocumentService(
        ApplicationDbContext context,
        IFileStorageService fileStorageService,
        IDocumentScanQueueClient scanQueueClient)
    {
        _context = context;
        _fileStorageService = fileStorageService;
        _scanQueueClient = scanQueueClient;
    }

    public async Task<Document> UploadAsync(int uploaderUserId, UploadDocumentRequest request)
    {
        if (request.FileSizeBytes <= 0 || request.FileSizeBytes > MaxFileSizeBytes)
        {
            throw new ArgumentException("File size must be greater than 0 and no more than 25 MB.", nameof(request));
        }

        if (!FileSignatureValidator.IsExtensionAllowed(request.FileName))
        {
            throw new ArgumentException("File type is not supported.", nameof(request));
        }

        // Authorization: if the document is being associated with a project, caller must be a current member (or its PM)
        if (request.ProjectId is int projectId)
        {
            var project = await _context.Projects.FindAsync(projectId)
                ?? throw new UnauthorizedDocumentAccessException("Project does not exist.");
            var isMember = project.ProjectManagerId == uploaderUserId ||
                await _context.ProjectMembers.AnyAsync(pm => pm.ProjectId == projectId && pm.UserId == uploaderUserId);
            if (!isMember)
            {
                throw new UnauthorizedDocumentAccessException("User is not a member of the specified project.");
            }
        }

        // Read the file once into memory to validate content signature, compute the hash, and store it
        using var buffer = new MemoryStream();
        await request.FileStream.CopyToAsync(buffer);
        var contentBytes = buffer.ToArray();

        var header = contentBytes.Take(8).ToArray();
        if (!FileSignatureValidator.MatchesContentSignature(request.FileName, header))
        {
            throw new ArgumentException("File content does not match its declared type.", nameof(request));
        }

        var contentHash = Convert.ToHexString(SHA256.HashData(contentBytes));

        var isDuplicate = await _context.Documents.AnyAsync(d =>
            d.UploadedByUserId == uploaderUserId &&
            d.Title == request.Title &&
            d.ProjectId == request.ProjectId &&
            d.ContentHash == contentHash);

        if (isDuplicate)
        {
            throw new DuplicateDocumentException("A document with the same title, project, and content already exists.");
        }

        buffer.Position = 0;
        var filePath = await _fileStorageService.UploadAsync(buffer, request.FileName, request.ContentType, uploaderUserId, request.ProjectId);

        var document = new Document
        {
            Title = request.Title,
            Description = request.Description,
            Category = request.Category,
            Tags = request.Tags,
            UploadedByUserId = uploaderUserId,
            ProjectId = request.ProjectId,
            FileName = request.FileName,
            FilePath = filePath,
            FileSizeBytes = request.FileSizeBytes,
            FileType = request.ContentType,
            ContentHash = contentHash,
            ScanStatus = DocumentScanStatus.PendingScan,
            UploadedDate = DateTime.UtcNow,
            UpdatedDate = DateTime.UtcNow
        };

        _context.Documents.Add(document);

        _context.DocumentActivityLogs.Add(new DocumentActivityLog
        {
            Document = document,
            UserId = uploaderUserId,
            ActivityType = DocumentActivityType.Upload,
            Timestamp = DateTime.UtcNow
        });

        await _context.SaveChangesAsync();

        await _scanQueueClient.EnqueueScanRequestAsync(document.DocumentId, document.FilePath);

        return document;
    }

    public Task<IReadOnlyList<Document>> GetMyDocumentsAsync(int userId, DocumentListQuery query) =>
        throw new NotImplementedException("Implemented in User Story 2 (Browse, Search, and Organize Documents).");

    public Task<IReadOnlyList<Document>> GetProjectDocumentsAsync(int userId, int projectId, DocumentListQuery query) =>
        throw new NotImplementedException("Implemented in User Story 2 (Browse, Search, and Organize Documents).");

    public Task<IReadOnlyList<Document>> GetSharedWithMeAsync(int userId) =>
        throw new NotImplementedException("Implemented in User Story 4 (Share Documents with Notifications).");

    public Task<IReadOnlyList<Document>> SearchAsync(int userId, string searchTerm) =>
        throw new NotImplementedException("Implemented in User Story 2 (Browse, Search, and Organize Documents).");

    public Task<Stream> DownloadAsync(int userId, int documentId) =>
        throw new NotImplementedException("Implemented in User Story 3 (Manage Document Lifecycle).");

    public Task<Document> UpdateMetadataAsync(int userId, int documentId, UpdateDocumentMetadataRequest request) =>
        throw new NotImplementedException("Implemented in User Story 3 (Manage Document Lifecycle).");

    public Task ReplaceFileAsync(int userId, int documentId, Stream newContent, string fileName, string contentType) =>
        throw new NotImplementedException("Implemented in User Story 3 (Manage Document Lifecycle).");

    public Task DeleteAsync(int userId, int documentId) =>
        throw new NotImplementedException("Implemented in User Story 3 (Manage Document Lifecycle).");

    public Task ShareAsync(int userId, int documentId, IReadOnlyList<int> recipientUserIds) =>
        throw new NotImplementedException("Implemented in User Story 4 (Share Documents with Notifications).");
}
