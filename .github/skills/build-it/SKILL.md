---
name: build-it
description: >
  Autonomous issue worker. Loops through every open GitHub issue, implements it
  with TDD, commits, pushes, and moves to the next until the backlog is empty.
  Use this when asked to build all open issues or burn down the backlog.
user-invocable: true
---

# Build-It: Autonomous Issue Worker

Work through **every open GitHub issue**, one at a time, until none remain.

## Before you start

1. Read `AGENTS.md` in the repository root — it contains mandatory development principles (TDD, SOLID, zero bugs), the git workflow, and the feature completion checklist. Follow all of those rules for every issue.
2. Sync with remote:
   ```bash
   git pull --rebase origin master
   ```

## Issue loop

Repeat this cycle until `gh issue list --state open` returns no results.

### 1. Pick the next issue

```bash
gh issue list --state open --limit 1 --json number,title,labels,body
```

If no issues are open, announce "All issues complete" and stop.

### 2. Understand the issue

```bash
gh issue view <number>
```

Read the full description, acceptance criteria, and any linked issues or comments. Make sure you understand what "done" looks like before writing any code.

### 3. Implement (TDD)

Follow TDD strictly:

1. **Write a failing test first** that captures the issue's requirements
2. **Run the test** — confirm it fails (`dotnet test`)
3. **Write the minimal production code** to make it pass
4. **Run all tests** — confirm everything is green (`dotnet test`)
5. **Refactor** if needed while keeping tests green

Also follow the AGENTS.md rules:
- SOLID principles
- Guard clauses for all public method inputs
- Nullable reference types — no nulls where non-null is expected
- Classes under 300 lines

### 4. Quality gates

ALL of these must pass before committing:

```bash
dotnet build Lanny/Lanny.csproj --warnaserror
dotnet test
```

If anything fails, fix it before moving on.

### 5. Commit and push

```bash
git add -A
git commit -m "<concise summary of what was built>

Closes #<number>"
git push origin master
```

The commit message must reference the issue with `Closes #<number>` so GitHub auto-closes it.

### 6. Verify and continue

```bash
gh issue view <number> --json state
```

If the issue did not close (e.g. push failed), resolve the problem and retry. Then go back to step 1.

## Rules

- **One issue at a time.** Do not batch or skip ahead.
- **Never skip tests.** Every issue gets test coverage — no exceptions.
- **Never skip the push.** Work is not done until `git push` succeeds.
- **If an issue is unclear**, add a comment asking for clarification and move to the next issue.
- **If an issue is blocked** by another, skip it and come back after the blocker is resolved.
- **If a build or test fails**, fix it. Never leave master broken.
- **Stop conditions**: all issues closed, or you hit an issue you cannot resolve after reasonable effort (explain why and stop).
