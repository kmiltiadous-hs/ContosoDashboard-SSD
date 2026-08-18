# Implementation Plan: Document Upload and Management

**Branch**: `001-document-upload-management` | **Date**: 2026-08-17 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/001-document-upload-management/spec.md`

## Summary

Add secure document upload, browsing/search, lifecycle management, and sharing to
ContosoDashboard, integrated with existing Tasks/Projects/Dashboard. Files are
stored on the local filesystem outside `wwwroot` behind an `IFileStorageService`
abstraction (swappable for Azure Blob Storage later); malware scanning runs
asynchronously as a background job — the web app enqueues a message to an
Azure Storage Queue after upload, and a Queue Storage-triggered Azure Function
(`DocumentScanFunction`) dequeues it, runs the scan behind the same
`IMalwareScanner` abstraction (stub scanner for training), and updates the
document's scan status; all access is authorized at the service layer per
current project/document ownership, evaluated on every request (no caching),
per the Constitution's security-first principles.

## Technical Context

**Language/Version**: C# 12 / .NET 8 (existing `ContosoDashboard.csproj`, `net8.0`)

**Primary Dependencies**: ASP.NET Core 8 Blazor Server + Razor Pages, EF Core 8
(`Microsoft.EntityFrameworkCore.SqlServer`), ASP.NET Core Cookie Authentication
(existing mock auth), ASP.NET Core MVC Controllers (new — needed to serve files
outside `wwwroot` with authorization checks, not currently used in this project),
`Azure.Storage.Queues` SDK (new — enqueues scan-request messages from the web
app), Azure Functions (isolated worker, .NET 8) with a Queue Storage trigger
(new `ContosoDashboard.ScanFunction` project — hosts the background virus-scan
job), **Azurite** storage emulator (new — local, offline-capable Queue Storage
for dev/training so no live Azure connectivity is required)

**Storage**: SQL Server via existing `ApplicationDbContext` (EF Core,
`Database.EnsureCreated()`) for metadata; local filesystem (new
`App_Data/uploads/{userId}/{projectId|"personal"}/{guid}.{ext}` directory,
outside `wwwroot`) for file content, behind `IFileStorageService`; an Azure
Storage Queue (`document-scan-queue`, backed by Azurite locally) for
decoupling upload from virus scanning — carries only `DocumentId` +
`StoragePath`, never file content

**Testing**: No test project exists yet in this repo. This feature adds a new
xUnit test project (`ContosoDashboard.Tests`) using
`Microsoft.EntityFrameworkCore.InMemory` (or SQLite in-memory) for service-layer
unit/integration tests — required by Constitution Principle IV (Test-First for
Security-Relevant Logic) since this feature is authorization- and file-handling-heavy

**Target Platform**: Self-hosted ASP.NET Core web app (existing dev/training
deployment target — Windows/Linux, Kestrel)

**Project Type**: Web application — single ASP.NET Core project (Blazor Server +
Razor Pages + new MVC controller), matching existing structure; no
frontend/backend split

**Performance Goals**: Upload validation+storage completes within 30s for files
up to 25MB (FR-030); document list/search pages return within 2s for up to 500
documents (FR-029, SC-003)

**Constraints**: Must run fully offline in the training environment (the
Azurite emulator substitutes for a live Azure Storage account — no real cloud
connectivity required to develop/run/test); must reuse existing mock cookie
authentication and role claims; must preserve/strengthen existing security
headers (CSP, HSTS, etc.) in `Program.cs`; malware scanning and file storage
MUST be implemented behind interfaces so a production implementation (real AV
engine, `AzureBlobStorageService`) can be substituted via DI without touching
business logic, controllers, or UI; the Queue Storage message MUST carry only
an identifier/path (never raw file bytes or PII) since queue contents are
logged/retried

**Scale/Scope**: Internal training application; up to ~500 documents per
list/search view; single-instance web app deployment; the background scan job
runs as a separate Azure Function app (Consumption plan) that autoscales with
queue depth independently of the web app

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | How this feature satisfies it |
|---|---|
| I. Secure by Design | File type accepted via allow-list validated against actual file content (magic-byte signature check), not just extension/client MIME type (FR-002); all metadata inputs validated server-side before persistence |
| II. Authorization Enforced at the Service Layer | `DocumentService` (not pages/UI) checks ownership/project-membership/role before every view/download/preview/edit/delete/share operation, re-evaluated per request (FR-023) |
| III. Secure Data & File Handling | Files stored under `App_Data/uploads` (outside `wwwroot`), served only via an `[Authorize]`-protected MVC controller endpoint; GUID-based storage filenames, never user-supplied names, used in paths (FR-007, FR-008, FR-009); the async scan job runs as an isolated Azure Function, so a scan never blocks a request thread and a document stays `PendingScan` (not downloadable/previewable/shareable) until the Function confirms it clean |
| IV. Test-First for Security-Relevant Logic | New `ContosoDashboard.Tests` xUnit project created in this feature; authorization (positive/negative), duplicate-detection, and concurrency-conflict logic get tests before/alongside implementation; `DocumentScanFunction` gets its own test project exercising clean/rejected scan outcomes |
| V. Least Privilege & Auditability | New `DocumentActivityLog` table records uploads/downloads/deletes/shares/scan-rejections (who/what/when) to support FR-027 audit trail and FR-028 admin reporting; services request only the DbContext access they need; the Function app connects to SQL Server and Storage Queue using its own least-privilege connection string/managed identity, separate from the web app's credentials |

**Result**: PASS — no violations requiring Complexity Tracking entries. Adding a
test project, an MVC controller, and a separate Azure Function project for the
background scan job are net-new additions that satisfy existing principles;
they are not deviations from them.

## Project Structure

### Documentation (this feature)

```text
specs/001-document-upload-management/
├── plan.md              # This file (/speckit.plan command output)
├── research.md          # Phase 0 output (/speckit.plan command)
├── data-model.md        # Phase 1 output (/speckit.plan command)
├── quickstart.md        # Phase 1 output (/speckit.plan command)
├── contracts/           # Phase 1 output (/speckit.plan command)
│   ├── document-service-contract.md
│   └── file-download-endpoint-contract.md
├── checklists/
│   └── requirements.md
└── tasks.md             # Phase 2 output (/speckit.tasks command - NOT created by /speckit.plan)
```

### Source Code (repository root)

```text
ContosoDashboard/                          # existing single ASP.NET Core project
├── Controllers/                           # NEW — MVC controllers (not previously used)
│   └── DocumentDownloadController.cs      # authorized file streaming endpoint
├── Data/
│   └── ApplicationDbContext.cs            # MODIFIED — add DbSets + OnModelCreating config
├── Models/
│   ├── Document.cs                        # NEW
│   ├── DocumentShare.cs                   # NEW
│   └── DocumentActivityLog.cs             # NEW
├── Services/
│   ├── IFileStorageService.cs             # NEW — UploadAsync/DeleteAsync/DownloadAsync/GetUrlAsync
│   ├── LocalFileStorageService.cs         # NEW — System.IO-based implementation
│   ├── IMalwareScanner.cs                 # NEW — pluggable scan interface, shared with the Function project
│   ├── StubMalwareScanner.cs              # NEW — training/offline implementation
│   ├── IDocumentScanQueueClient.cs        # NEW — enqueues a scan-request message after upload
│   ├── AzureDocumentScanQueueClient.cs    # NEW — Azure.Storage.Queues-based implementation (Azurite locally)
│   ├── IDocumentService.cs                # NEW
│   └── DocumentService.cs                 # NEW — upload/browse/search/manage/share orchestration + authorization
├── Pages/
│   ├── Documents.razor                    # NEW — "My Documents" + "Shared with Me" views
│   ├── ProjectDetails.razor               # MODIFIED — add Project Documents section
│   ├── Tasks.razor                        # MODIFIED — add attach/upload-from-task
│   └── Index.razor                        # MODIFIED — add Recent Documents widget + count card
├── Program.cs                             # MODIFIED — register new services, add AddControllers()/MapControllers()
└── appsettings.json                       # MODIFIED — add DocumentStorage:RootPath, size/type limits,
                                            #            QueueStorage:ConnectionString, QueueStorage:ScanQueueName

ContosoDashboard.ScanFunction/             # NEW Azure Functions project (isolated worker, .NET 8)
├── ContosoDashboard.ScanFunction.csproj
├── host.json
├── local.settings.json                    # local dev — points at Azurite + local SQL Server
└── DocumentScanFunction.cs                # NEW — [Function] with a Queue Storage trigger on
                                            #        "document-scan-queue"; resolves IMalwareScanner,
                                            #        updates Document.ScanStatus via ApplicationDbContext,
                                            #        deletes the file + logs ScanRejected on failure

ContosoDashboard.Tests/                    # NEW xUnit test project
├── ContosoDashboard.Tests.csproj
├── Services/
│   ├── DocumentServiceTests.cs            # authorization positive/negative cases
│   ├── DuplicateDetectionTests.cs
│   └── ConcurrencyConflictTests.cs
└── Storage/
    └── LocalFileStorageServiceTests.cs

ContosoDashboard.ScanFunction.Tests/       # NEW xUnit test project
└── DocumentScanFunctionTests.cs           # clean-scan and rejected-scan outcome coverage
```

**Structure Decision**: Single ASP.NET Core project (existing `ContosoDashboard`)
extended in place, following its established Models/Services/Pages/Data
conventions — no frontend/backend split, since this is Blazor Server. The
async malware scan is factored out into its own `ContosoDashboard.ScanFunction`
Azure Functions project so it runs and scales independently of the web app,
communicating only via the `document-scan-queue` Storage Queue and the shared
SQL Server database — no direct reference between the two projects beyond the
shared `IMalwareScanner`/model types. Two new top-level test projects are added
(no test project currently exists in the repo), satisfying Constitution
Principle IV.

## Background Virus Scan Job (Azure Functions + Queue Storage)

The malware scan is decoupled from the upload request/response cycle by
running as a separate Azure Function triggered off an Azure Storage Queue:

1. **Upload (web app)**: `DocumentService.UploadAsync` saves the file via
   `IFileStorageService`, inserts the `Document` row with
   `ScanStatus = PendingScan`, then calls
   `IDocumentScanQueueClient.EnqueueScanRequestAsync(documentId, storagePath)`.
   This sends a small JSON message (`{ "documentId": ..., "storagePath": ... }`
   — no file bytes, no PII) to the `document-scan-queue` Azure Storage Queue
   and returns immediately, so the upload response is not blocked on scanning
   (FR-030).
2. **Trigger (Function)**: `ContosoDashboard.ScanFunction` defines
   `DocumentScanFunction` with a `[QueueTrigger("document-scan-queue")]`
   binding. The Functions host dequeues the message and invokes the function
   automatically as messages arrive — no polling code required.
3. **Scan execution**: The function resolves `IMalwareScanner` (the same
   interface used by the web app, registered via the isolated-worker DI
   container) and calls `ScanAsync` against the file read through
   `IFileStorageService`.
4. **Result handling**: On a clean result, the function sets
   `Document.ScanStatus = Available` via `ApplicationDbContext` and logs an
   `Upload`-adjacent activity entry. On a detected/failed result, it deletes
   the stored file, sets `ScanStatus = Rejected`, and logs a `ScanRejected`
   `DocumentActivityLog` entry (FR-004) — matching the state machine in
   [data-model.md](./data-model.md).
5. **Reliability**: Azure Storage Queues give at-least-once delivery with a
   visibility timeout — if the function crashes mid-scan, the message
   reappears and is retried automatically. After the configured dequeue count
   is exceeded, the Functions poison-message handling moves it to
   `document-scan-queue-poison` rather than retrying forever; a document stuck
   there stays `PendingScan` and should be surfaced to an Administrator (future
   enhancement, not required for this feature's scope).
6. **Local/offline development**: The Azurite emulator provides Queue Storage
   locally (`UseDevelopmentStorage=true` connection string), and the Function
   app runs via Azure Functions Core Tools (`func start`) alongside the web
   app — both configured through `appsettings.json` /
   `local.settings.json`, with no dependency on a live Azure subscription
   during training/dev.

## Complexity Tracking

*No entries — Constitution Check passed without violations.*

