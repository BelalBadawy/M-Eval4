# AGENTS.md

Employee Evaluation app.

Instructions for **every** AI coding agent in this repo — Claude Code, Codex, or anything
else. This file is the single source of truth; harness-specific files point here or are
generated from it. See `## Agent harnesses`.

Older ADRs, plans, `PROGRESS.md` entries and code comments say `CLAUDE.md ## Section`.
That content is this file.

## Stack

- Frontend: React 19 + TypeScript + Vite + Tailwind CSS v4.0 + shadcn/ui
- Backend: ASP.NET Core Minimal APIs + C# (.NET 10 / 9)
- Database: SQL Server (Localhost), ORM & migrations via EF Core (Entity Framework Core)

## Layout

Scaffolded 2026-09-02. Create paths here rather than inventing new ones.

- `backend/` — ASP.NET Core Minimal API application (Endpoints, Services, Data/DbContext, Models, Migrations); services hold business rules
- `frontend/src/` — React app
- `PRD.md` — authoritative requirements
- `PROGRESS.md` — per-module status and open specification decisions
- `AGENTS.md` — this file; the rules every agent follows
- `CLAUDE.md` — generated stub that imports this file (`## Agent harnesses`)
- `.agents/plans/` — plans (naming in `## Planning`)
- `.agents/adr/` — architecture decision records, named `{sequence}.{decision-name}.md`
- `.agents/commands/` — canonical command bodies, mirrored to `.claude/commands/`

## Agent harnesses

Rules are edited **here and nowhere else**. Harness stubs (like `CLAUDE.md`) import or sync from this file.

## Commands

- Backend build: `dotnet build backend`
- Backend test: `dotnet test backend`
- Backend run: `dotnet run --project backend`
- Add EF Core migration: `dotnet ef migrations add <MigrationName> --project backend`
- Apply EF Core migrations: `dotnet ef database update --project backend`
- Rollback EF Core migration: `dotnet ef database update <PreviousMigrationName> --project backend`
- Frontend dev: `npm run dev` (in `frontend/`)
- Frontend test: `npm run test` (in `frontend/`)
- Frontend build: `npm run build` (in `frontend/`)

## Spec

- `PRD.md` is the authoritative requirements document: 12 numbered functional modules and 5 user roles (System Administrator, Evaluation Administrator, Evaluator, Approver, Employee Viewer). Consult it for scope, role permissions, and module behavior.

## Rules

- Business rules MUST NOT be implemented in React; backend behavior belongs in ASP.NET Core services.
- Prefer existing patterns over introducing new ones. Once a pattern is in the codebase or recorded in an ADR, it MUST be preserved unless a new ADR changes it. While a layer is still unscaffolded there is no pattern to preserve — the first implementation *establishes* one, so decide it deliberately and record it in `.agents/adr/` (following standard ADR format: Status, Context, Decision, Consequences); location and naming are as given in `## Layout`. An ADR is approved when the user says so; nothing else approves it.
- Small, single-purpose diffs — not large rewrites.
- Do not modify unrelated modules or files. Three companions to a change are expected, not unrelated: the regenerated contract and client (`## API contract`), `PROGRESS.md` (`## Progress`), and this file when a rule changes.
- Do not introduce a new dependency without justification. Name it and the reason in the plan; if it shapes architecture, it needs an ADR.
- `PRD.md` and the API contract are the only sources of *product behavior* — derive behavior from them, never from assumption. This file governs engineering practice, not behavior.
- Security-sensitive changes — authentication, authorization and role checks (PRD Module 1, which defines the permission system, and Module 12, where the Approver and Evaluation Administrator roles are exercised; there is no separate security module), secret or password handling, raw SQL, file upload, or any path that could expose another user's evaluation data — require, beyond the normal checks: a test for the deny case and not only the allow case, and a `/security-review` pass over the diff.
- A defect that recurs, or that a rule here would have prevented, feeds a rule back into this file — not just a one-off fix. There is no production environment; this applies in every environment.

## API contract

- ASP.NET Core Minimal API endpoints export an OpenAPI document (e.g. `backend/openapi.json`).
- Frontend API client generated from the OpenAPI specification (e.g. using `openapi-typescript` / `openapi-fetch`).
- Whenever endpoints, parameters, or DTOs change, regenerate the OpenAPI spec and frontend client.

## Migrations

- Migrations are managed via Entity Framework Core (`Microsoft.EntityFrameworkCore.SqlServer` & `Microsoft.EntityFrameworkCore.Design`).
- Target database: SQL Server (Localhost).
- Use `dotnet ef migrations add <MigrationName> --project backend` to generate migrations.
- Every migration must be reversible with a verified `Down()` method.
- Apply locally using `dotnet ef database update --project backend`.

## Tests

- Never ignore a failing test; no feature is complete with one failing.
- New behavior — including every API mutation — requires a new automated test.
- Do NOT modify or delete an existing test just to make it pass. If a test is genuinely wrong, explain why and ask first; otherwise fix the code.
- "Relevant tests" means every suite covering the change surface in `## After Editing`, plus any suite the change could plausibly break. A failure that predates the change does not become acceptable: report it separately, and never use it to excuse a new one.
- There is no CI yet. The gate is the `## After Editing` commands, run locally, with real output reported.

## Definition of done

- All changes pass the relevant tests before being considered complete.
- Never declare completion without validation evidence — report actual command output, not a claim about what would pass.
- Bootstrap exception: where the task is creating the tooling itself and no suite exists yet, the evidence is the new commands running clean. Say plainly that the suite is empty rather than implying tests passed.

## Out of bounds

- Never commit secrets, connection strings, or `.env` files.

## Workflow

DISCOVER → SPEC → PLAN → VALIDATE → TASKS → IMPLEMENT → TEST → REVIEW

- Spec written and reviewed before code.
- VALIDATE proves the plan is correct before any code is written — it gates the plan, not the build. No task starts until every rule in `## Planning` holds and:
  - every product-behavior task traces to a numbered `PRD.md` requirement (`ORG-001`, `FD-014`, …); a task that invents product scope fails the gate;
  - every infrastructure task — scaffolding, tooling, CI, a dependency or generator choice — traces to an ADR in `.agents/adr/` instead. It needs no PRD requirement, and MUST NOT smuggle in product behavior;
  - no task depends on a specification decision that `PROGRESS.md` marks **blocking**. An open decision whose `PRD.md` section states an implementable default is not blocking — implement the default and cite the requirement (e.g. FD-016);
  - every contract change names the endpoints and schemas it touches;
  - every schema change names its migration and its `Down()` rollback.
- A plan that fails VALIDATE goes back to PLAN — do not start TASKS on it.
- TASKS breaks the validated plan into ordered, individually testable units for IMPLEMENT. They live in the plan file under a `## Tasks` heading — not in a separate document.
- TEST runs the automated suites first. Browser coverage of a user-facing flow is `npm run test:e2e`; a browser MCP, where one is configured, is for exploratory checking and does not substitute for an E2E test. No browser MCP is configured in this repo today — if a flow needs one, say so rather than reporting a browser check that did not happen.
- Iterate on any issues found in TEST before moving to REVIEW.
- REVIEW is not complete until PROGRESS.md reflects the work — see `## Progress`.

## Before Editing

Inspect:

- the relevant `PRD.md` module
- architecture decisions in `.agents/adr/`
- neighboring code
- tests
- database impact

## After Editing

Run and report actual command results. Which commands apply depends on what changed:

- Backend code: targeted tests, then `dotnet test backend`, `dotnet build backend --no-incremental`
- Frontend code: `npm run test`, `npx tsc -b`, `npm run lint`, `npm run build`
- Contract change: both of the above, plus the regenerated client and the `backend/openapi.json` diff
- Migration: `dotnet ef database update` then rollback via `dotnet ef database update <PreviousMigration>` against local SQL Server — report both
- User-facing flow: `npm run test:e2e`
- Agent config (this file, `.agents/commands/`): `npm run agents:check`

A command that does not exist yet is not a pass — report that it does not exist.

## Planning

- Save all plans to `.agents/plans/`.
- Naming convention: `{sequence}.{plan-name}.md` (e.g., `1.auth-setup.md`).
- Each task in the plan must include at least one validation test.
- Assess complexity and single-pass feasibility; include a complexity indicator at the top of each plan:
  - ✅ **Simple** — single-pass executable, low risk
  - ⚠️ **Medium** — may need iteration, some complexity
  - 🔴 **Complex** — break into sub-plans before executing

## Progress

- Update PROGRESS.md as each task completes — not just at the end of a session.
- Confirm PROGRESS.md is current before ending a session.
- Check PROGRESS.md for current module status and open specification decisions before starting new work.
- Mark every open specification decision **blocking** or **non-blocking** — `## Workflow` VALIDATE gates on that distinction.
