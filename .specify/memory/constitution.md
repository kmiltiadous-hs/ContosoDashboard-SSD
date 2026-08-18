<!--
Sync Impact Report
==================
Version change: [TEMPLATE] → 1.0.0 (initial ratification)
Modified principles: n/a (first concrete adoption from placeholder template)
Added sections:
  - Core Principles: I. Secure by Design, II. Authorization Enforced at the
    Service Layer, III. Secure Data & File Handling, IV. Test-First for
    Security-Relevant Logic, V. Least Privilege & Auditability
  - Technology & Architecture Constraints
  - Development Workflow & Quality Gates
  - Governance
Removed sections: none (placeholder tokens replaced)
Templates requiring updates:
  - .specify/templates/plan-template.md — ⚠ pending manual review for
    Constitution Check alignment with security-first principles
  - .specify/templates/spec-template.md — ⚠ pending manual review to ensure
    acceptance criteria prompt for auth/negative-path scenarios
  - .specify/templates/tasks-template.md — ⚠ pending manual review to ensure
    task categories include security test tasks
Follow-up TODOs: none
-->

# ContosoDashboard Constitution

## Core Principles

### I. Secure by Design
Every feature MUST be designed with security as a first-class requirement, not an
afterthought. All user input (form fields, file uploads, query parameters, route
values) MUST be validated and sanitized at the point of entry using allow-lists
(e.g., file extension/MIME whitelists) rather than deny-lists. Output rendered to
the browser MUST be encoded to prevent injection (XSS, HTML injection). New
dependencies and libraries MUST be evaluated for known vulnerabilities before
adoption. Rationale: ContosoDashboard handles employee, project, and document data;
vulnerabilities introduced early are the most expensive to remediate later.

### II. Authorization Enforced at the Service Layer (NON-NEGOTIABLE)
Role and ownership checks (Employee/TeamLead/ProjectManager/Administrator, and
resource-ownership such as "uploaded by" or "project member") MUST be enforced in
services/business logic, never only in UI components or page markup. Every
resource-access path (view, download, edit, delete, share) MUST verify the
current user is authorized for that specific resource to prevent Insecure Direct
Object Reference (IDOR) vulnerabilities. `[Authorize]` attributes and UI hiding
are defense-in-depth additions, not substitutes for service-layer checks.
Rationale: Blazor Server UI state can be manipulated or bypassed; only
server-side authorization logic is trustworthy.

### III. Secure Data & File Handling
Files and other untrusted data MUST be stored outside web-accessible directories
(e.g., not under `wwwroot`) and served only through authorized controller
endpoints. Stored filenames MUST be system-generated (e.g., GUID-based); raw
user-supplied filenames MUST NEVER be used to construct file system paths, to
prevent path traversal. Sensitive fields and secrets MUST NOT be logged or
committed to source control. Rationale: Aligns with OWASP Top 10 guidance on
injection and broken access control, and matches the stakeholder security
requirements already defined for document management.

### IV. Test-First for Security-Relevant Logic (NON-NEGOTIABLE)
Authorization rules, input validation, and file-handling logic MUST have
automated tests written before or alongside implementation, covering both
allowed and denied access paths (positive and negative cases). A feature MUST
NOT be marked complete if only the "happy path" is tested. Rationale: Security
regressions are typically introduced through untested edge cases (wrong role,
wrong owner, malformed input).

### V. Least Privilege & Auditability
Components, services, and database roles MUST request only the access they need
to perform their function. Actions that create, modify, delete, or share
sensitive resources (documents, tasks, projects, user data) MUST be logged with
enough context (who, what, when) to support an audit trail. Rationale: Supports
incident response and compliance reporting expected by Administrators in the
stakeholder requirements.

## Technology & Architecture Constraints

ContosoDashboard is a Blazor Server (.NET 8) application with EF Core/SQL Server
and cookie-based authentication. Training/demo constraints (e.g., mock
authentication, local filesystem storage instead of cloud services) are
permitted, but MUST be implemented behind interfaces (e.g.,
`IFileStorageService`) so production-grade implementations can be substituted
without changes to business logic, controllers, or UI. Security headers (CSP,
HSTS, X-Frame-Options, etc.) configured in `Program.cs` MUST be preserved or
strengthened, never weakened, by future changes.

## Development Workflow & Quality Gates

All pull requests/changes touching authentication, authorization, file
handling, or data access MUST include a brief security self-review noting how
each Core Principle above was satisfied. Code review MUST verify: input
validation exists, authorization is checked server-side, no secrets are
exposed, and tests cover negative/denied-access cases. Complexity or deviation
from these principles MUST be explicitly justified in the PR description.

## Governance

This constitution supersedes ad-hoc practices for this repository. Amendments
require: (1) a documented rationale, (2) version bump per semantic versioning
(MAJOR for incompatible principle removal/redefinition, MINOR for new/expanded
principles or sections, PATCH for clarifications/wording), and (3) an updated
Sync Impact Report recorded in this file's history. All PRs and code reviews
MUST verify compliance with this constitution; unresolved violations block
merge unless an explicit, reviewed exception is documented.

**Version**: 1.0.0 | **Ratified**: 2026-08-17 | **Last Amended**: 2026-08-17
