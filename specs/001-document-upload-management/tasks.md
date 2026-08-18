# Tasks: Document Upload and Management

**Input**: Design documents from `/specs/001-document-upload-management/`
**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/), [quickstart.md](./quickstart.md)

**Tests**: Included — the plan requires a new `ContosoDashboard.Tests` project (Constitution Principle IV: Test-First for Security-Relevant Logic) and a `ContosoDashboard.ScanFunction.Tests` project.

**Organization**: Tasks are grouped by user story (P1–P5 from spec.md) to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1–US5)
- Paths are relative to the repository root

## Path Conventions

Single ASP.NET Core project (`ContosoDashboard/`) extended in place, plus two new projects: `ContosoDashboard.ScanFunction/` (Azure Functions isolated worker) and `ContosoDashboard.Tests/` / `ContosoDashboard.ScanFunction.Tests/` (xUnit), per [plan.md](./plan.md) Project Structure.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Project/tooling initialization for the feature's new projects and dependencies

- [ ] T001 Create `ContosoDashboard.Tests` xUnit project in `ContosoDashboard.Tests/ContosoDashboard.Tests.csproj`, referencing `ContosoDashboard.csproj` and adding `Microsoft.EntityFrameworkCore.InMemory`
- [ ] T002 Create `ContosoDashboard.ScanFunction` Azure Functions isolated-worker (.NET 8) project in `ContosoDashboard.ScanFunction/ContosoDashboard.ScanFunction.csproj`, referencing `ContosoDashboard.csproj` (for shared `IMalwareScanner`, models, `ApplicationDbContext`) and adding `Microsoft.Azure.Functions.Worker.Extensions.Storage.Queues`
- [ ] T003 [P] Create `ContosoDashboard.ScanFunction.Tests` xUnit project in `ContosoDashboard.ScanFunction.Tests/ContosoDashboard.ScanFunction.Tests.csproj`, referencing `ContosoDashboard.ScanFunction.csproj`
- [ ] T004 [P] Add `Azure.Storage.Queues` NuGet package reference to `ContosoDashboard/ContosoDashboard.csproj`
- [ ] T005 [P] Add `DocumentStorage:RootPath`, `DocumentStorage:MaxFileSizeBytes`, `QueueStorage:ConnectionString`, `QueueStorage:ScanQueueName` settings to `ContosoDashboard/appsettings.json` and `ContosoDashboard/appsettings.Development.json` (Development pointing at `UseDevelopmentStorage=true` for Azurite)
- [ ] T006 [P] Create `ContosoDashboard.ScanFunction/host.json` and `ContosoDashboard.ScanFunction/local.settings.json` (Azurite connection string + SQL Server LocalDB connection string for local/offline dev)
- [ ] T007 [P] Document Azurite install/start steps (`npm install -g azurite` / `azurite --silent`) needed before local dev in `ContosoDashboard.ScanFunction/local.settings.json` comments or a short section appended to [quickstart.md](./quickstart.md)

**Checkpoint**: Solution builds with the two new empty projects wired into the existing solution/csproj references.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core models, storage/scan abstractions, and DI wiring that every user story depends on

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [ ] T008 [P] Create `Document` model (with `ScanStatus` enum, `RowVersion` concurrency token, `ContentHash`) in `ContosoDashboard/Models/Document.cs`
- [ ] T009 [P] Create `DocumentShare` model in `ContosoDashboard/Models/DocumentShare.cs`
- [ ] T010 [P] Create `DocumentActivityLog` model and `DocumentActivityType` enum in `ContosoDashboard/Models/DocumentActivityLog.cs`
- [ ] T011 Update `ContosoDashboard/Data/ApplicationDbContext.cs`: add `DbSet<Document>`, `DbSet<DocumentShare>`, `DbSet<DocumentActivityLog>`; configure `Restrict`/`Cascade` deletes; add indexes on `UploadedByUserId`, `ProjectId`, `ScanStatus`, composite `(UploadedByUserId, Title, ProjectId, ContentHash)`, and unique index on `DocumentShare(DocumentId, SharedWithUserId)` (depends on T008, T009, T010)
- [ ] T012 [P] Create `IFileStorageService` interface in `ContosoDashboard/Services/IFileStorageService.cs`
- [ ] T013 [P] Create `LocalFileStorageService` implementation (`System.IO`-based, generates `{userId}/{projectId|"personal"}/{guid}.{ext}` path before writing) in `ContosoDashboard/Services/LocalFileStorageService.cs` (depends on T012)
- [ ] T014 [P] Create `IMalwareScanner` interface and `ScanResult` record in `ContosoDashboard/Services/IMalwareScanner.cs` (shared with `ContosoDashboard.ScanFunction` via project reference)
- [ ] T015 [P] Create `StubMalwareScanner` implementation in `ContosoDashboard/Services/StubMalwareScanner.cs` (depends on T014)
- [ ] T016 [P] Create `IDocumentScanQueueClient` interface in `ContosoDashboard/Services/IDocumentScanQueueClient.cs`
- [ ] T017 Create `AzureDocumentScanQueueClient` implementation (`Azure.Storage.Queues`-based, sends `{documentId, storagePath}` JSON message) in `ContosoDashboard/Services/AzureDocumentScanQueueClient.cs` (depends on T004, T016)
- [ ] T018 [P] Create `UnauthorizedDocumentAccessException`, `DuplicateDocumentException`, `DocumentConcurrencyException` in `ContosoDashboard/Services/DocumentExceptions.cs`
- [ ] T019 Create `IDocumentService` interface (all methods per [contracts/document-service-contract.md](./contracts/document-service-contract.md)) in `ContosoDashboard/Services/IDocumentService.cs` (depends on T008, T009)
- [ ] T020 Register `IFileStorageService`, `IMalwareScanner`, `IDocumentScanQueueClient`, `IDocumentService` in DI, bind `DocumentStorage`/`QueueStorage` config sections, and add `AddControllers()`/`MapControllers()` in `ContosoDashboard/Program.cs` (depends on T012–T019)
- [ ] T021 Scaffold `ContosoDashboard.ScanFunction/DocumentScanFunction.cs` with a `[Function]` bound to `[QueueTrigger("document-scan-queue")]`, and configure its isolated-worker `Program.cs`/DI to resolve `ApplicationDbContext`, `IFileStorageService`, and `IMalwareScanner` from `local.settings.json`/`host.json` config (depends on T002, T006, T011–T015)

**Checkpoint**: Foundation ready — user story implementation can now begin.

---

## Phase 3: User Story 1 - Upload a Document (Priority: P1) 🎯 MVP

**Goal**: An employee uploads a file with metadata; it is stored securely, saved as `PendingScan`, and scanned asynchronously via the Azure Function before becoming `Available`.

**Independent Test**: Log in as an Employee, upload a supported file under 25 MB with required metadata, and confirm it appears with a success message, transitions from "Pending Scan" to "Available", and duplicate/oversized/unsupported-type/unauthorized-project uploads are rejected.

### Tests for User Story 1 ⚠️

- [ ] T022 [P] [US1] Unit tests for content-signature validation (magic-byte checks, FR-002) in `ContosoDashboard.Tests/Services/FileSignatureValidatorTests.cs`
- [ ] T023 [P] [US1] Unit tests for duplicate-upload detection (FR-031) in `ContosoDashboard.Tests/Services/DuplicateDetectionTests.cs`
- [ ] T024 [P] [US1] Unit tests for `LocalFileStorageService` (path generation before write, idempotent delete) in `ContosoDashboard.Tests/Storage/LocalFileStorageServiceTests.cs`
- [ ] T025 [P] [US1] Unit tests for `DocumentScanFunction` clean-scan and rejected-scan outcomes (`ScanStatus` update, file deletion + `ScanRejected` log on failure) in `ContosoDashboard.ScanFunction.Tests/DocumentScanFunctionTests.cs`

### Implementation for User Story 1

- [ ] T026 [US1] Implement file content-signature validation helper (PDF/Office/JPEG/PNG magic-byte checks) in `ContosoDashboard/Services/FileSignatureValidator.cs`
- [ ] T027 [US1] Implement `DocumentService.UploadAsync` — project-membership authorization, size/type validation via T026, `ContentHash` + duplicate check, save via `IFileStorageService`, insert `Document` (`PendingScan`), enqueue scan message via `IDocumentScanQueueClient` — in `ContosoDashboard/Services/DocumentService.cs` (depends on T011–T020, T026)
- [ ] T028 [US1] Implement `DocumentScanFunction` body — dequeue message, call `IMalwareScanner.ScanAsync`, update `Document.ScanStatus` to `Available`/`Rejected`, delete file + log `ScanRejected` on failure — in `ContosoDashboard.ScanFunction/DocumentScanFunction.cs` (depends on T021)
- [ ] T029 [US1] Build upload form (file picker, title/category/project/tags fields, client-side size/type hints, progress indicator, success/error messaging) in `ContosoDashboard/Pages/Documents.razor`
- [ ] T030 [US1] Wire upload form submit to `IDocumentService.UploadAsync`, surfacing `DuplicateDocumentException`/validation errors as user-facing messages, in `ContosoDashboard/Pages/Documents.razor` (depends on T027, T029)
- [ ] T031 [US1] Log `Upload` `DocumentActivityLog` entry on successful upload in `ContosoDashboard/Services/DocumentService.cs` (depends on T027)

**Checkpoint**: User Story 1 is fully functional and independently testable — uploads succeed, scan asynchronously via the Function, and rejected/duplicate/oversized files are handled correctly.
- Note: Include comprehensive error handling for file size limits and unsupported types
---

## Phase 4: User Story 2 - Browse, Search, and Organize Documents (Priority: P2)

**Goal**: Users can list, sort, filter, and search documents they're authorized to see ("My Documents", "Project Documents").

**Independent Test**: Upload documents as different users (via US1), then confirm each user's "My Documents"/"Project Documents" views show only authorized documents, with working sort/filter/search.

### Tests for User Story 2 ⚠️

- [ ] T032 [P] [US2] Unit tests for `GetMyDocumentsAsync`, `GetProjectDocumentsAsync`, and `SearchAsync` authorization filtering (only authorized/`Available` documents returned) in `ContosoDashboard.Tests/Services/DocumentQueryAuthorizationTests.cs`

### Implementation for User Story 2

- [ ] T033 [US2] Implement `DocumentService.GetMyDocumentsAsync` with `DocumentListQuery` sort (title/date/category/size) and filter (category/project/date range) support in `ContosoDashboard/Services/DocumentService.cs`
- [ ] T034 [US2] Implement `DocumentService.GetProjectDocumentsAsync` re-checking current project membership at call time in `ContosoDashboard/Services/DocumentService.cs`
- [ ] T035 [US2] Implement `DocumentService.SearchAsync` (title/description/tags/uploader/project, authorized-only results, target <2s per FR-029) in `ContosoDashboard/Services/DocumentService.cs`
- [ ] T036 [US2] Build "My Documents" list UI (title, category, upload date, size, project columns; sort/filter controls) in `ContosoDashboard/Pages/Documents.razor` (depends on T033)
- [ ] T037 [US2] Add "Project Documents" section to `ContosoDashboard/Pages/ProjectDetails.razor` (depends on T034)
- [ ] T038 [US2] Add search bar + results list to `ContosoDashboard/Pages/Documents.razor` (depends on T035)

**Checkpoint**: User Stories 1 AND 2 both work independently — documents can be uploaded, browsed, filtered, and searched with correct authorization scoping.

---

## Phase 5: User Story 3 - Manage Document Lifecycle (Priority: P3)

**Goal**: Owners (and authorized PMs/Admins) can download, preview, edit metadata, replace files, and delete documents.

**Independent Test**: Upload a document as its owner, then download/preview it, edit its metadata, replace its file, and delete it — verifying each action succeeds only for authorized users and concurrent edits conflict correctly.

### Tests for User Story 3 ⚠️

- [ ] T039 [P] [US3] Unit tests for `UpdateMetadataAsync` concurrency-conflict handling (`RowVersion` mismatch → `DocumentConcurrencyException`, FR-032) in `ContosoDashboard.Tests/Services/ConcurrencyConflictTests.cs`
- [ ] T040 [P] [US3] Unit tests for `DeleteAsync` authorization (owner/Project Manager/Administrator allow; others deny) in `ContosoDashboard.Tests/Services/DocumentServiceTests.cs`
- [ ] T041 [P] [US3] Contract tests for the download/preview endpoint (200/403/404/415 cases, `PendingScan`/`Rejected` always denied) in `ContosoDashboard.Tests/Controllers/DocumentDownloadControllerTests.cs`

### Implementation for User Story 3

- [ ] T042 [US3] Implement `DocumentService.DownloadAsync` (owner/project-member/share-recipient/Admin authorization; `Available`-only) in `ContosoDashboard/Services/DocumentService.cs`
- [ ] T043 [US3] Implement `DocumentService.UpdateMetadataAsync` with `RowVersion` concurrency check in `ContosoDashboard/Services/DocumentService.cs`
- [ ] T044 [US3] Implement `DocumentService.ReplaceFileAsync` (uploader-only, preserves metadata history) in `ContosoDashboard/Services/DocumentService.cs`
- [ ] T045 [US3] Implement `DocumentService.DeleteAsync` (owner, project's PM, or Admin) removing file + record in `ContosoDashboard/Services/DocumentService.cs`
- [ ] T046 [US3] Create `DocumentDownloadController` with `/documents/{documentId}/download` and `/documents/{documentId}/preview` routes per [contracts/file-download-endpoint-contract.md](./contracts/file-download-endpoint-contract.md) in `ContosoDashboard/Controllers/DocumentDownloadController.cs` (depends on T042)
- [ ] T047 [US3] Add edit/replace/delete actions and confirmation dialogs to `ContosoDashboard/Pages/Documents.razor` (depends on T043, T044, T045)
- [ ] T048 [US3] Add inline preview rendering (PDF/JPEG/PNG) using the preview route in `ContosoDashboard/Pages/Documents.razor` (depends on T046)
- [ ] T049 [US3] Log `Download`/`Edit`/`Delete` `DocumentActivityLog` entries in `ContosoDashboard/Services/DocumentService.cs` (depends on T042, T043, T045)

**Checkpoint**: User Stories 1–3 all work independently — full document lifecycle management is functional with correct authorization and concurrency handling.

---

## Phase 6: User Story 4 - Share Documents with Notifications (Priority: P4)

**Goal**: Owners share documents with specific users, who are notified and can view them under "Shared with Me".

**Independent Test**: Upload a document as User A, share it with User B, confirm User B gets a notification and sees it under "Shared with Me", while other users cannot access it.

### Tests for User Story 4 ⚠️

- [ ] T050 [P] [US4] Unit tests for `ShareAsync` (owner-only authorization, duplicate-share prevention) in `ContosoDashboard.Tests/Services/DocumentSharingTests.cs`
- [ ] T051 [P] [US4] Unit tests for `GetSharedWithMeAsync` filtering and IDOR denial for non-shared/non-member users in `ContosoDashboard.Tests/Services/DocumentSharingTests.cs`

### Implementation for User Story 4

- [ ] T052 [US4] Implement `DocumentService.ShareAsync` (uploader-only, unique `(DocumentId, SharedWithUserId)` enforcement) in `ContosoDashboard/Services/DocumentService.cs`
- [ ] T053 [US4] Implement `DocumentService.GetSharedWithMeAsync` in `ContosoDashboard/Services/DocumentService.cs`
- [ ] T054 [US4] Integrate with existing `NotificationService` to notify recipients in-app when a document is shared, in `ContosoDashboard/Services/DocumentService.cs` (depends on T052)
- [ ] T055 [US4] Build "Share Document" recipient-picker UI in `ContosoDashboard/Pages/Documents.razor` (depends on T052)
- [ ] T056 [US4] Build "Shared with Me" tab/view in `ContosoDashboard/Pages/Documents.razor` (depends on T053)
- [ ] T057 [US4] Log `Share` `DocumentActivityLog` entries in `ContosoDashboard/Services/DocumentService.cs` (depends on T052)

**Checkpoint**: User Stories 1–4 all work independently — sharing and notifications are functional with correct IDOR protection.

---

## Phase 7: User Story 5 - Integrate Documents with Tasks and Dashboard (Priority: P5)

**Goal**: Documents can be attached from a task detail page; the dashboard shows a "Recent Documents" widget and document count.

**Independent Test**: Upload a document directly from a task detail page, confirm it's associated with that task's project; confirm the dashboard shows the 5 most recent documents and an accurate count.

### Implementation for User Story 5

- [ ] T058 [US5] Add optional `Document` association to `TaskItem` (single FK or lightweight join table per [data-model.md](./data-model.md) deferred decision) in `ContosoDashboard/Models/TaskItem.cs` and `ContosoDashboard/Data/ApplicationDbContext.cs`
- [ ] T059 [US5] Add upload-from-task UI, wired to `IDocumentService.UploadAsync` with `ProjectId` derived from the task's project, in `ContosoDashboard/Pages/Tasks.razor` (depends on T058)
- [ ] T060 [US5] Display the task's attached documents list on the task detail view in `ContosoDashboard/Pages/Tasks.razor` (depends on T058)
- [ ] T061 [US5] Add "Recent Documents" widget (5 most recent uploads) to `ContosoDashboard/Pages/Index.razor`
- [ ] T062 [US5] Add document count summary card to `ContosoDashboard/Pages/Index.razor`
- [ ] T063 [US5] Notify project members (via `NotificationService`) when a new document is added to their project, in `ContosoDashboard/Services/DocumentService.cs`

**Checkpoint**: All user stories are independently functional — task/dashboard integration complete.

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: Improvements spanning multiple user stories

- [ ] T064 [P] Implement Administrator reporting (document type distribution, most active uploaders, access patterns, FR-028) in `ContosoDashboard/Services/DocumentService.cs` and a reporting page
- [ ] T065 [P] Verify/add indexes and pagination as needed so search/list pages meet the 2s target for 500 documents (FR-029, SC-003)
- [ ] T066 Security review pass: confirm no IDOR across download/preview/share routes, existing CSP/HSTS headers unaffected by new `Controllers`/`AddControllers()` registration, and Queue Storage messages carry no file content/PII
- [ ] T067 Verify Azure Storage Queue poison-message behavior (dequeue-count threshold, `document-scan-queue-poison`) is configured in `ContosoDashboard.ScanFunction/host.json`
- [ ] T068 Run through all [quickstart.md](./quickstart.md) validation scenarios end-to-end and fix any gaps found

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately
- **Foundational (Phase 2)**: Depends on Setup completion — BLOCKS all user stories
- **User Stories (Phase 3+)**: All depend on Foundational phase completion
  - US1 (P1) has no dependency on other stories — build first (MVP)
  - US2–US5 depend only on Foundational + US1 existing (documents to browse/manage/share/attach), not on each other's implementation details
- **Polish (Phase 8)**: Depends on all desired user stories being complete

### User Story Dependencies

- **US1 (P1)**: Can start after Foundational — no dependency on other stories
- **US2 (P2)**: Can start after Foundational; needs documents from US1 to be meaningful but does not require US1 code changes
- **US3 (P3)**: Can start after Foundational; builds on `Document`/`IDocumentService` from Foundational, independently testable once a document exists
- **US4 (P4)**: Can start after Foundational; independently testable once a document exists
- **US5 (P5)**: Can start after Foundational; integrates with existing Tasks/Index pages

### Within Each User Story

- Tests MUST be written and FAIL before implementation
- Models/interfaces before services
- Services before controllers/UI
- Story complete before moving to next priority (if working sequentially)

### Parallel Opportunities

- All Setup tasks marked [P] (T003–T007) can run in parallel after T001/T002
- All Foundational [P] tasks (T008–T010, T012, T014, T016, T018) can run in parallel
- Once Foundational completes, US1–US5 can be staffed and worked in parallel by different developers (though US2–US5 need documents to exist for full end-to-end testing)
- All [P] test tasks within a story can run in parallel

---

## Parallel Example: User Story 1

```bash
# Launch all tests for User Story 1 together:
Task: "Unit tests for content-signature validation in ContosoDashboard.Tests/Services/FileSignatureValidatorTests.cs"
Task: "Unit tests for duplicate-upload detection in ContosoDashboard.Tests/Services/DuplicateDetectionTests.cs"
Task: "Unit tests for LocalFileStorageService in ContosoDashboard.Tests/Storage/LocalFileStorageServiceTests.cs"
Task: "Unit tests for DocumentScanFunction outcomes in ContosoDashboard.ScanFunction.Tests/DocumentScanFunctionTests.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup
2. Complete Phase 2: Foundational (CRITICAL — blocks all stories; includes the Azure Function scan job scaffolding)
3. Complete Phase 3: User Story 1
4. **STOP and VALIDATE**: Test upload + async scan independently (quickstart.md §1)
5. Deploy/demo if ready

### Incremental Delivery

1. Complete Setup + Foundational → Foundation ready (models, storage, scan queue/Function, DI)
2. Add US1 (Upload) → Test independently → Deploy/Demo (MVP!)
3. Add US2 (Browse/Search) → Test independently → Deploy/Demo
4. Add US3 (Lifecycle) → Test independently → Deploy/Demo
5. Add US4 (Sharing) → Test independently → Deploy/Demo
6. Add US5 (Task/Dashboard integration) → Test independently → Deploy/Demo
7. Each story adds value without breaking previous stories

### Parallel Team Strategy

With multiple developers:

1. Team completes Setup + Foundational together
2. Once Foundational is done:
   - Developer A: US1 (Upload) + the `DocumentScanFunction` scan logic (T028)
   - Developer B: US2 (Browse/Search)
   - Developer C: US3 (Lifecycle)
   - Developer D: US4 (Sharing) and US5 (Task/Dashboard) once documents exist
3. Stories complete and integrate independently via the shared `IDocumentService`/`Document` model
