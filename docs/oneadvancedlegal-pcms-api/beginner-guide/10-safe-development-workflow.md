# 10 — Safe Development Workflow

This guide tells you **how to work safely** in this codebase — which areas are risky, where changes have wide impact, and the recommended workflow before making any change.

---

## Before You Write Any Code

### Step 1: Understand the domain

1. Find the domain in the controller: `api/IntegrationApi/Controllers/{Domain}/v1/`
2. Read the service interface: `api/Business/Legal.Pcms.Integration.Api.Business.{Domain}/v1/I{Domain}Service.cs`
3. Read the repository interface: `api/Data/Legal.Pcms.Integration.Api.Data.{Domain}/v1/I{Domain}Repository.cs`
4. Read the existing service implementation to understand existing patterns
5. Check the existing tests to understand expected behaviour

### Step 2: Run the tests before changing anything

```bash
dotnet test api/Legal.Pcms.Integration.Api.Test.sln
```

Confirm all tests pass on the current branch before you make changes. If tests are already failing, understand why before proceeding.

### Step 3: Build cleanly

```bash
dotnet build api/Legal.Pcms.Integration.Api.sln
dotnet build api/Legal.Pcms.Integration.Api.Test.sln
```

Fix any build warnings in the code you plan to change.

---

## Risk Classification of Areas

### High Risk — Touch with caution

| Area | Why it's risky | What to do |
|---|---|---|
| `api/Common/Legal.Pcms.Api.Common.Data/v1/Implementation/BaseRepository.cs` | Every repository inherits this. A bug here breaks all database access | Run full test suite. Get senior review. |
| `api/Common/Legal.Pcms.Api.Common.Business/v1/Implementation/BaseService.cs` | Every service inherits this. A bug here breaks all business logic | Same as above |
| `api/Common/Legal.Pcms.Api.Common.Data/v1/Implementation/AppDbContext.cs` | 80+ DbSets. Trigger configurations. EF Core compatibility level. A mistake causes runtime failures | Review EF Core migration implications |
| `api/IntegrationApi/Program.cs` | Middleware pipeline, DI registrations, startup. Errors here crash the API at boot | Test startup locally after every change |
| `api/Common/Legal.Pcms.Api.Common.Web/Middlewares/ExceptionHandlingMiddleware.cs` | Handles all exceptions for all requests. A bug here causes all 4xx/5xx to malfunction | Test error paths explicitly |
| `api/Common/Legal.Pcms.Api.Common/Constants/AppConstants.cs` | Constants are referenced everywhere. Renaming or changing values breaks behaviour | Only add; never rename existing constants |

### Medium Risk — Standard care

| Area | Why it matters |
|---|---|
| `IMatterService` / `MatterService` / `MatterRepository` | Most complex domain. Transactional operations, permission checks, document integration |
| `IClientService` / `ClientService` | Client creation involves related entities (Persons, Organisations, AML) |
| Authentication/multi-tenant configuration | Wrong configuration affects all tenants |
| AutoMapper profiles | Missing mappings fail at runtime |

### Lower Risk — Safer areas

| Area | Notes |
|---|---|
| Simple lookup domains (Courts, Titles, AddressTypes) | Read-only, no complex business logic |
| Adding new endpoints to an existing domain | Additive — doesn't change existing behaviour |
| Adding new test cases | No production impact |
| Documentation | No code impact |

---

## Safe Extension Points

These are the correct places to add new functionality without breaking existing behaviour:

| What you want to do | Safe extension point |
|---|---|
| Add a new endpoint to an existing domain | Add method to `I{Domain}Service` → implement in service → add controller action |
| Add a new database query | Add method to `I{Domain}Repository` → implement in repository |
| Add a new domain entirely | Follow [Adding a New Domain API](../CLAUDE.md#adding-a-new-domain-api) in CLAUDE.md |
| Add a new error type | Add new class inheriting `BaseException`, add case to `ExceptionHandlingMiddleware` mapping |
| Add new config options | Add to `ApplicationOptions` class, read via `IOptions<ApplicationOptions>` |
| Add new constants | Append to `AppConstants` — never modify existing constant values |
| Add logging to existing method | Add `_logger.LogMethodEntry()` / `_logger.LogException()` calls |

---

## Tightly Coupled Areas to Avoid Modifying Casually

### `CreateMatterWithRelatedEntitiesAsync()`

This method in `MatterRepository` performs a multi-table transactional write spanning: Matter, Project, ProjectMapType, MatterBalance, Label, AuditMatter. It is the most complex write operation in the system.

- Do not modify the transaction boundary without understanding all downstream effects
- Changes require testing with a real PCMS database, not just unit tests
- Coordinate with the team before modifying

### `GetMatterUserPermission()`

Calls a database UDF to evaluate user permissions for a matter. Incorrect changes here could grant or deny access incorrectly for all users.

### `IMultiTenantService` implementations

These control which database every request connects to. A bug here sends data to the wrong tenant or returns wrong tenant's data. Treat these as security-critical code.

### `ExceptionHandlingMiddleware`

This is the last line of defence for all unhandled errors. Changes could expose internal stack traces to clients or suppress meaningful error codes.

---

## Recommended Workflow for Adding a New Feature

1. **Create a feature branch** from `main`
   ```bash
   git checkout -b feature/LSS-XXXX-description
   ```

2. **Interface first.** Add the new method signature to `I{Domain}Service.cs` and `I{Domain}Repository.cs` before writing any implementation.

3. **Implement repository method first.** Write the SQL/EF Core query. Test it manually against a local database.

4. **Implement service method.** Call the repository. Add validation. Map DTO ↔ entity. Log method entry.

5. **Add controller action.** Thin — just calls service, returns result.

6. **Write tests.** Mock the repository, test the service logic. Minimum: happy path, not-found path, validation failure path.

7. **Run full test suite.**
   ```bash
   dotnet test api/Legal.Pcms.Integration.Api.Test.sln
   ```

8. **Build cleanly.**
   ```bash
   dotnet build api/Legal.Pcms.Integration.Api.sln
   ```

9. **Test locally via Swagger.** Start the API and use Swagger UI to verify the endpoint behaves correctly.

10. **Raise PR.** Reference the Jira ticket. Follow the PR template.

---

## Workflow for Bug Fixes

1. **Reproduce the bug locally** before touching any code.
2. **Write a failing test** that demonstrates the bug.
3. **Fix the code** until the test passes.
4. **Run the full test suite** — confirm nothing else broke.
5. **Do not mix bug fix with refactoring in the same PR.**

---

## Before Merging

Checklist:

- [ ] All existing tests pass
- [ ] New tests written for new behaviour
- [ ] No hard-coded connection strings, secrets, or environment values
- [ ] All new SQL queries use `CreateParameter()` (no string interpolation)
- [ ] New services and repositories registered in `Program.cs`
- [ ] AutoMapper profiles updated and registered
- [ ] `_logger.LogMethodEntry()` called in all new service methods
- [ ] No `.Result` or `.Wait()` on async calls
- [ ] No new `BaseException` subclasses without updating `ExceptionHandlingMiddleware`
- [ ] CLAUDE.md guidelines reviewed and followed

---

## Branch Naming Convention

```
feature/LSS-XXXX-short-description    ← new feature
fix/LSS-XXXX-short-description        ← bug fix
topic/short-description               ← exploratory or refactor work
```

Jira ticket references (LSS-XXXX) are used in branch names and PR titles, as seen in recent git history.

---

## Suggested Learning Order for New Developers

1. Read [00-project-overview.md](00-project-overview.md) — understand what the system is
2. Read [01-repository-structure.md](01-repository-structure.md) — understand where things live
3. Read [02-architecture-overview.md](02-architecture-overview.md) — understand the layered design
4. Read [04-request-flow.md](04-request-flow.md) — trace one request end-to-end
5. Read the `Accounts` domain end-to-end (it is simple, read-only, good for learning the pattern):
   - `AccountsController.cs`
   - `IAccountsService.cs` + `AccountsService.cs`
   - `IAccountsRepository.cs` + `AccountsRepository.cs`
   - `AccountsProfile.cs`
   - `AccountServiceTest.cs`
6. Read [03-design-patterns.md](03-design-patterns.md) — understand how patterns connect
7. Read [07-coding-standards.md](07-coding-standards.md) — before writing any code
8. Read [09-common-pitfalls.md](09-common-pitfalls.md) — know what to avoid
9. Set up your local environment following [05-build-and-run.md](05-build-and-run.md)
10. Run all tests and confirm they pass
11. Make your first change in a low-risk domain following this workflow guide
