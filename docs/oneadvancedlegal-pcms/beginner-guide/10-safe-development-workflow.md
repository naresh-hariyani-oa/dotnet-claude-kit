# 10 — Safe Development Workflow

## Before You Write Any Code

### 1. Read the Architecture First

Before touching any file, understand where it sits in the architecture:
- Is it in the Presentation layer (Wisej form)?
- Is it in the Business layer (Br* class)?
- Is it in the Data layer (repository or stored procedure)?
- Is it in the Infrastructure/Shared layer (IRISLegal.*)

The layer tells you the expected scope of your change and who will be affected.

### 2. Check Callers Before Modifying

Use "Find All References" in Visual Studio (Shift+F12) or Grep before changing any method signature, interface, or shared utility:

```bash
# Find all usages of a method across the codebase
grep -r "MethodName" ./app --include="*.cs"
```

A shared method in `IRIS.Law.PmsCommon` or `IRISLegal.SqlRepositories` may be called from dozens of places. Changes to these propagate widely.

### 3. Run Tests Before Starting

Establish a baseline — know what was already passing before you made changes:

```bash
dotnet test app/LegalCloud.UnitTests/LegalCloud.UnitTests.csproj
```

If tests are already failing before your change, investigate why before proceeding. Do not create a PR that adds new failures on top of existing ones without knowing what you own.

---

## High-Risk Areas — Handle With Care

These areas have a high density of dependencies and historically cause regressions when changed carelessly:

| Area | Risk Level | Reason |
|---|---|---|
| `IRIS.Law.PmsCommonData/ApplicationSettings.cs` (239KB) | **Critical** | System-wide singleton read by almost everything |
| `IRIS.Law.DiaryBusiness/BookingsSystem.cs` (79KB) | **Critical** | Core diary booking logic — complex state machine |
| `IRISLegal.SqlRepositories/RepositoryModule.cs` | **High** | All repository DI registrations — a mistake here breaks all data access |
| `IRISLegal.Fields/ContainerConfiguration.cs` | **High** | Master Autofac container — mistakes break the whole app at startup |
| `CloudApplicationStartup.cs` | **High** | Startup sequence — affects every user session |
| `Program.cs` | **High** | Application entry point — affects startup for all users |
| `PmsModule.cs` | **High** | 199-line DI module for PMS — used by all PMS features |
| `IRIS.Law.ControlLib/` | **Medium** | Shared UI controls — UI regressions affect all forms using them |
| `Web.config` | **Medium** | IIS / OWIN settings — wrong config can prevent the app from starting |
| `Directory.Packages.props` | **Medium** | Package versions for the entire solution — version conflicts can cascade |

**Rule:** Any change to a Critical area should be reviewed by at least one senior team member before merging.

---

## Safe Extension Points (Lower Risk)

These areas are designed for extension and are lower risk to add to:

| Extension Point | How to extend |
|---|---|
| New business domain | Add new `IRIS.Law.{Domain}Business/`, `IRIS.Law.{Domain}Data/` following the existing four-layer pattern |
| New repository | Create class ending in `Repository`, implement interface — auto-registered by `RepositoryModule` |
| New service | Add `RegisterType<T>().As<I>()` to the appropriate Autofac module |
| New snap panel | Implement `ISnapControl`, add `Keyed` registration in `PmsModule` |
| New task processor | Implement `IClientUserTaskProcessor`, add `Keyed` registration in `PmsModule` |
| New microservice | Add a new project in `Solicitors.WebApi/` following the existing service structure |
| New unit test | Add to the relevant subdirectory in `LegalCloud.UnitTests/` |
| New NuGet package | Add version to `Directory.Packages.props`, add reference to `.csproj` without version |

---

## Making a Change — Step-by-Step

### For a Bug Fix

1. **Reproduce the bug** — verify you can see it before writing any code.
2. **Locate the code** — use the architecture knowledge from this guide to find the right layer.
3. **Check impact** — use Find All References to understand what depends on the code you're changing.
4. **Write a failing test** — add a unit test that demonstrates the bug. It should fail before your fix.
5. **Make the fix** — keep it minimal. Only change what is needed to fix the reported bug.
6. **Verify the test passes** — your new test should now pass.
7. **Run all tests** — `dotnet test` — ensure no regressions.
8. **Build** — `dotnet build app/Legal.Cloud.sln` — ensure no compilation errors.
9. **Raise a PR** — follow the PR template in `.github/pull_request_template.md`.

### For a New Feature

1. **Understand the domain** — read the relevant business layer (Br* classes) to understand existing patterns.
2. **Plan the layering** — decide which layers need changes (UI form, business, data).
3. **Start with the data layer** — add repository methods first, then business logic, then UI.
4. **Follow existing patterns** — match the naming and structure of the nearest similar feature.
5. **Write tests for business logic** — target 80%+ coverage for new code.
6. **Test the full flow** — manually exercise the feature in a running instance.
7. **Raise a PR** — ensure the PR description explains what changed and why.

---

## Git Workflow

```bash
# Create a new branch for your work
git checkout -b feat/TICKET-123-description

# Stage specific files (avoid git add -A which can include sensitive files)
git add app/Source/IRIS.Law.PmsBusiness/BrMatter.cs
git add app/LegalCloud.UnitTests/IRIS.Law.PmsBusiness.UnitTest/BrMatterTests.cs

# Commit with a meaningful message
git commit -m "feat(matter): load matter correctly when draft status is set

PRU-XXXX: Matter fails to load when status is Draft.
Added null-check before status comparison in BrMatter.GetMatter."

# Push and open a PR
git push origin feat/TICKET-123-description
```

**Branch naming:** Use the ticket ID as a prefix (e.g., `feat/PRU-1234-description` or `fix/PRU-1234-description`).

**Never force-push to `main`.**

---

## Code Review Checklist (Before Raising a PR)

Before requesting review, verify:

- [ ] The PR solves only the stated problem (no mixed concerns)
- [ ] All new public/protected members have XML documentation
- [ ] No hardcoded connection strings, API keys, or environment values
- [ ] All SQL uses parameterized queries
- [ ] No `.Result` or `.Wait()` blocking on async code
- [ ] New data access code has an OpenTelemetry span
- [ ] Tests exist for new business logic (80%+ coverage)
- [ ] `dotnet test` passes with no new failures
- [ ] `dotnet build app/Legal.Cloud.sln` passes
- [ ] No `Version="..."` attributes in `.csproj` package references

---

## Suggested Learning Path for New Developers

Follow this order when getting familiar with the codebase:

| Week | Focus |
|---|---|
| 1 | Read this guide end-to-end. Build and run locally. Explore the solution structure in Visual Studio. |
| 2 | Read `IRIS.Law.PmsCommon`, `ApplicationSettings`, `UserInformation` to understand shared state. Trace the startup flow from `Program.cs` through `CloudApplicationStartup`. |
| 3 | Read a complete domain module: start with `IRIS.Law.PmsBusiness/BrMatter.cs` + `IRIS.Law.PmsData/` + matching unit tests. |
| 4 | Trace a full UI flow: pick a Wisej form in `IRIS.Law.PmsApplication/PmsForms/`, follow a button click through business layer to SQL. |
| 5 | Make your first small fix on a well-tested area (unit test coverage > 80%). Run the test suite. Raise a PR. |
| 6+ | Tackle increasingly complex features; start contributing to test coverage improvement in under-tested areas. |

---

## When in Doubt

- Ask a senior developer rather than guess in high-risk areas.
- Check the `docs/performance/` directory — it contains deep analysis of system behavior that is often relevant to bug investigations.
- Read the `// TODO:` comments in the file you're modifying — they contain important context about migration decisions.
- Check the GitHub PR history for the file — recent changes often explain current behavior.
