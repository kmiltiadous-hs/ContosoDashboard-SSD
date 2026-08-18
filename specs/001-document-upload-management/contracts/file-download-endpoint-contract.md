# Contract: File Download/Preview Endpoint

## Route

`GET /documents/{documentId:int}/download`
`GET /documents/{documentId:int}/preview` (PDF/JPEG/PNG only, per FR-017)

Implemented by `DocumentDownloadController` (new MVC controller — see
[research.md](../research.md) §4).

## Authorization

- Both routes require `[Authorize]` (any authenticated ContosoDashboard user).
- The controller MUST additionally call `IDocumentService` to verify the
  current user is authorized for the specific `documentId` (owner, current
  project member, share recipient, or Administrator) before streaming any
  bytes — route-level `[Authorize]` alone is insufficient (Constitution
  Principle II).
- Documents in `PendingScan` or `Rejected` state MUST return 404/403
  regardless of the caller's relationship to the document.

## Response

- **200 OK**: file bytes streamed with `Content-Type` set to the document's
  stored `FileType` and `Content-Disposition` set to `attachment` (download
  route) or `inline` (preview route, PDF/JPEG/PNG only).
- **403 Forbidden**: caller is authenticated but not authorized for this
  document.
- **404 Not Found**: document does not exist, or (to avoid confirming
  existence to unauthorized callers) is not accessible to the caller.
- **415 Unsupported Media Type**: preview requested for a non-previewable
  document type (e.g., a Word document) — client should fall back to download.

## Non-functional

- Every request logs a `DocumentActivityLog` entry (`Download`) via
  `IDocumentService`/`DocumentService`, satisfying FR-027.
- Response streaming reads from `IFileStorageService.DownloadAsync`, never
  accesses the filesystem directly from the controller.
