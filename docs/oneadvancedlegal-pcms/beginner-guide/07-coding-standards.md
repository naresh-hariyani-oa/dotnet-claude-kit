# 07 — Coding Standards

These standards are enforced in code review. Following them from day one will save you review cycles.

> **Also read:** [`CLAUDE.md`](../../CLAUDE.md) at the repo root — it is the authoritative source. This document expands on it with examples.

---

## Naming Conventions

### Classes and Interfaces

```csharp
// PascalCase for classes
public class MatterService { }

// PascalCase for interfaces, prefixed with I
public interface IMatterService { }

// Business layer classes are prefixed with Br
public class BrMatter { }
public class BrClients { }
public class BrFeeEarners { }

// Repository implementations use the Sql prefix
public class SqlMatterRepository : IMatterRepository { }
```

### Methods and Properties

```csharp
// PascalCase for public and protected methods and properties
public void SaveMatter(Matter matter) { }
public int MatterId { get; set; }

// Use expression-body for simple properties
public int MatterId => _matterId;
public string DisplayName => $"{LastName}, {FirstName}";
```

### Fields and Local Variables

```csharp
// Backing fields: underscore prefix + camelCase
private int _matterId;
private string _clientReference;
private readonly IMatterRepository _matterRepository;

// Local variables: camelCase, concise and descriptive
var matterId = 42;
var clientList = new List<Client>();
```

### Constants and Enumerations

```csharp
// PascalCase for constants
public const int DefaultSessionTimeout = 30;

// PascalCase for enum members
public enum MatterStatus
{
    Active,
    Closed,
    Pending
}
```

---

## Class Organization (Regions)

Organize class members using regions in this order:

```csharp
public class MatterService : IMatterService
{
    #region Constructors
    public MatterService(IMatterRepository repository) { }
    #endregion

    #region Events
    public event EventHandler MatterSaved;
    #endregion

    #region Properties
    public int MatterId { get; set; }
    #endregion

    #region Methods
    public void SaveMatter(Matter matter) { }
    private void ValidateMatter(Matter matter) { }
    #endregion

    #region IMatterService
    // Interface implementation at the bottom
    void IMatterService.Execute() { }
    #endregion
}
```

---

## Comments

### When to write comments

Comments explain **WHY**, not **WHAT**. If the code is clear, no comment is needed.

**Write a comment when:**
- There is a hidden constraint (e.g., a specific SQL Server bug workaround)
- The code would surprise a reader ("why does this call `PostTime` at logout?")
- A known platform limitation requires an unusual approach

**Do not write a comment when:**
- The code already explains itself through good naming
- You are restating what the code does in prose

### Comment style

```csharp
// Use double-slash with a space, proper sentence case, ending with a period.
// Correct: This ensures the transaction rolls back if an error occurs.
// Wrong: //this ensures transaction rolls back on error

/// <summary>
/// XML documentation for public and protected members.
/// </summary>
/// <param name="matterId">The matter identifier.</param>
/// <returns>The matter entity, or null if not found.</returns>
public Matter GetMatter(int matterId) { }
```

### TODO comments

Use `// TODO:` to mark work that is intentionally deferred. Always explain why:

```csharp
// TODO: LoadFromFile() is not supported on the web.
// public void LoadFromFile(string path) { ... }
```

---

## Documentation (XML Doc Comments)

| Member type | Required |
|---|---|
| Public classes | `/// <summary>` |
| Public methods | `/// <summary>` + `<param>` + `<returns>` |
| Public properties | `/// <summary>` |
| Protected members | `/// <summary>` |
| Private members | `//` comment (not XML) |

---

## Dependency Injection Style

### Constructor injection

Prefer constructor injection over property injection or direct `Container.Instance.Resolve<T>()` where possible:

```csharp
// Preferred — dependencies are explicit and testable
public class MatterService : IMatterService
{
    private readonly IMatterRepository _matterRepository;
    private readonly IActivityLogService _activityLog;

    public MatterService(IMatterRepository matterRepository, IActivityLogService activityLog)
    {
        _matterRepository = matterRepository;
        _activityLog = activityLog;
    }
}
```

### When to use Container.Instance.Resolve

Use `Container.Instance.Resolve<T>()` in legacy Application layer code (WinForms forms) where constructor injection is not practical. This is a recognized pattern in this codebase — it is not ideal, but do not refactor working form code to add constructor injection without a clear reason.

---

## Async / Await

The codebase uses synchronous code extensively in the legacy layers. When writing new code:

- Prefer `async`/`await` in new service-level code.
- **Never block async code** with `.Result` or `.Wait()`. This causes deadlocks in ASP.NET and Wisej environments:

```csharp
// WRONG — can deadlock
var result = GetDataAsync().Result;

// CORRECT — await properly
var result = await GetDataAsync();
```

If you cannot make the calling method async (common in Wisej form event handlers), consider whether the async operation is truly needed in that location, or whether it can be moved to a background service.

---

## SQL and Data Access

### Always use parameterized queries

Never concatenate user input into SQL strings:

```csharp
// WRONG — SQL injection risk
var sql = $"SELECT * FROM Matter WHERE Reference = '{reference}'";

// CORRECT — parameterized with Dapper
var parameters = new DynamicParameters();
parameters.Add("@reference", reference, DbType.String);
var result = connection.Query<Matter>("SELECT * FROM Matter WHERE Reference = @reference", parameters);
```

### OpenTelemetry spans on new data access

New data access code should include an OpenTelemetry activity span:

```csharp
using var activity = ActivitySource.StartActivity("GetMatterById");
activity?.SetTag("matter.id", matterId);
var result = _repository.GetById(matterId);
```

See [`docs/OTel_Naming_Convention.md`](../OTel_Naming_Convention.md) for naming conventions.

---

## Error Handling

### Use try/catch only where you have a recovery action

Do not add empty `catch` blocks or swallow exceptions silently. The only exception is in exit handlers where a secondary failure must not prevent cleanup:

```csharp
// This pattern (from CloudApplicationStartup.cs) is acceptable only in application exit handlers
catch (Exception ex)
{
    try { Container.Instance.Resolve<ITrackException>().TrackException(ex); }
    catch { }  // Best-effort: if logging also fails, we can't do anything
}

// This is WRONG everywhere else
catch (Exception) { }  // Silently swallowing — never do this
```

### Log errors at the point of handling

```csharp
catch (SqlException ex)
{
    _logger.Error(ex, "Failed to load matter {MatterId}", matterId);
    throw;  // Re-throw if the caller needs to know
}
```

---

## Configuration

- Never hardcode connection strings, API keys, or environment-specific URLs in source code.
- Read from `ConfigurationManager`, environment variables, or `appsettings.json`.
- Use `Environment.GetEnvironmentVariable()` for secrets that vary between environments.

---

## Multi-Targeting Code

When writing code that behaves differently on .NET Framework 4.8 vs .NET 7.0, use separate file naming:

```
MyClass.net48.cs     ← .NET Framework 4.8 specific code
MyClass.netcore.cs   ← .NET 7.0 specific code
```

These files use `#if` preprocessor directives or are included/excluded via the `.csproj` `<Compile>` condition. Follow the existing pattern in `Startup.net48.cs` and `Startup.netcore.cs`.

---

## Designer Files

`*.Designer.cs` files are auto-generated by Visual Studio / Wisej. Follow these rules:

- Do **not** edit them unless absolutely necessary for a WinForms migration change.
- If you must edit a generated file, make the smallest possible change and track the reason in a GitHub issue.
- Prefer making changes through the designer (visual editor) or in the non-generated partial class where possible.

---

## Security

- No hardcoded credentials, tokens, or passwords — ever.
- All SQL must be parameterized (see above).
- Do not log sensitive data (passwords, tokens, PII) — even in debug logs.
- Input from users or external APIs must be validated before use.
- HSTS is enforced via `Web.config` (`max-age=31536000`) — do not remove this header.

---

## Package Management

- Never specify a `Version` in a `<PackageReference>` — versions are managed in `Directory.Packages.props`.
- Do not add a NuGet reference for the Grafana OpenTelemetry assemblies — they are DLL references.
- See [`CPMReadme.md`](../../CPMReadme.md) for the full guide.
