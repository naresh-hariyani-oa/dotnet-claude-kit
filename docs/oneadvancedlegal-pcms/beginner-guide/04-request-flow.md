# 04 — Request Flow

This document explains how the application works at runtime — from a user opening the browser to data being returned from the database. Understanding these flows is essential before making any changes.

---

## Flow 1: Application Startup

When a user first navigates to the application URL, this sequence runs:

```
Browser → IIS / OWIN Pipeline
            │
            ▼
    Startup.net48.cs              ← .NET Framework 4.8 OWIN middleware configured
            │
            ▼
    Program.Main(args)            ← Wisej calls this as the "application entry point"
            │
            ├─ EfContextInitializer.Set()          ← Configures EF6 for SQL Server
            ├─ UnhandledExceptionErrorHandler.Register()
            ├─ Application.SessionTimeout += ...   ← Session countdown logic hooked
            │
            ├─ Application.IsAuthenticated?
            │     NO  → Application.Navigate("/login")   ← Redirect to login page
            │     YES ↓
            │
            ▼
    CloudApplicationStartup.Start(args)
            │
            ├─ [static constructor, runs once per app domain]
            │     Application.Services.AddService<UrlManager>(Session)
            │     Application.Services.AddService<NavigationManager>(Session)
            │     Application.Services.AddService<ConfigurationManager>(Shared)
            │     Application.Services.AddService<BackgroundTasksService>(Shared)
            │     BackgroundTasksService.Start()
            │
            ├─ Application.MainPage = new Splash()   ← Shows splash screen
            ├─ Application_ApplicationExit hooked    ← Logout cleanup registered
            │
            ▼
    InitSessionTimeoutCount()     ← Reads SysParam table via Dapper for timeout config
            │
            ▼
    Application ready for user
```

**Key insight:** The `CloudApplicationStartup` static constructor runs **once per application domain**, not once per user. Session-scoped services (UrlManager, NavigationManager) give each user their own instance. Shared services (ConfigurationManager, BackgroundTasksService) are shared by all users.

---

## Flow 2: User Login

```
User enters credentials on /login page
            │
            ▼
    Solicitors.Authentication microservice
    (Basic Auth handler: BasicAuthenticationIdentity)
            │
            ▼
    Authentication validated → session token issued
            │
            ▼
    Wisej sets Application.IsAuthenticated = true
            │
            ▼
    Application_ApplicationRefresh event fires
    (Program.cs checks UserInformation.Instance.DbUid == 0)
            │
            ▼
    CloudApplicationStartup.Start() called again
    → NavigationManager initialized
    → Splash screen shown
    → Main application shell loads
```

---

## Flow 3: UI Interaction (Wisej Form Lifecycle)

Wisej operates differently from a traditional HTTP request/response model. The browser maintains a **persistent WebSocket connection** to the server. Every UI action sends a message over this socket — there are no page navigations or HTTP round-trips for normal interactions.

```
User clicks a button in browser
            │
            ▼
    WebSocket message → Wisej server runtime
            │
            ▼
    Wisej dispatches to the correct Form/Control event handler
    (e.g., btnSave_Click, dgvMatters_CellClick)
            │
            ▼
    Event handler runs C# code on the server
            │
            ├─ Resolve service from Container.Instance or Application.Services
            │
            ├─ Call Business layer (e.g., BrMatter.SaveMatter())
            │     │
            │     └─ Call Repository (e.g., IMatterRepository.Save())
            │           │
            │           └─ Execute SQL (Dapper / EF / Stored Procedure)
            │
            ▼
    Event handler updates form controls (e.g., lblStatus.Text = "Saved")
            │
            ▼
    Wisej sends UI diff back to browser over WebSocket
            │
            ▼
    Browser updates rendered UI
```

**Key insight:** Unlike a web API, there is no HTTP request per user action. The form state lives on the server in memory for the duration of the session. Each open form is an actual C# object in server memory.

---

## Flow 4: Data Access — Business to Repository to SQL

This is the most important flow to understand when fixing bugs or adding features:

```
Presentation Layer (Wisej Form)
    │
    ▼
Business Layer (BrMatter, BrClients, etc.)
    │  Resolves service from Autofac: Container.Instance.Resolve<IMatterService>()
    │
    ▼
Service Layer (MatterService)
    │  Validates, applies business rules
    │  Coordinates multiple repositories
    │
    ▼
Repository Layer (SqlMatterRepository)
    │  Implements IMatterRepository
    │  Injected by Autofac with TraceLogger interceptor wrapping it
    │
    ▼
Data Access
    ├─ [Dapper] SqlConnection + DynamicParameters → SQL Server
    ├─ [EF6]    DbContext.Set<T>() → SQL Server
    └─ [ADO.NET] DataAdapter + StoredProcedure → SQL Server (legacy Data projects)
```

---

## Flow 5: Microservice REST Request (Solicitors.WebApi)

When the main application needs data from a microservice:

```
Main Application (Wisej form or service)
    │
    ▼
IHttpRequestClient (IRISLegal.Infrastructure)
    │  Constructs HTTP request
    │
    ▼
HTTP POST/GET → Solicitors.{Service} (e.g., Solicitors.Matter)
    │
    ▼
OWIN pipeline (Global.asax.cs)
    │
    ▼
DefaultDependencyResolver (DI container for this service)
    │
    ▼
{Service}Controller.cs (REST endpoint)
    │
    ├─ Validate input
    ├─ Call domain service
    └─ Return JSON response
    │
    ▼
HTTP response → back to main application
```

**Key insight:** Microservices are completely isolated. They have their own DI, their own database connections, and their own error handling. A failure in `Solicitors.Matter` does not crash the main monolith.

---

## Flow 6: Application Exit / Logout

```
User closes browser tab / session timeout occurs
            │
            ▼
    Wisej Application_ApplicationExit event fires
            │
            ▼
    CloudApplicationStartup.Application_ApplicationExit()
            │
            ├─ Application.IsAuthenticated?
            │     YES:
            │       ApplicationSettings.Instance.PostTimesheetsOnLogOut?
            │           → SrvTimeCommon.PostTime(userId)    ← Posts outstanding timesheets
            │       BrUsers.UserLoggedOut(userId, DateTime.Now)  ← Audit trail
            │
            └─ Container.Instance.Dispose()   ← Disposes Autofac container (releases all services)
```

**Warning:** `Container.Instance.Dispose()` releases all Autofac-managed resources for that user's session. After this point, any code that calls `Container.Instance.Resolve<T>()` will throw. The `try/catch` around this block is deliberate.

---

## Flow 7: Session Timeout

```
Inactivity detected by Wisej
            │
            ▼
    Application_SessionTimeout event fires
            │
            ├─ IsSessionTimeoutFeatureFlagDisabled()?
            │     YES → e.Handled = true (suppress built-in form), do nothing
            │     NO ↓
            │
            ├─ SessionTimeoutCount-- <= 0?
            │     NO  → just decrement, e.Handled = true
            │     YES ↓
            │
            └─ SessionTimeoutWindow.Show() ← Custom countdown dialog
                    │
                    User clicks "Stay Logged In":
                        InitSessionTimeoutCount() resets the counter
                    │
                    User does nothing / clicks "Log Out":
                        Session terminates → Application_ApplicationExit fires
```

---

## Flow 8: Background Jobs

```
Application startup
    │
    BackgroundTasksService.Start()   ← Registered as Shared (all sessions)
            │
            ▼
    Background thread pool
            │
            ├─ Periodic job execution (configured in BackgroundTasksService)
            ├─ Each job resolves services from the Autofac container
            └─ Results logged via NLog
```

Background jobs run independently of user sessions. They share the `ConfigurationManager` service but do not have access to user-specific state like `UserInformation.Instance`.

---

## Flow 9: Document Production (Mail Merge)

```
User triggers document generation from a Wisej form
            │
            ▼
    Solicitors.DocumentProduction
            │
            ├─ Template resolution (Word .docx template)
            ├─ Data merge fields resolved from PCMS entities
            ├─ Solicitors.InteropToOpenXml
            │     ├─ Content control population
            │     └─ Office Interop → OpenXML conversion
            │
            └─ Generated document returned to user (download or DMS storage)
```

---

## What Developers Need to Remember About These Flows

1. **No stateless HTTP** — Wisej forms hold server-side state. A form object can live in memory for hours. This is different from ASP.NET MVC or API controllers.

2. **Session affinity** — Because form state lives on the server, users must always hit the same server instance. This affects load balancing configuration.

3. **Database connections are short-lived** — Repositories open and close connections per operation. Do not hold `SqlConnection` objects open across multiple operations.

4. **Container.Instance is per-thread** — The Autofac container uses `[ThreadStatic]`. Each thread gets its own container. Be aware of this when using `Task.Run()` or background threads — they may not have the expected container.

5. **Microservices are fire-and-forget from the monolith's perspective** — Errors from REST calls to microservices should be handled gracefully. The main application should not crash if a microservice is unavailable.
