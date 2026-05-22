# 08 — Debugging Guide

## Debugging in Visual Studio

### Starting a Debug Session

1. Set `Legal.Cloud` as the startup project in Solution Explorer.
2. Press **F5** to start with the debugger attached (IIS Express).
3. Set breakpoints anywhere in the C# code — including inside Wisej form event handlers, business layer classes, and repositories.

Wisej's server-side execution model means **C# breakpoints work exactly like WinForms debugging**. When the browser triggers a UI event, the server-side handler runs and your breakpoints fire normally.

### Attaching to a Running Process

If the application is already running (e.g., full IIS):

1. Debug → Attach to Process (Ctrl+Alt+P).
2. Select `w3wp.exe` (IIS worker process) or the relevant process.
3. Ensure "Managed (.NET Framework)" is included in the attach type.

---

## Reading Application Logs

The application uses **NLog** for logging. Log files are typically written to the application's output directory or a configured path.

Log locations depend on the NLog configuration (`NLog.config` or NLog setup in the project). Check the NLog configuration for the active log target.

**Log levels used:**

| Level | When it fires |
|---|---|
| `Trace` | Fired by the `TraceLogger` interceptor on every repository method call |
| `Debug` | Detailed diagnostic information |
| `Info` | Significant application events (startup, login, logout) |
| `Warn` | Recoverable unexpected situations |
| `Error` | Exceptions and failures |
| `Fatal` | Critical failures requiring immediate attention |

For production, logs are also visible in **Grafana** (connected via OpenTelemetry + Application Insights / New Relic).

---

## Reading OpenTelemetry Traces

The application emits distributed traces via the Grafana OpenTelemetry build. In production, these traces are visible in:

- **Grafana** — traces, logs, and metrics dashboard
- **New Relic** — APM monitoring

For local development, you can start a local OpenTelemetry collector (Jaeger, Zipkin) and configure the exporter endpoint. See [`docs/performance/`](../performance/) for more detail on the observability setup.

---

## Debugging Data Access Issues

### Repository method not being called

1. Check if the repository is registered. All classes ending with `Repository` in `IRISLegal.SqlRepositories` are auto-registered. If yours is in a different assembly, check the relevant Autofac module.
2. Set a breakpoint in the repository implementation to verify it is being hit.
3. Remember: the `TraceLogger` interceptor wraps every repository call. If logging output shows the method entry but no SQL, the issue is inside the repository body itself.

### SQL errors

1. Capture the SQL and parameters at the Dapper call site using a breakpoint on `connection.Query<T>()`.
2. Copy the SQL and parameters to SQL Server Management Studio (SSMS) and run it directly.
3. Common issues: null parameters, wrong column names, stored procedure parameter mismatches.

### Entity Framework errors

1. Enable EF SQL logging by setting a `Database.Log` delegate in the DbContext:

```csharp
context.Database.Log = sql => System.Diagnostics.Debug.WriteLine(sql);
```

2. This outputs every SQL statement EF generates to the Debug Output window in Visual Studio.

---

## Debugging Autofac DI Issues

### "Component not registered" exception

Autofac throws this when you try to resolve a type that was never registered.

**Diagnosis steps:**
1. Check which module the type should be in (Repository → `RepositoryModule`, Business → `BusinessModule`, etc.).
2. Verify the class name ends with the expected suffix (`Repository`, `Service`, `Business`) for auto-scanning to pick it up.
3. Check `ContainerConfiguration.cs` to confirm the relevant module is being registered.
4. If using Keyed resolution: ensure the key string matches exactly.

### "Circular dependency" exception

This means two components each depend on the other. Redesign one to remove the circular reference. Introduce an interface or mediator between them.

### Thread-Static container issue

`ContainerConfiguration.Instance` uses `[ThreadStatic]`. If a `Task.Run()` or background thread calls `Container.Instance.Resolve<T>()`, it will get a different (possibly null) container instance from the one on the UI thread.

**Fix:** Resolve the dependency before the background task, and pass it in:

```csharp
// Correct — resolve before starting background thread
var service = Container.Instance.Resolve<IMyService>();
Task.Run(() => service.DoWork());

// Wrong — resolves on the wrong thread
Task.Run(() => Container.Instance.Resolve<IMyService>().DoWork());
```

---

## Debugging Wisej UI Issues

### UI update not reflecting in browser

Wisej sends UI diffs to the browser after each server-side event handler completes. If an update is not visible:

1. Check whether the control's property is actually being set (add a breakpoint).
2. Ensure you are setting the property on the UI thread. Heavy background work that touches UI controls must use `Application.Update()` or `BeginInvoke()` to marshal back to the Wisej context.

### Form opens but shows blank / crashes

Check the form's constructor and `InitializeComponent()` for exceptions. Designer-generated code in `*.Designer.cs` can reference controls or properties that are no longer valid after migration. Look for `NullReferenceException` in the output.

### Session state lost between requests

Wisej maintains form state server-side. If you use `Task.Run()` or other async mechanisms that execute on a different thread, the Wisej session context may not be available. This surfaces as "Session not found" errors.

---

## Debugging Configuration Issues

### Environment variable not being read

1. Check the variable name exactly (case-sensitive on some platforms).
2. In IIS, environment variables must be set on the Application Pool or in `applicationHost.config`, not just in the system environment.
3. Restart IIS after setting new environment variables — they are read at startup, not dynamically.

### Wrong database being used

`Program.cs` reads `DEFAULT_DB_PARAM_NAME` environment variable to find the connection string key. Set a breakpoint in `GetSysParamValue()` to see which connection string is being resolved.

---

## Common Error Messages and Their Causes

| Error message | Likely cause |
|---|---|
| `"Component not registered"` | DI registration missing in an Autofac module |
| `"The type initializer for 'ApplicationSettings' threw an exception"` | Database connection failed at startup |
| `"TransactionScope has already been completed"` | `Commit()` called more than once on a `SqlUnitOfWork` |
| `"A second operation started on this context"` | EF DbContext used from multiple threads simultaneously — resolve as `InstancePerLifetimeScope` |
| `"Object reference not set to an instance"` in `InitializeComponent` | Designer-generated code references a removed control or unsupported Wisej property |
| `"Session not found"` | Background thread trying to access Wisej session context |
| `"NuGet restore failed: NU1008"` | Package version specified in `.csproj` rather than `Directory.Packages.props` |

---

## Using the Output Window in Visual Studio

The Debug → Output window shows:
- Entity Framework SQL (if `Database.Log` is enabled)
- `Console.WriteLine()` calls from `Program.cs` (feature flag states, etc.)
- Debug assertions
- Wisej framework diagnostic messages

Filter by process name to reduce noise when multiple projects are running.
