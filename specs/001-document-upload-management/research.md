# Research: Document Upload and Management

## 1. Content-based file type validation

**Decision**: Validate accepted file types (PDF, Word, Excel, PowerPoint, plain
text, JPEG, PNG) by checking the file's leading byte signature ("magic
numbers") after upload, in addition to the client-supplied extension/MIME type.

**Rationale**: FR-002 and the Constitution's Secure by Design principle require
validating actual content, not trusting client-supplied metadata, to block
disguised/renamed executables. Signature checking (e.g., `%PDF-` for PDF,
`PK\x03\x04` for the ZIP-based Office Open XML formats, `\xFF\xD8\xFF` for
JPEG, `\x89PNG` for PNG) is well-known and requires no external dependency,
keeping the app fully offline-capable.

**Alternatives considered**: A third-party MIME-detection library was
considered but rejected — it would add a dependency with no offline benefit
over a small internal signature-check helper, and the constraint list of
accepted types is small and stable.

## 2. Malware scanning strategy

**Decision**: Define an `IMalwareScanner` interface with a single method
(e.g., `ScanAsync(Stream content) -> ScanResult`). Provide a `StubMalwareScanner`
for the offline/training implementation that always returns "clean" (optionally
configurable to simulate a "detected" result for demo/testing purposes). Scan
execution is queued and runs asynchronously after the file and initial
"Pending Scan" metadata record are saved (per Clarifications).

**Rationale**: Resolves the clarified requirement that scanning must work
offline while still being swappable for a real AV engine (e.g., ClamAV, an
OS-level AV API) in production, without touching upload/business logic.

**Alternatives considered**: Synchronous pre-storage scanning was the original
stakeholder-doc suggestion, but was superseded by the clarification session's
decision to scan asynchronously post-save to avoid blocking the upload
response on the scan.

## 3. Background scan execution model

**Decision**: Process the malware scan out-of-process as an Azure Function
(`DocumentScanFunction`), triggered by a message on an Azure Storage Queue
(`document-scan-queue`). After `DocumentService` saves the file and the
"Pending Scan" metadata record, it enqueues a small JSON message (`DocumentId`,
`StoragePath`) to the queue via `Azure.Storage.Queues`. The Queue Storage
trigger fires the Function App, which resolves `IMalwareScanner` (same
interface used elsewhere in the codebase), scans the stored file, and updates
`Document.ScanStatus` (and removes the file + logs a `ScanRejected` activity on
failure) directly against the shared SQL Server database via
`ApplicationDbContext`. For the offline/training environment, the **Azurite**
storage emulator provides a local, no-cloud-connectivity implementation of the
same Queue Storage APIs, and the Function App runs locally via the Azure
Functions Core Tools — so the design satisfies the offline constraint while
matching the production architecture exactly (no code branching between
environments, only connection-string configuration differs).

**Rationale**: A queue-trigger Function decouples scan execution from the web
app process — scans no longer compete with request-handling threads/memory,
scan throughput can scale independently (Functions Consumption plan autoscaling
per queue depth), and a crashed/restarted web app no longer loses in-flight
scans (the queue message remains until processed, giving at-least-once
delivery and automatic retry/poison-queue handling). This is the recommended
Azure pattern for decoupled async processing and keeps `IMalwareScanner` as the
single pluggable scan abstraction, satisfying the requirement that a real AV
engine can be substituted without touching upload/business logic.

**Alternatives considered**: An in-process `BackgroundService` backed by a
`System.Threading.Channels.Channel<int>` was the original decision — simple and
dependency-free, but scans are lost on process restart/crash and cannot scale
independently of the web app. It remains a fallback for environments with no
Azurite/Functions tooling available, but the Azure Functions + Queue Storage
trigger is now the primary design given it is required by this plan and
matches the production target architecture described in
[data-model.md](./data-model.md).

## 4. Serving files stored outside `wwwroot`

**Decision**: Add a new ASP.NET Core MVC controller (`DocumentDownloadController`)
with `[Authorize]` plus an explicit per-document authorization check (via
`IDocumentService`) before streaming file bytes via `FileStreamResult`. This is
the first MVC controller in the project (previously Blazor Server + Razor
Pages only), so `Program.cs` needs `AddControllers()`/`MapControllers()`.

**Rationale**: FR-007 requires files to be served only through an authorized
access path, never a static URL. Blazor Server doesn't have a built-in
per-request file-streaming primitive as clean as an MVC controller action, and
the stakeholder doc explicitly calls out "controller endpoints... enables
authorization checks."

**Alternatives considered**: A Razor Page handler (`OnGetDownload`) could serve
the same purpose, but a dedicated controller keeps download/preview endpoints
cleanly separate from page rendering and matches common ASP.NET Core practice
for binary content endpoints.

## 5. Duplicate upload detection (FR-031)

**Decision**: Compute a SHA-256 hash of the uploaded file content at upload
time and store it on the `Document` record. Before accepting a new upload,
check for an existing, non-rejected document owned by the same user with the
same title, the same project scope (or personal), and the same content hash;
reject as duplicate if found.

**Rationale**: Directly implements the clarified duplicate rule
(title+project+uploader+content) without relying on filename (which is never
trusted per FR-008).

**Alternatives considered**: Comparing raw file bytes was rejected as
inefficient for repeated checks; comparing only title+project (ignoring
content) was rejected because it would block legitimate re-uploads of a
different file under the same title.

## 6. Concurrency conflict detection (FR-032)

**Decision**: Add an EF Core concurrency token (`byte[] RowVersion` with
`[Timestamp]`, or SQL Server `rowversion` column) to `Document`. Metadata edits
include the token; EF Core throws `DbUpdateConcurrencyException` on a stale
save, which the service layer catches and surfaces as a conflict error to the
second saver.

**Rationale**: This is the standard, built-in EF Core/SQL Server mechanism for
optimistic concurrency and directly satisfies the clarified "reject the second
save with a conflict error" behavior without custom locking logic.

**Alternatives considered**: Application-level "last modified timestamp"
comparison was considered but rejected in favor of the database-enforced
`rowversion` mechanism, which is more reliable under concurrent requests.

## 7. Audit trail and reporting data (FR-027, FR-028)

**Decision**: Add a `DocumentActivityLog` table capturing `UserId`,
`DocumentId`, `ActivityType` (Upload/Download/Delete/Share/etc.), and
`Timestamp` for every document-related action, queryable by Administrators for
reporting (most uploaded types, most active uploaders, access patterns).

**Rationale**: FR-028's reporting requirements need structured, queryable data;
plain application logs (`ILogger`) are useful for operational debugging but are
not a practical source for aggregate reports. This also satisfies Constitution
Principle V (Least Privilege & Auditability).

**Alternatives considered**: Relying solely on `ILogger` structured logs was
rejected because it would require external log aggregation tooling not present
in this offline training environment.

## 8. Testing framework

**Decision**: Add a new `ContosoDashboard.Tests` xUnit project using
`Microsoft.EntityFrameworkCore.InMemory` for fast service-layer tests
(authorization allow/deny paths, duplicate detection, concurrency conflicts).

**Rationale**: No test project exists in the repository today. Constitution
Principle IV (Test-First for Security-Relevant Logic, NON-NEGOTIABLE) requires
automated tests covering both allowed and denied access paths for this
authorization- and file-handling-heavy feature.

**Alternatives considered**: Using a real SQL Server LocalDB instance for tests
was considered but rejected for the default fast unit-test suite (adds setup
friction and slows iteration); it remains an option for a smaller set of
integration tests if EF Core InMemory proves insufficient for a given scenario
(e.g., unique-index or concurrency-token behavior that InMemory doesn't fully
emulate — SQLite in-memory is the fallback for those specific cases).
