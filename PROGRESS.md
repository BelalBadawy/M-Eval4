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
| 1 | Authentication & Authorization | `AUTH` | [ ] | [ ] |
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
| Password hashing algorithm | 1 (AUTH) | Argon2id with bcrypt fallback | Industry standard password hashing; configurable in settings |
| Token transport | 1 (AUTH) | Bearer JWT in Authorization header | Standard REST/OpenAPI client transport compatible with `openapi-fetch` |

## Architecture decisions

See `.agents/adr/`.

| ADR | Decision | Plan |
| 1 | ASP.NET Core Minimal APIs + EF Core + SQL Server Stack | - |


## Plans

| Plan | Complexity | Status |
| --- | --- | --- |
