# Quickstart: Validating Document Upload and Management

## Prerequisites

- .NET 8 SDK installed.
- SQL Server LocalDB available (existing project default connection string in
  [appsettings.json](../../ContosoDashboard/appsettings.json)).
- Clean database state before first-time testing (per stakeholder doc):

  ```powershell
  sqllocaldb stop mssqllocaldb
  sqllocaldb delete mssqllocaldb
  # Database will be recreated automatically on next run
  ```

## Setup

```powershell
cd ContosoDashboard
dotnet restore
dotnet run
```

The app seeds four mock users (Administrator, Project Manager, Team Lead,
Employee) and one sample project — see
[ApplicationDbContext.cs](../../ContosoDashboard/Data/ApplicationDbContext.cs).
Log in via the mock login dropdown at `/login` as any seeded user.

## Validation Scenarios

Run these after implementation to confirm the feature meets its spec
([spec.md](./spec.md)) and contracts ([contracts/](./contracts)):

### 1. Upload (User Story 1 / P1)

1. Log in as "Ni Kang" (Employee). Navigate to the Documents page.
2. Upload a PDF under 25 MB with a title and category. Confirm a success
   message and that the document shows status "Pending Scan" briefly, then
   "Available" (stub scanner clears it automatically).
3. Attempt to upload a 30 MB file → expect a clear size-limit error, no
   document created.
4. Attempt to upload an unsupported type (e.g., `.exe` renamed to `.pdf`) →
   expect rejection based on content signature, not extension.
5. Re-upload the exact same file (same title/project/content) → expect a
   duplicate-upload rejection (FR-031).

### 2. Browse, Search, Organize (P2)

1. As the same user, confirm "My Documents" lists the uploaded document with
   correct metadata, and sort/filter controls work.
2. Log in as "Camille Nicole" (Project Manager) for the sample project;
   upload a document associated with that project; confirm all project
   members can see it under "Project Documents".
3. Search by title/tag/uploader from a user with only partial access;
   confirm only authorized results are returned.

### 3. Lifecycle Management (P3)

1. As the uploader, download and (for PDF/JPEG/PNG) preview the document.
2. Edit its metadata; confirm changes persist.
3. Open the same document's edit form in two sessions, save both — confirm
   the second save is rejected with a conflict error (FR-032).
4. Delete the document as the uploader; confirm it's gone. Repeat as a
   non-owner, non-PM, non-Admin user and confirm the delete is denied.

### 4. Sharing (P4)

1. Share the project document with "Floris Kregel" (Team Lead).
2. Log in as Floris; confirm an in-app notification and the document under
   "Shared with Me".
3. Attempt to access the same document's download URL as a user who wasn't
   shared it and isn't a project member → expect 403/404 (no IDOR).

### 5. Task & Dashboard Integration (P5)

1. From a task detail page, upload a document; confirm it's associated with
   the task's project.
2. On the dashboard home page, confirm the "Recent Documents" widget shows
   the 5 most recent uploads and the document count summary card is correct.

### 6. Security/Access-Revocation Check

1. Remove a user from a project's membership.
2. Immediately retry that user's access to a previously-visible project
   document → expect denial on the very next request (FR-023).

## Automated Tests

Run the new test project:

```powershell
cd ContosoDashboard.Tests
dotnet test
```

Expected coverage at minimum: authorization allow/deny cases for each
`IDocumentService` method, duplicate-detection logic, and concurrency-conflict
handling (see [plan.md](./plan.md) Project Structure).
