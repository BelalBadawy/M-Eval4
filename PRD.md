# Performance Evaluation System — Product Requirements Document (PRD)

**Version:** 1.0  
**Scope:** Authentication & Authorization,Organization Structure, Evaluation Cycles & Groups, Dynamic Evaluation Forms, Forced Distribution, Evaluation Execution, and Approval Workflow.

---

## 1. Product Overview

The Performance Evaluation System is a centralized platform for managing employee performance evaluations across the organization.

The system shall:

- Maintain the organizational structure and employee reporting hierarchy.
- Determine which employees are eligible for evaluation.
- Create evaluation cycles and generate versioned evaluation groups.
- Assign eligible employees to the correct evaluator according to N-Level and direct-management rules.
- Support dynamic evaluation templates, sections, questions, weights, and scoring.
- Support forced-distribution policies, headcount-derived group target means, and group-level distribution validation.
- Allow evaluators to create, save, and submit evaluations for their assigned employees.
- Provide controlled upward visibility and approval workflow.
- Preserve historical group and employee snapshots when organizational or eligibility data changes.
- Separate application authentication/authorization from the HR employee identity.

---

# 2. Business Objectives

1. Replace manual evaluation-group preparation with controlled system generation.
2. Ensure only eligible employees participate in evaluation.
3. Ensure employees are assigned to the correct evaluator based on organizational hierarchy.
4. Prevent historical evaluation groups from changing when employee data changes later.
5. Support different evaluation forms for different business requirements.
6. Enforce forced-distribution rules at the appropriate group level.
7. Provide secure, role-based administration and evaluator access.
8. Provide traceable approval and historical evaluation records.

---

# 3. Scope

## 3.1 In Scope

- Authentication
- Role-based authorization
- Organization and employee hierarchy
- Employee evaluation eligibility
- Evaluation cycles
- Evaluation group generation
- Evaluation group versioning
- Evaluation group membership snapshots
- Dynamic evaluation templates
- Evaluation questions and scoring
- Performance ratings
- Forced distribution
- Evaluation execution
- Group submission validation
- Approval workflow
- Historical data preservation

## 3.2 Out of Scope

The supplied design does not define:

- Payroll
- Recruitment
- Attendance
- Leave management
- Compensation calculations
- Full HR master-data administration
- Notification/email implementation details
- Rejection/return approval paths
- Forced-distribution submission override/exception handling

---

# 4. Functional Modules

The system is specified as twelve numbered functional modules. Each module owns a
requirement-ID prefix; every product-behaviour requirement in this document carries an ID
of the form `<PREFIX>-<NNN>`.

| # | Module | Prefix |
| --- | --- | --- |
| 1 | Authentication & Authorization | `AUTH` |
| 2 | Organization & Employee Hierarchy | `ORG` |
| 3 | Evaluation Eligibility | `ELIG` |
| 4 | Evaluation Cycles | `CYC` |
| 5 | Group Generation & Versioning | `GRP` |
| 6 | Membership Snapshots | `SNAP` |
| 7 | Dynamic Evaluation Templates | `TPL` |
| 8 | Questions & Scoring | `QST` |
| 9 | Performance Ratings | `RAT` |
| 10 | Forced Distribution | `FD` |
| 11 | Execution & Group Submission | `EXEC` |
| 12 | Approval Workflow | `APPR` |

---

# 5. User Roles & Hierarchy

The system enforces role hierarchy via an integer `Level`. Callers cannot assign, modify, or create roles with `Level >= own level`.

| Role | Level | System Protected | Description |
| :--- | :--- | :--- | :--- |
| **Super Admin** | 100 | Yes | Manages roles, permissions, and Admin accounts. Highest administrative authority. |
| **Admin** | 50 | Yes | Manages users, bulk imports, and assigns roles with `Level < 50`. Cannot manage or create Admins. |
| **Auditor** | 30 | Yes | Read-only access to audit logs, security events, and import histories. |
| **User** | 10 | Yes | Default internal user role automatically granted to all created and imported accounts. |

---

# 6. Module 1 — Authentication, Authorization & User Provisioning

### Purpose
Provide an enterprise internal authentication, user provisioning, role-based access control (RBAC), and bulk Excel import engine. There is zero public self-registration. All accounts are administrator-provisioned with temporary credentials subject to mandatory first-login password replacement.

### System Permissions
- **User Management:** `users.create`, `users.read`, `users.update`, `users.deactivate`, `users.delete`, `users.unlock`, `users.reset-password`, `users.force-logout`, `users.import`
- **Role Management:** `roles.manage`, `roles.assign`
- **Audit:** `audit.read`

### Requirements

#### Provisioning & Credentials
- **AUTH-001** The system shall be internal-only: no public registration endpoints, invitation links, or email activation links shall exist.
- **AUTH-002** Accounts shall be created exclusively by Administrators, either individually (`POST /api/v1/users`) or via bulk Excel import (`/api/v1/imports`).
- **AUTH-003** Newly provisioned accounts shall be active immediately upon creation (`IsActive = true`) without email confirmation.
- **AUTH-004** Every newly provisioned account shall receive a temporary default password (`Mina@123` loaded from configuration, never hardcoded in source) with `MustChangePassword = true`.
- **AUTH-005** Every newly provisioned account shall automatically receive the default `User` role upon creation.
- **AUTH-006** The `Users` table shall store user profile data only (`FullName`, `Email`, `PhoneNumber`). Roles and organizational structures shall not exist as columns on the `Users` table and must be stored in normalized junction tables (`UserRoles`).

#### Session Model & Token Lifecycle
- **AUTH-007** The system shall enforce at most one active refresh token per user via a filtered unique index on `RefreshToken.UserId` where `RevokedAtUtc IS NULL`.
- **AUTH-008** A new login from any device shall revoke any pre-existing active refresh token with reason `SupersededByNewLogin` before issuing the new token (one device wins).
- **AUTH-009** Refreshing a token (`POST /api/v1/auth/refresh`) shall revoke the presented token with reason `Rotated` and issue a new active refresh token.
- **AUTH-010** If a refresh token already marked `Rotated` is presented, the system shall treat it as a suspicious replay attack: it shall immediately revoke the user's current active refresh token with reason `SuspiciousReplay` and reject the request with `401 Unauthorized`.
- **AUTH-011** While a user has `MustChangePassword = true`, the issued JWT token shall contain claim `must_change_password: true`.
- **AUTH-012** The `FirstLoginGatewayFilter` shall intercept requests carrying `must_change_password: true` and reject all routes with `403 Forbidden` (`PasswordChangeRequired`), with exactly three whitelisted exceptions: `POST /api/v1/auth/change-password`, `POST /api/v1/auth/refresh`, and `POST /api/v1/auth/logout`.
- **AUTH-013** Refreshing while `MustChangePassword = true` shall check live database state and re-issue an access token retaining `must_change_password: true`.

#### Password Management & Reset
- **AUTH-014** Password change (`POST /api/v1/auth/change-password`) shall require the current password, validate complexity policy (minimum 8 characters, uppercase, lowercase, digit, special character), and explicitly reject passwords matching the configured default password.
- **AUTH-015** Successful password change shall set `MustChangePassword = false`, update `PasswordChangedAtUtc = DateTime.UtcNow`, revoke the active session with reason `PasswordChanged`, and issue fresh tokens.
- **AUTH-016** Self-service password reset request (`POST /api/v1/auth/forgot-password`) shall issue a single-use 30-minute `PasswordResetToken`, dispatch an email notification, and return a uniform response regardless of whether the email exists (enumeration-safe).
- **AUTH-017** Password reset completion (`POST /api/v1/auth/reset-password`) shall validate the token, enforce password complexity, update the password, set `MustChangePassword = false`, and revoke any active session with reason `PasswordReset`.
- **AUTH-018** Password history tracking (last N passwords) is explicitly deferred to future modules.
- **AUTH-019** Admin force-reset (`POST /api/v1/users/{id:guid}/force-reset-password`) shall reset the user's password to the configured default, set `MustChangePassword = true`, reset `PasswordChangedAtUtc = DateTime.UtcNow`, revoke active sessions with reason `AdminForceReset`, and send an email notification.
- **AUTH-020** A background service (`DefaultPasswordInactivityWorker`) shall daily scan for accounts remaining on the default password where `PasswordChangedAtUtc ?? CreatedAtUtc` exceeds 14 days, deactivate the account (`IsActive = false`), write an audit log, and notify administrators.

#### Account Security & Lockout
- **AUTH-021** Single source of truth for lockout shall be `LockoutEndUtc > DateTime.UtcNow`. The system shall increment `FailedLoginAttempts` upon failed credential validation, locking the account for 15 minutes after 5 consecutive failures.
- **AUTH-022** Administrator unlock (`POST /api/v1/users/{id:guid}/unlock`) shall reset `FailedLoginAttempts = 0` and set `LockoutEndUtc = null`.
- **AUTH-023** Administrator force-logout (`POST /api/v1/users/{id:guid}/force-logout`) shall immediately revoke the target user's active session with reason `AdminForceLogout`.
- **AUTH-024** Self-protection guards shall prevent administrators from deactivating themselves or deactivating the last active administrator account.

#### Role Hierarchy & Permissions
- **AUTH-025** The system shall enforce role hierarchy: administrators cannot assign, edit, or create roles with `Level >= own level`. An Admin (50) cannot assign or manage Admin accounts; only Super Admin (100) may manage Admins.
- **AUTH-026** Core system roles (`Super Admin`, `Admin`, `Auditor`, `User`) shall be flagged `IsSystemProtected = true` and cannot be deleted or renamed.
- **AUTH-027** Updating role permissions (`PUT /api/v1/roles/{id:guid}/permissions`) shall perform an atomic replacement of the role's assigned permissions.
- **AUTH-028** Modifying or removing a user's role shall immediately revoke the user's active refresh token to ensure permission changes take effect promptly.
- **AUTH-029** Bulk role assignment (`POST /api/v1/users/bulk-assign-role`) shall validate caller hierarchy level against the target role and update multiple users in one transaction.

#### User Lifecycle & Profile Self-Service
- **AUTH-030** Email uniqueness shall be enforced via a filtered unique index on `Email` where `[SoftDeletedAtUtc] IS NULL`, permitting account recreation following soft deletion.
- **AUTH-031** Self-service profile updates (`PUT /api/v1/users/me`) shall require only authentication (`Authorize`) and shall be constrained to non-sensitive fields (`PhoneNumber`), without requiring administrative permissions. Route collision shall be prevented via `{id:guid}` constraints on administrator routes.

#### Excel Bulk Import Engine
- **AUTH-032** All bulk import endpoints shall be grouped under the dedicated namespace `/api/v1/imports/...`.
- **AUTH-033** The import template (`GET /api/v1/imports/template`) shall include only `Users` table columns (`FullName`, `Email`). Any uploaded file containing forbidden columns (`Role`, `Department`, `Password`) shall be rejected immediately.
- **AUTH-034** Upload limits shall be strictly enforced: maximum file size 5 MB, maximum row count 5,000 rows.
- **AUTH-035** Formula injection neutralization shall sanitize leading characters (`=`, `+`, `-`, `@`) while preserving internal hyphens in names (e.g. "Anne-Marie").
- **AUTH-036** Dry-run validation (`POST /api/v1/imports/dry-run`) shall parse rows, detect in-file duplicates and DB-existing duplicates, stage valid records in `ImportBatchRow`, and generate summary metrics and downloadable error reports (`GET /api/v1/imports/{id:guid}/errors.xlsx`).
- **AUTH-037** At execution time (`POST /api/v1/imports/{id:guid}/execute`), the system shall re-check database duplicates for staged rows against the live database state, applying the batch's chosen `DuplicateStrategy` (`Skip`, `Update`, `FailRow`) and `CommitPolicy` (`AllOrNothing`, `PartialValidOnly`).
- **AUTH-038** Single active import lock shall be enforced via the database; concurrent execution attempts shall be rejected with `409 Conflict`.
- **AUTH-039** Cancellation (`POST /api/v1/imports/{id:guid}/cancel`) shall mark the batch `Cancelled`, halting background processing. Staged records in `ImportBatchRow` shall be purged upon batch completion or cancellation.
- **AUTH-040** Batch rollback (`POST /api/v1/imports/{id:guid}/rollback`) shall soft-deactivate all accounts created by that batch (`IsRolledBack = true`, `IsActive = false`), revoke their active sessions, and leave pre-existing updated accounts untouched.

#### Audit Logging
- **AUTH-041** All authentication events, provisioning actions, role changes, status updates, and import jobs shall be recorded in the `AuditLog` table. Audit logs shall be immutable: updates and deletions shall be blocked at the database context level. Querying audit logs (`GET /api/v1/audit/logs`) shall be restricted to users possessing `audit.read`.

---

# 7. Module 2 — Organization & Employee Hierarchy

### Purpose
Maintain the organizational structure (Companies, Departments, Sections, Positions with N-Level) and employee reporting hierarchy that underpins evaluation eligibility (Module 3), cycle group generation & evaluator routing (Module 5), and historical membership snapshots (Module 6).

### System Permissions
- **Organization Read:** `org.read`
- **HR Master Import:** `org.import`
- **Eligibility Override:** `employees.manage-eligibility`
- **User Account Binding:** `employees.link-user`

### Requirements

#### Organizational Structure & Lookups
- **ORG-001** The system shall store organizational lookups (`Companies`, `Departments`, `Sections`, `Positions`) using external integer IDs as primary keys without database IDENTITY generation (`.ValueGeneratedNever()`).
- **ORG-002** Organizational lookups shall have zero manual CRUD endpoints; the sole writer for lookups shall be the HR synchronization import process.
- **ORG-003** Departments shall belong to exactly one Company; Sections shall belong to exactly one Department. Composite unique constraints `(DepartmentId, CompanyId)` and `(SectionId, DepartmentId)` shall be enforced in the database.
- **ORG-004** Positions shall define an integer `NLevel >= 1`, where Level 1 represents the highest organizational tier (e.g., CEO/General Manager) and higher values represent lower hierarchy tiers.

#### Employee Master & Placement Matrix
- **ORG-005** Employees shall use the external HR `EmployeeId` as the primary key (`.ValueGeneratedNever()`) and `EmployeeNumber` as an immutable unique business key. An existing `EmployeeId` matched with a different `EmployeeNumber` shall be rejected as a row error.
- **ORG-006** An employee's organizational placement shall be enforced via composite foreign keys: `(DepartmentId, CompanyId)` referencing `Departments` and `(SectionId, DepartmentId)` referencing `Sections`, configured with `DeleteBehavior.Restrict`.
- **ORG-007** The system shall enforce via check constraint `CK_Empl_SectionNeedsDept` that `SectionId` cannot be assigned if `DepartmentId` is NULL.
- **ORG-008** `EmploymentStatus` shall support `1 = Active`, `2 = Resigned`, and `3 = Terminated`. Database check constraints shall enforce:
  - `CK_Empl_StatusDates`: `ResignationDate` is NULL for Active status and NOT NULL for Resigned or Terminated status.
  - `CK_Empl_ResignationAfterHire`: `ResignationDate IS NULL OR ResignationDate >= HireDate`.

#### Manager Hierarchy & Cycle Detection
- **ORG-009** Each employee may reference a `DirectManagerId` pointing to `Employees(EmployeeId)` with `DeleteBehavior.Restrict`. Top-level executives (`NLevel = 1`) are expected to have `DirectManagerId = null`.
- **ORG-010** The system shall detect and reject cyclical manager relationships of any depth (e.g., A reports to B, B reports to C, C reports to A) across the overlaid graph (`file rows ∪ existing DB employees`).
- **ORG-011** A direct manager must be an active employee (`EmploymentStatus = 1` and `IsActive = 1`) within the same company.

#### Dual Identity & Local Column Ownership
- **ORG-012** The system shall link an employee to an application login account via `UserId INT NULL REFERENCES Users(Id)` (`DeleteBehavior.Restrict`), enforced by a filtered unique index `UX_Employees_UserId WHERE UserId IS NOT NULL`.
- **ORG-013** The fields `IsEvaluationEligible`, `UserId`, and `IsActive` shall be locally managed: HR bulk imports shall never overwrite these fields during upserts.
- **ORG-014** Authorized administrators (`employees.manage-eligibility`) may toggle `IsEvaluationEligible` for an employee, writing an audit log entry (`EligibilityChanged`).
- **ORG-015** Authorized administrators (`employees.link-user`) may link or unlink an employee to an existing `Users` account. Target user must exist and be active (`IsActive = true`); double-linking shall return `409 Conflict`. Audit entries (`UserLinked`, `UserUnlinked`) shall be recorded.

#### Hierarchy Querying & Anomalies
- **ORG-016** Querying the organizational hierarchy tree (`GET /api/v1/org/structure`) and companies list (`GET /api/v1/org/companies`) shall return active-only companies, active departments, and active sections.
- **ORG-017** Querying an employee's manager chain (`GET /api/v1/employees/{id:int}/manager-chain`) shall traverse upward from the employee to the root manager (capped at 100 hops), returning each manager's `EmployeeId`, `FullName`, `PositionId`, `PositionName`, and `NLevel`.
- **ORG-018** Querying an employee's direct reports (`GET /api/v1/employees/{id:int}/direct-reports`) shall return all active employees having `DirectManagerId` equal to the specified ID.
- **ORG-019** An anomaly query (`GET /api/v1/employees/anomalies` and `GET /api/v1/employees/orphans`) shall perform comprehensive hierarchy integrity validation for Module 5's group generation gate, identifying:
  - **Orphans:** active employees missing a direct manager who are not top-tier executives (`DirectManagerId == null && NLevel > 1`).
  - **Root-with-Manager Anomalies:** top-tier executives who have an assigned direct manager (`DirectManagerId != null && NLevel == 1`), flagging suspicious root hierarchy corruption.
  - **Manager Status/Company Mismatches:** active employees whose assigned manager is inactive, resigned, or in a different company.
- **ORG-020** Employee search (`GET /api/v1/employees`) shall support pagination, full-text search, and filtering by `companyId`, `departmentId`, `sectionId`, `positionId`, `managerId`, `nLevel`, `status`, `isEvaluationEligible`, and `hasLinkedAccount`.

#### HR Master Synchronization & Offboarding Cascade
- **ORG-021** HR organizational import dry-run (`POST /api/v1/org/imports/dry-run`) shall validate file format, structural placement matrix integrity, manager links, and detect cycles, returning summary metrics and row error reports without modifying database state.
- **ORG-022** HR organizational import execution (`POST /api/v1/org/imports/execute`) shall execute synchronously with the uploaded file within a single transaction (`CommitPolicy = AllOrNothing`), enforcing a single active import lock (`409 Conflict`) shared between dry-run and execute. Duplicate strategies are evaluated against:
  - `EmployeeId` (primary upsert join key).
  - `EmployeeNumber` (must match existing ID; cross-ID duplicate numbers rejected as row error).
  - `Email` (pre-validated against active DB users to prevent constraint violation).
  Lookups and employees preserve local columns, leave absent employees untouched, and accept NLevel 1 employees with a manager (flagging them via `/anomalies`).
- **ORG-023** When an employee's status changes to Resigned or Terminated, active direct reports are flagged via the hierarchy anomaly query (manager-mismatch category) for managerial reassignment.
- **ORG-024** Setting an employee `IsActive = false` acts as a local data-correction flag, excluding the employee from active queries, hierarchy, and evaluation eligibility without deactivating their linked `User` account.
- **ORG-025** All organizational import success (`OrgImportExecuted`), import failure (`OrgImportFailed`), eligibility modification (`EligibilityChanged`), user linkage (`UserLinked`, `UserUnlinked`), and employee offboarding (`EmployeeOffboarded`) events shall be recorded in `AuditLogs`.
- **ORG-026** When an employee's `EmploymentStatus` changes to Resigned (2) or Terminated (3) via import, the system shall deactivate the linked user account (`IsActive = false`), revoke its active session with reason `EmployeeOffboarded`, and write an audit entry (`EmployeeOffboarded`).