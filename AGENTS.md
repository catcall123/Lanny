# Agent Instructions

> **IMPORTANT**: GitHub Copilot and all AI agents MUST read and follow these instructions for every task in this repository.

This project uses **GitHub Issues** for issue tracking.

## Issue Workflow

- Use `gh issue list --state open` to find available work
- Use `gh issue view <number>` to inspect issue details before coding
- Reference the issue number in your working notes and final handoff
- After committing and pushing a completed fix, close the corresponding GitHub issue

## Git Workflow

- This repository uses **trunk-based development**
- Work directly on `master`
- Do **not** create feature branches unless the user explicitly asks for one
- Keep `master` current with `origin/master` before starting and after landing work

## Development Principles

### Zero Bugs Philosophy

This project follows a **zero bugs** discipline. Bugs are not tolerated, triaged, or deferred -- they are prevented.

**Core beliefs:**
- A bug that reaches production is a failure of process, not just code
- Every bug is a missing test -- if a test had existed, the bug would not have shipped
- Warnings are bugs waiting to happen -- treat them as errors

**Defensive coding standards:**
- **Guard clauses** - Validate all public method inputs; fail fast with `ArgumentNullException`, `ArgumentException`, or `InvalidOperationException`
- **Null safety** - Use nullable reference types (`#nullable enable`). Never pass or return `null` where a non-null contract exists
- **Error handling** - Catch only exceptions you can handle. Never swallow exceptions silently. Log and rethrow or wrap in a domain-specific exception
- **Immutability** - Prefer `readonly`, `init`, and immutable collections. Mutable shared state is a bug factory
- **Fail fast** - If something is wrong, throw immediately. Do not continue with bad state

**Regression discipline:**
- When a bug is found, write a **failing test that reproduces it** BEFORE writing the fix
- The test must fail without the fix and pass with it
- This ensures the bug can never silently return

### SOLID Principles
All code in this solution MUST follow SOLID principles:
- **S**ingle Responsibility Principle - Each class should have only one reason to change
- **O**pen/Closed Principle - Classes should be open for extension but closed for modification
- **L**iskov Substitution Principle - Derived classes must be substitutable for their base classes
- **I**nterface Segregation Principle - Many specific interfaces are better than one general-purpose interface
- **D**ependency Inversion Principle - Depend on abstractions, not concretions

**How to verify SOLID compliance:**
- **SRP violation** - If a class name contains "And" or "Manager" or "Helper" with multiple unrelated methods, split it
- **OCP violation** - If adding a new feature requires editing existing `if/else` or `switch` chains, use polymorphism or strategy pattern instead
- **LSP violation** - If a subclass throws `NotImplementedException` or ignores a base method, the hierarchy is wrong
- **ISP violation** - If a class implements an interface but leaves methods as no-ops, the interface is too broad
- **DIP violation** - If a class instantiates its own dependencies with `new`, inject them instead

**Additional design guidance:**
- Prefer composition over inheritance
- Keep classes small -- if a file exceeds 300 lines, consider extracting responsibilities
- All dependencies should be injected via constructor injection and registered in the DI container

### Code Smell Prevention
Code smells are treated as early defects. Do not introduce them knowingly and do not leave them behind for "later cleanup."

**Required prevention rules:**
- **No duplication** - If logic is copied twice, extract the shared behavior before merging
- **No long methods** - If a method becomes hard to scan, deeply nested, or mixes orchestration with low-level detail, extract smaller methods
- **No oversized classes** - If a class accumulates unrelated responsibilities, split it before adding more behavior
- **No flag arguments** - If a boolean parameter changes behavior, replace it with separate methods, a strategy, or a richer type
- **No primitive obsession** - Replace repeated strings, IDs, ranges, and grouped parameters with named constants or value objects
- **No magic values** - Use named constants, options, or enums instead of unexplained literals
- **No hidden dependencies** - Do not reach into static state or instantiate collaborators inline when they should be injected
- **No dead code** - Remove commented-out code, unused members, stale branches, and abandoned TODOs without an issue
- **No speculative abstraction** - Do not add layers, interfaces, or extension points unless they solve a real current need
- **No broad catch-and-ignore blocks** - Silent failure is a smell; handle errors explicitly or let them fail fast

**Code smell triggers that require refactoring before completion:**
- **Long parameter lists or data clumps** - Introduce a dedicated request type or value object
- **Large `if/else` or `switch` chains by type, string, or mode** - Replace with polymorphism, strategy, or dispatch tables
- **Feature envy** - Move behavior closer to the data it operates on
- **Shotgun surgery** - If one change touches many files for one policy, centralize that policy
- **Temporal coupling** - If methods must be called in a fragile order, redesign the API to make correct usage explicit
- **Inappropriate intimacy** - If classes know too much about each other's internals, introduce a narrower interface or move the behavior

**Review standard:**
- Before finishing a change, explicitly look for duplication, poor naming, long methods, long parameter lists, magic values, and mixed responsibilities
- If a smell cannot be removed within the current change, document the reason and create a GitHub issue before landing the work

### Test-Driven Development (TDD)
All development MUST follow TDD methodology:
1. **Write the test FIRST** - Before writing any production code, write a failing test that defines the expected behavior
2. **Run the test** - Verify it fails (red phase)
3. **Write minimal code** - Implement just enough code to make the test pass (green phase)
4. **Refactor** - Clean up the code while keeping tests green (refactor phase)
5. **Repeat** - Continue the red-green-refactor cycle

**TDD Rules:**
- NO production code without a corresponding test
- Tests must be written BEFORE the implementation
- Each new feature must have automated test coverage
- Tests should be in the `tests/` directory following the existing structure

**Test quality standards:**
- **Test the happy path AND the sad path** - Every feature needs tests for success cases, failure cases, edge cases, and boundary conditions
- **Test naming** - Use descriptive names: `MethodName_Scenario_ExpectedBehavior` (e.g., `Scan_WithUnreachableHost_ReturnsEmptyList`)
- **One assertion per concept** - Each test should verify one logical behavior. Multiple asserts are fine if they verify the same concept
- **Test error handling** - Verify that exceptions are thrown for invalid inputs, that error messages are meaningful, and that the system recovers gracefully
- **No test interdependence** - Tests must not depend on execution order or shared mutable state
- **Arrange-Act-Assert** - Structure every test with clear setup, execution, and verification phases

### Feature Completion Checklist
A feature is NOT complete until ALL of the following are true:
- [ ] All tests pass (`dotnet test`)
- [ ] No compiler warnings or errors (`dotnet build --warnaserror`)
- [ ] Static analysis passes with no new warnings
- [ ] All public methods have guard clauses for invalid inputs
- [ ] Error paths have corresponding tests
- [ ] All files are committed to git
- [ ] Changes are pushed to remote (`git push`)
- [ ] Build succeeds (`dotnet build`)

## Build & Test

```bash
dotnet build Lanny/Lanny.csproj
dotnet test
```

## Container Deployment

Current production deployment details are documented in [`DEPLOYMENT.md`](DEPLOYMENT.md). Use that file for the Podman host, registry, rootful run command, persistent volume, and verification steps.

Build and run with Podman:
```bash
podman compose up -d
```

Or manually:
```bash
podman build -t lanny ./Lanny
podman run -d --name lanny --network=host --cap-add=NET_RAW --cap-add=NET_ADMIN -v lanny-data:/app/data lanny
```

The container requires `--network=host` (or macvlan) for LAN broadcast visibility and `NET_RAW`/`NET_ADMIN` capabilities for raw sockets and ARP scanning.

## Quick Reference

```bash
gh issue list --state open     # Find available work
gh issue view <number>         # View issue details
gh issue close <number>        # Close a completed issue
git pull --rebase origin master
git push origin master
```

## Landing the Plane (Session Completion)

**When ending a work session**, you MUST complete ALL steps below. Work is NOT complete until `git push` succeeds.

**MANDATORY WORKFLOW:**

1. **File issues for remaining work** - Create issues for anything that needs follow-up
2. **Run quality gates** (if code changed) - Tests, linters, builds
3. **Update issue status** - Close finished work, update in-progress items
4. **PUSH TO REMOTE** - This is MANDATORY:
   ```bash
   git pull --rebase origin master
   git push origin master
   git status  # MUST show "up to date with origin"
   ```
5. **Close completed GitHub issues** - Close every issue fully resolved by the landed commit
6. **Clean up** - Clear stashes, prune remote branches
7. **Verify** - All changes committed AND pushed
8. **Hand off** - Provide context for next session

**CRITICAL RULES:**
- Work is NOT complete until `git push` succeeds
- NEVER stop before pushing - that leaves work stranded locally
- NEVER say "ready to push when you are" - YOU must push
- If push fails, resolve and retry until it succeeds
