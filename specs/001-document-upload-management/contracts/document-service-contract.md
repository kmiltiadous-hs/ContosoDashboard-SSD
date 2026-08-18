# Contract: Document Service & Storage Abstractions

Internal service-layer contracts exposed to Pages/Controllers within the
ContosoDashboard application. These are the abstraction boundaries the
stakeholder requirements explicitly call for (`IFileStorageService`) plus the
service that enforces authorization for every document operation.

## `IFileStorageService`

```csharp
public interface IFileStorageService
{
    Task<string> UploadAsync(Stream fileStream, string fileName, string contentType);
    Task DeleteAsync(string filePath);
    Task<Stream> DownloadAsync(string filePath);
    Task<string> GetUrlAsync(string filePath, TimeSpan expiration);
}
```

- **UploadAsync**: generates a unique storage path (`{userId}/{projectId|"personal"}/{guid}.{ext}`)
  BEFORE writing, writes the stream to that path, and returns the relative
  path to persist on the `Document` record. MUST NOT use the caller-supplied
  `fileName` in the returned path (FR-008).
- **DeleteAsync**: removes the file at the given relative path; MUST be
  idempotent (no error if already absent, to tolerate retry after partial
  failure).
- **DownloadAsync**: returns a readable stream for the given relative path;
  callers (the download controller) are responsible for authorization checks
  before calling this method — this method performs no authorization itself.
- **GetUrlAsync**: reserved for a future signed-URL-capable implementation
  (e.g., Azure Blob SAS); the local implementation may throw
  `NotSupportedException` or return the authorized controller route.
- **Implementations**: `LocalFileStorageService` (training, `System.IO`-based)
  today; `AzureBlobStorageService` swappable later via DI configuration with
  no changes to callers.

## `IMalwareScanner`

```csharp
public interface IMalwareScanner
{
    Task<ScanResult> ScanAsync(Stream content, CancellationToken cancellationToken);
}

public record ScanResult(bool IsClean, string? ThreatName = null);
```

- Called by the background scan queue after a document is saved in
  `PendingScan` state.
- **Implementations**: `StubMalwareScanner` (training — always returns
  `IsClean = true`, or a configurable override for test/demo purposes) today;
  a real AV-backed implementation swappable later via DI, with no changes to
  `DocumentService` or the scan queue.

## `IDocumentService`

```csharp
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
```

**Authorization contract** (Constitution Principle II — enforced inside this
service, not in Pages/UI):

| Method | Authorization rule |
|---|---|
| `UploadAsync` | Caller must be an authenticated user; if `ProjectId` is set, caller MUST be a member of that project (or its Project Manager) |
| `GetMyDocumentsAsync` | Returns only documents where `UploadedByUserId == userId` |
| `GetProjectDocumentsAsync` | Caller MUST currently be a member of `projectId`, re-checked at call time |
| `GetSharedWithMeAsync` | Returns only documents with an active `DocumentShare` for `userId` |
| `SearchAsync` | Results filtered server-side to documents the caller owns, is a project member for, or has been shared |
| `DownloadAsync` | Caller MUST be the uploader, a current member of the document's project, a share recipient, or an Administrator; document MUST be `Available` (not `PendingScan`/`Rejected`) |
| `UpdateMetadataAsync` | Caller MUST be the uploader; concurrency token mismatch → conflict error (no silent overwrite) |
| `ReplaceFileAsync` | Caller MUST be the uploader |
| `DeleteAsync` | Caller MUST be the uploader, OR a Project Manager of the document's project, OR an Administrator |
| `ShareAsync` | Caller MUST be the uploader |

**Error contract**: authorization failures throw a distinct
`UnauthorizedDocumentAccessException` (mapped to HTTP 403 at the boundary);
duplicate uploads throw `DuplicateDocumentException` (mapped to a clear
validation error); concurrent-edit conflicts throw
`DocumentConcurrencyException` (mapped to a clear "reload and retry" error).
