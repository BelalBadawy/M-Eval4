---
description: Build from the plan
argument-hint: [link-to-plan]
---

# Build

Read and execute: `$ARGUMENTS`

## Process

1. **Read the plan and tasks**
   - Read the target plan file in `.agents/plans/` (or provided argument).
   - Understand the objectives, architectural decisions, and the structured `## Tasks` checklist.
   - Verify that `PROGRESS.md` contains no blocking specification decisions for the target module.

2. **Execute tasks in order**
   - Implement tasks sequentially as defined in `## Tasks`.
   - Adhere to project stack patterns: ASP.NET Core Minimal APIs, EF Core, SQL Server (Localhost), and React 19.
   - For backend changes, enforce business rules in services and data integrity in EF Core.
   - Verify syntax, types, and builds after each change.

3. **Run task validation**
   - For each completed task, run its specified automated test(s) or validation command(s).
   - Fix any failures immediately before moving to the next task.
   - Update `PROGRESS.md` and the plan checklist (`- [x]`) as each task completes.

4. **Post-editing verification (`AGENTS.md ## After Editing`)**
   - Run relevant verification commands based on what changed:
     - Backend: Targeted tests, then `dotnet test backend`, `dotnet build backend --no-incremental`
     - Migrations: `dotnet ef database update --project backend` then test rollback via `dotnet ef database update <PreviousMigration> --project backend`
     - Contract change: Export OpenAPI specification (`backend/openapi.json`)
   - Report actual terminal command outputs — never assume or claim without running.

5. **Report completion**
   - Tasks completed (checklist status)
   - Files created / modified
   - Automated test results and validation evidence
   - Updated status in `PROGRESS.md`
