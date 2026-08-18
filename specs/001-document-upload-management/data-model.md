# Data Model: Document Upload and Management

## Document

Represents an uploaded file's metadata and lifecycle state.

| Field | Type | Notes |
|---|---|---|
| DocumentId | int (PK, identity) | Integer key for consistency with `User`/`Project` (per stakeholder constraint) |
| Title | string(200), required | User-supplied title |
| Description | string(2000), optional | |
| Category | string(50), required | Text value: "Project Documents", "Team Resources", "Personal Files", "Reports", "Presentations", "Other" (per stakeholder constraint: text, not enum) |
| Tags | string(500), optional | Comma-separated free-form tags (simple approach; no separate tag table needed at this scale) |
| UploadedByUserId | int (FK → User), required | `Restrict` delete, consistent with existing FK conventions |
| ProjectId | int? (FK → Project), optional | `Restrict` delete; null = personal document |
| FileName | string(260) | Original user-supplied filename, stored for display only — never used to build a file path |
| FilePath | string(400) | System-generated relative path: `{userId}/{projectId|"personal"}/{guid}.{ext}` |
| FileSizeBytes | long | |
| FileType | string(255) | MIME type; 255 chars to accommodate long Office MIME strings (per stakeholder constraint) |
| ContentHash | string(64) | SHA-256 hex digest of file content, used for duplicate detection (FR-031) |
| ScanStatus | enum: PendingScan, Available, Rejected | Default `PendingScan` on upload |
| UploadedDate | DateTime (UTC) | Auto-captured |
| UpdatedDate | DateTime (UTC) | Auto-updated on metadata edit or file replace |
| RowVersion | byte[] (`[Timestamp]`/`rowversion`) | Optimistic concurrency token (FR-032) |

**Relationships**: many-to-one → `User` (uploader), many-to-one → `Project`
(optional), one-to-many → `DocumentShare`, one-to-many → `DocumentActivityLog`,
optionally referenced by `TaskItem` (task attachment — see Task Integration
below).

**Validation rules**:
- Title, Category, UploadedByUserId, FileType, FilePath required.
- FileSizeBytes MUST be > 0 and ≤ 25 MB (FR-003).
- FileType/content MUST match the accepted allow-list (FR-002).
- Duplicate check (title + ProjectId scope + UploadedByUserId + ContentHash)
  MUST be enforced before insert (FR-031).
- Only `Available` documents are visible in browse/search/download/preview/
  share flows; `PendingScan`/`Rejected` documents are excluded except from the
  uploader's own "My Documents" view (shown with a status badge).

**State transitions**:

```text
(upload) --> PendingScan --[scan succeeds]--> Available
                          --[scan fails]-----> Rejected (file removed from storage)
```

`Available` documents can transition back to a rejected/removed state only via
explicit user deletion (FR-020), not via re-scanning.

## DocumentShare

Represents a sharing relationship granting a specific recipient user access to
a document they did not upload.

| Field | Type | Notes |
|---|---|---|
| DocumentShareId | int (PK, identity) | |
| DocumentId | int (FK → Document), required | `Cascade` delete (a share is meaningless once its document is gone) |
| SharedWithUserId | int (FK → User), required | `Restrict` delete |
| SharedByUserId | int (FK → User), required | `Restrict` delete — the document owner who created the share |
| SharedDate | DateTime (UTC) | |

**Relationships**: many-to-one → `Document`, many-to-one → `User` (recipient),
many-to-one → `User` (sharer).

**Validation rules**:
- `(DocumentId, SharedWithUserId)` MUST be unique (no duplicate shares of the
  same document to the same user).
- `SharedByUserId` MUST be the document's `UploadedByUserId` (only the owner
  can share, per FR-021) — enforced in `DocumentService`, not at the DB layer.

## DocumentActivityLog

Append-only audit trail of document-related activity, supporting FR-027
(audit trail) and FR-028 (admin reporting).

| Field | Type | Notes |
|---|---|---|
| DocumentActivityLogId | int (PK, identity) | |
| DocumentId | int (FK → Document), required | `Cascade` delete acceptable here (log tied to document's lifetime; report queries can also snapshot FileType/Category into log if needed for post-delete reporting — deferred decision, see Assumptions) |
| UserId | int (FK → User), required | `Restrict` delete |
| ActivityType | enum: Upload, Download, Edit, Delete, Share, ScanRejected | |
| Timestamp | DateTime (UTC) | |

**Relationships**: many-to-one → `Document`, many-to-one → `User`.

## Enums

- `DocumentScanStatus`: `PendingScan`, `Available`, `Rejected`
- `DocumentActivityType`: `Upload`, `Download`, `Edit`, `Delete`, `Share`, `ScanRejected`

## Existing entities touched

- **User** ([Models/User.cs](../../ContosoDashboard/Models/User.cs)): no schema
  change; referenced as uploader/recipient/sharer/role source.
- **Project** ([Models/Project.cs](../../ContosoDashboard/Models/Project.cs)):
  no schema change; referenced for optional document association and
  membership-based authorization via existing `ProjectMember`.
- **TaskItem** ([Models/TaskItem.cs](../../ContosoDashboard/Models/TaskItem.cs)):
  add an optional `DocumentId` join (or a lightweight `TaskDocument` link
  table, if a task can have multiple attached documents) to support FR-024;
  exact shape (single FK vs. join table) is a task-level implementation detail
  deferred to `/speckit.tasks`.

## `ApplicationDbContext` changes

- Add `DbSet<Document> Documents`, `DbSet<DocumentShare> DocumentShares`,
  `DbSet<DocumentActivityLog> DocumentActivityLogs`.
- `OnModelCreating`: configure `Restrict`/`Cascade` delete behaviors as above,
  add indexes on `Document.UploadedByUserId`, `Document.ProjectId`,
  `Document.ScanStatus`, and a composite index on
  `(UploadedByUserId, Title, ProjectId, ContentHash)` for duplicate-check
  performance; unique index on `DocumentShare(DocumentId, SharedWithUserId)`.
