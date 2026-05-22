# 09 — Common Pitfalls

These are mistakes that new developers commonly make in this codebase. Reading this before writing your first change will save you time.

---

## 1. Using the Wrong Solution File

**Pitfall:** Opening a module-specific `.sln` file (e.g., `Legal.Cloud.NavigationBar.sln`) instead of the main one.

**Effect:** Missing project references cause build errors. The wrong startup project may be selected.

**Fix:** Always use `app/Legal.Cloud.sln` for development. The other `.sln` files are for CI/CD or isolated module work only.

---

## 2. Specifying Package Versions in `.csproj`

**Pitfall:**

```xml
<!-- WRONG — triggers NU1008 build error -->
<PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
```

**Effect:** Build error: `NU1008 Projects that use central package version management should not define the version on the PackageReference items`.

**Fix:** Remove the `Version` attribute and add the version to `Directory.Packages.props`:

```xml
<!-- In .csproj -->
<PackageReference Include="Newtonsoft.Json" />

<!-- In Directory.Packages.props -->
<PackageVersion Include="Newtonsoft.Json" Version="13.0.3" />
```

---

## 3. Treating Wisej Like ASP.NET

**Pitfall:** Assuming each user action creates a new HTTP request. Writing code that treats forms as stateless.

**Effect:** Misunderstanding the lifecycle leads to bugs like:
- Re-initializing a form on every button click (because you think it's a "new request")
- Not understanding why form properties persist between button clicks (they do — the form object stays alive on the server)
- Trying to use `HttpContext.Current` (not available in Wisej event handlers)

**Fix:** Treat Wisej forms like WinForms — they are stateful C# objects living on the server for the lifetime of the session.

---

## 4. Calling `.Result` or `.Wait()` on Async Code

**Pitfall:**

```csharp
// WRONG — causes deadlock in Wisej/ASP.NET context
var data = GetDataAsync().Result;
```

**Effect:** Deadlock. The async method waits for the UI thread to be free, but the UI thread is blocking waiting for the async method.

**Fix:** Either `await` the call properly, or use a fire-and-forget pattern with careful error handling. Do not mix synchronous blocking with async code.

---

## 5. Modifying `*.Designer.cs` Files Casually

**Pitfall:** Editing auto-generated Designer files to fix layout or behavior.

**Effect:** The next time the Wisej or Visual Studio designer runs, your changes are overwritten and lost.

**Fix:**
- Make changes through the visual designer, or in the non-generated partial class file (e.g., `FrmMatter.cs` not `FrmMatter.Designer.cs`).
- If a migration-required edit to a Designer file is unavoidable, make the smallest possible change and track the reason in a GitHub issue.

---

## 6. Writing SQL Without Parameterization

**Pitfall:**

```csharp
// WRONG — SQL injection vulnerability
var sql = $"SELECT * FROM Matter WHERE Reference = '{reference}'";
connection.Query<Matter>(sql);
```

**Effect:** Security vulnerability (SQL injection). Also bypasses query plan caching in SQL Server.

**Fix:**

```csharp
var sql = "SELECT * FROM Matter WHERE Reference = @reference";
var parameters = new DynamicParameters();
parameters.Add("@reference", reference, DbType.String);
connection.Query<Matter>(sql, parameters);
```

---

## 7. Resolving from `Container.Instance` on a Background Thread

**Pitfall:** Calling `Container.Instance.Resolve<T>()` inside a `Task.Run()` or `Thread.Start()`.

**Effect:** `Container.Instance` uses `[ThreadStatic]`. Background threads get a `null` or empty container, causing `NullReferenceException` or "Component not registered" errors.

**Fix:** Resolve what you need on the Wisej UI thread first, then pass the resolved instance into the background task:

```csharp
// Correct
var service = Container.Instance.Resolve<IMyService>();
Task.Run(() => service.DoWork());
```

---

## 8. Mutating `ApplicationSettings.Instance` or `UserInformation.Instance`

**Pitfall:** Setting properties on these singletons in business logic code or tests without restoring them.

**Effect:**
- In production: state corruption for the active user session. For example, accidentally setting `UserInformation.Instance.DbUid = 0` will make the application think no user is logged in.
- In tests: test pollution — one test's changes affect subsequent tests.

**Fix:**
- In production code: read from these singletons; do not write to them outside of the authentication flow.
- In tests: save and restore original values in `IDisposable.Dispose()`.

---

## 9. Missing Autofac Registration

**Pitfall:** Creating a new repository or service class but forgetting to register it.

**Effect:** "No component for type X has been registered" exception at runtime.

**Common causes:**
- Class name does not end with `Repository` (so auto-scan in `RepositoryModule` skips it).
- The class is in a different assembly that isn't scanned.
- A Keyed registration was needed but only a regular registration was added.

**Fix:**
- Follow the naming convention: repositories end with `Repository`, services follow convention in `DataAccessModule`.
- If auto-scan won't cover it, add an explicit `builder.RegisterType<T>()` in the appropriate module.

---

## 10. Adding a NuGet Reference to Grafana OpenTelemetry

**Pitfall:** Adding `<PackageReference Include="OpenTelemetry" />` to a project.

**Effect:** Conflicts with the custom Grafana OTel build (v1.15.0-rc.1) in `otel/netfx/`. Assembly binding conflicts, runtime failures.

**Fix:** Do not add OpenTelemetry NuGet packages. The Grafana DLLs in `app/Legal.Cloud/Legal.Cloud/otel/netfx/` are used directly. See [`CPMReadme.md`](../../CPMReadme.md).

---

## 11. Editing the Legacy IRIS.Law Code Without Understanding the Impact

**Pitfall:** Making "quick fixes" in large legacy files like `IRIS.Law.DiaryBusiness/BookingsSystem.cs` (79KB) or `IRIS.Law.PmsCommonData/ApplicationSettings.cs` (239KB) without understanding their use.

**Effect:** These files are deeply interconnected. A one-line change can break features in seemingly unrelated parts of the application.

**Fix:** Before changing legacy code:
1. Read [10-safe-development-workflow.md](10-safe-development-workflow.md).
2. Grep for all callers of the method you're changing.
3. Run the full test suite before and after.
4. Ask a senior developer to review changes in these files.

---

## 12. Committing to the Wrong Branch

**Pitfall:** Committing fixes directly to `main` or to a feature branch that is under a merge freeze.

**Effect:** Blocked pipeline, broken CI, or accidental production deployment.

**Fix:** Always create a new feature branch for your work. Check `.harness/` or team communication for active merge freezes before raising a PR.

---

## 13. Ignoring the `// TODO:` Comments

**Pitfall:** Assuming that code commented out with `// TODO: Not supported on web` is dead code and can be deleted.

**Effect:** These comments document intentional decisions from the WinForms migration. Deleting them loses the context about what was ported, what was deferred, and why.

**Fix:** Leave `// TODO:` comments in place. If you implement the deferred feature, remove the TODO and replace it with the implementation. Never just delete a TODO silently.

---

## 14. Using string Interpolation for Log Messages

**Pitfall:**

```csharp
// WRONG — eagerly evaluates the string even if the log level is disabled
_logger.Debug($"Loading matter {matterId} for user {userName}");
```

**Effect:** Performance cost — string interpolation always evaluates, even if Debug logging is off.

**Fix:** Use structured logging with message templates:

```csharp
// Correct — NLog evaluates lazily
_logger.Debug("Loading matter {MatterId} for user {UserName}", matterId, userName);
```

---

## 15. Forgetting to Dispose `SqlUnitOfWork`

**Pitfall:** Creating a `SqlUnitOfWork` without a `using` block.

**Effect:** The `TransactionScope` is never disposed, leading to connection pool exhaustion and database transaction timeouts.

**Fix:** Always use `using`:

```csharp
using (var uow = _unitOfWorkFactory.Create())
{
    _repository.Save(entity);
    uow.Commit();
}
```
