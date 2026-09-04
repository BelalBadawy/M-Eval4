# Progress

Per-module status and open specification decisions. Update as each task completes — not just
at the end of a session (`AGENTS.md ## Progress`).

## Convention

- `[ ]` = Not started
- `[-]` = In progress
- `[x]` = Completed

## Modules

Module numbering, names and requirement-ID prefixes are defined in `PRD.md`.

| # | Module | Prefix | Spec | Status |
| 1 | Authentication & Authorization | `AUTH` | [x] | [x] |
| 2 | Organization & Employee Hierarchy | `ORG` | [ ] | [ ] |
| 3 | Evaluation Eligibility | `ELIG` | [ ] | [ ] |
| 4 | Evaluation Cycles | `CYC` | [ ] | [ ] |
| 5 | Group Generation & Versioning | `GRP` | [ ] | [ ] |
| 6 | Membership Snapshots | `SNAP` | [ ] | [ ] |
| 7 | Dynamic Evaluation Templates | `TPL` | [ ] | [ ] |
| 8 | Questions & Scoring | `QST` | [ ] | [ ] |
| 9 | Performance Ratings | `RAT` | [ ] | [ ] |
| 10 | Forced Distribution | `FD` | [ ] | [ ] |
| 11 | Execution & Group Submission | `EXEC` | [ ] | [ ] |
| 12 | Approval Workflow | `APPR` | [ ] | [ ] |

## Open specification decisions

Every decision here is marked **blocking** or **non-blocking**. `AGENTS.md ## Workflow`
VALIDATE gates on that distinction: no task may start on a decision marked blocking.

### Blocking

*(None)*

### Non-blocking

| Decision | Module | Resolution | Why non-blocking |
| --- | --- | --- | --- |
| Single Active Session | 1 (AUTH) | Unique filtered index on `RefreshToken.UserId` where `RevokedAtUtc IS NULL`; one device wins on login; lightweight replay detection via `Rotated` / `SuspiciousReplay` | Simplifies token model without token families; clean revoke across all devices on suspicious replay |
| Password History | 1 (AUTH) | Explicitly deferred to future module | Not needed for MVP first-login and default credential flow |
| Staged Import Handoff | 1 (AUTH) | Staged in `ImportBatchRow` DB table during dry-run; re-validated against live DB duplicates at execution; purged on completion | Avoids physical file retention or re-upload while preventing duplicate collisions |

## Architecture decisions

See `.agents/adr/`.

| ADR | Decision | Plan |
| 1 | ASP.NET Core Minimal APIs + EF Core + SQL Server Stack | 1.auth-and-user-provisioning |


## Plans

| Plan | Complexity | Status |
| --- | --- | --- |
| 1.auth-and-user-provisioning | 🔴 Complex | Completed |
