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