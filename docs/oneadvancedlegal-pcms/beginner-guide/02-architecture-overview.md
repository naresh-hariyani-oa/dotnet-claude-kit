# 02 — Architecture Overview

## Architecture Style: Layered Monolith with Independent Microservices

PCMS uses a **Layered Architecture** as its primary pattern. The main application is a **modular monolith** — a single deployable unit composed of many well-structured modules. Alongside it, a separate set of **independent REST microservices** (`Solicitors.WebApi`) handles specific domain APIs.

There is no CQRS, no event sourcing, and no message broker. The architecture is deliberately pragmatic given its WinForms migration origin.

---

## The Layers

```
┌─────────────────────────────────────────────────────┐
│  PRESENTATION LAYER                                 │
│  Wisej-3 Forms & Controls                           │
│  app/Legal.Cloud/*, app/Source/IRIS.Law.*Application│
├─────────────────────────────────────────────────────┤
│  APPLICATION / ORCHESTRATION LAYER                  │
│  app/Source/IRIS.Law.*Application (PmsModule, etc.) │
│  app/Legal.Cloud/Legal.Cloud.Main (Services)        │
├─────────────────────────────────────────────────────┤
│  BUSINESS LOGIC LAYER                               │
│  app/Source/IRIS.Law.*Business (Br* classes)        │
├─────────────────────────────────────────────────────┤
│  DATA ACCESS LAYER                                  │
│  app/Source/IRIS.Law.*Data                          │
│  app/Source/IRISLegal.SqlRepositories               │
├─────────────────────────────────────────────────────┤
│  DOMAIN MODEL LAYER                                 │
│  app/Legal.Domain/*                                 │
│  app/Source/IRIS.Law.Entities                       │
├─────────────────────────────────────────────────────┤
│  INFRASTRUCTURE LAYER (cross-cutting)               │
│  app/Source/IRISLegal.*                             │
│  Autofac DI, NLog logging, OTel, EF contexts        │
└─────────────────────────────────────────────────────┘
```

**Rule:** Dependencies flow downward only. Presentation depends on Business; Business depends on Data. Data never depends on Business.

---

## The Four-Layer Domain Module Pattern

Every business domain in `app/Source/` follows this consistent structure:

```
IRIS.Law.{Domain}Application/    ← UI forms and user interaction
IRIS.Law.{Domain}Business/       ← Business rules (classes prefixed with Br*)
IRIS.Law.{Domain}Data/           ← Database access (DataSets, stored procedures)
IRIS.Law.{Domain}CommonData/     ← Shared DTOs and enumerations
```

**Example — PMS domain:**

| Project | What it contains |
|---|---|
| `IRIS.Law.PmsApplication` | Wisej forms: `FrmAddModule`, `FrmMatterDetails`, etc. + `PmsModule.cs` (DI config) |
| `IRIS.Law.PmsBusiness` | `BrMatter`, `BrClients`, `BrFeeEarners`, `BrAccountsRequestsAuthorisation` |
| `IRIS.Law.PmsData` | DataSet-based data access using ADO.NET stored procedure calls |
| `IRIS.Law.PmsCommonData` | `ApplicationSettings` (singleton), `UserInformation` (singleton), enums, constants |

**Why this matters:** When a bug is reported in matter loading, you look at `BrMatter` in business, then `PmsData` in data. When the UI needs fixing, you look at the Application layer. Layers are not mixed.

---

## Dependency Injection Architecture

The application uses **two separate DI mechanisms** that serve different purposes:

### 1. Autofac Container (General IoC)

The master Autofac container is assembled in [`IRISLegal.Fields/ContainerConfiguration.cs`](../../app/Source/IRISLegal.Fields/ContainerConfiguration.cs). It registers eight Autofac modules in sequence:

```csharp
containerBuilder.RegisterModule<RepositoryModule>();       // All SQL repositories + EF contexts
containerBuilder.RegisterModule<BusinessModule>();         // Business layer classes
containerBuilder.RegisterModule<ServicesModule>();         // Core services
containerBuilder.RegisterModule<MetaDataModule>();         // Metadata services
containerBuilder.RegisterModule<DataAccessModule>();       // Data access services
containerBuilder.RegisterModule<InfrastructureModule>();   // Infrastructure services
containerBuilder.RegisterModule<EntitiesModule>();         // Entity definitions
containerBuilder.RegisterModule<LoggingModule>();          // NLog loggers
```

You resolve dependencies via `Container.Instance.Resolve<T>()`. This container uses a **thread-cached session singleton** pattern (via `[ThreadStatic]`), meaning each user session gets its own Autofac container instance.

> **Warning:** Never store `Container.Instance` in a long-lived field. Always call `.Resolve<T>()` through the container reference at the point of use.

### 2. Wisej `Application.Services` Registry (Session/Shared Services)

Wisej has its own lightweight service registry used for services that need to be scoped to a user **session** or shared across the entire application. These are registered in [`CloudApplicationStartup.cs`](../../app/Legal.Cloud/Legal.Cloud.Main/CloudApplicationStartup.cs):

```csharp
Application.Services.AddService<UrlManager>(lifetime: ServiceLifetime.Session);
Application.Services.AddService<NavigationManager>(lifetime: ServiceLifetime.Session);
Application.Services.AddService<ConfigurationManager>(lifetime: ServiceLifetime.Shared);
Application.Services.AddService<BackgroundTasksService>(lifetime: ServiceLifetime.Shared);
```

- **Session** services — one instance per logged-in user session.
- **Shared** services — one instance for the entire application (all sessions).

You retrieve these via `Application.Services.GetService<T>()`.

**Rule:** Use Wisej `Application.Services` for UI/navigation/configuration services. Use Autofac for business logic, repositories, and data access.

---

## The Microservices Layer (Solicitors.WebApi)

The `Solicitors.WebApi` projects are **independently deployable REST services**. Each has:

- Its own DI container (`DefaultDependencyResolver.cs`)
- Its own OWIN pipeline (`Global.asax.cs`)
- No project references to other microservices
- No shared Autofac container with the monolith

They communicate with the main application and with each other exclusively via HTTP REST calls.

```
Main Monolith  ──HTTP──▶  Solicitors.Authentication
               ──HTTP──▶  Solicitors.Matter
               ──HTTP──▶  Solicitors.Client
               ──HTTP──▶  Solicitors.FeeEarners
               ──HTTP──▶  Solicitors.TimeActivities
               ──HTTP──▶  Solicitors.BillingCodes
               ──HTTP──▶  Solicitors.Dms
```

**Deployment:** These services are packaged as NuGet packages and hosted within **WFServer** in ALB (Application Load Balancer) deployments.

---

## Cross-Cutting Concerns

### Logging (NLog + Autofac Interceptor)

All repositories registered via `RepositoryModule` are automatically wrapped with a **`TraceLogger` interceptor** using Autofac's `DynamicProxy2`:

```csharp
builder.RegisterAssemblyTypes(typeof(RepositoryModule).Assembly)
       .Where(t => t.Name.EndsWith("Repository") && t.IsPublic)
       .AsImplementedInterfaces()
       .EnableInterfaceInterceptors()
       .InterceptedBy(typeof(TraceLogger));
```

The `TraceLogger` fires `BeforeInvocation`, `AfterInvocation`, and `OnException` hooks around every repository method call. You get logging without writing a single log statement in your repository classes.

### Observability (OpenTelemetry)

The application uses a **custom Grafana build** of OpenTelemetry (v1.15.0-rc.1) with patches for .NET Framework 4.8. These DLLs are stored in `app/Legal.Cloud/Legal.Cloud/otel/netfx/` and referenced directly, **not via NuGet**.

New data access code should add OpenTelemetry activity spans. See [`docs/OTel_Naming_Convention.md`](../OTel_Naming_Convention.md).

### Configuration

Configuration is split by target framework:

| What | Where | Who reads it |
|---|---|---|
| Wisej license, IIS settings | `Web.config` | .NET Framework 4.8 / IIS |
| App settings, Exchange config | `appsettings.json` | ASP.NET Core / .NET 7.0 |
| Runtime overrides | Environment variables | Both frameworks |

Key environment variables at runtime:
- `GRAPH_TENANT_DOMAIN`, `GRAPH_AUTH_CLIENT_ID`, `GRAPH_AUTH_KEY`, `GRAPH_TENANT` — Azure AD
- `PCMS_HOSTNAME` — Application hostname
- `DEFAULT_DB_PARAM_NAME` — Which DB to read `SysParam` table from
- `Account.*`, `Theme`, `Links.*` — Can override values in `appsettings.json`

### Feature Flags

Runtime feature toggles are accessed via the `FeatureFlags` class (`IRISLegal.FeatureToggle`):

```csharp
var featureFlag = new FeatureFlags();
bool isDisabled = featureFlag.IsEnabled(FeatureFlags.DisableSessionTimeOut, featureFlag.Customer);
```

Do not hardcode behavior guarded by a feature flag — check the flag at runtime.

---

## Authentication Architecture

### Session Authentication (Wisej)

The main application uses `Application.IsAuthenticated` from the Wisej framework. If the user is not authenticated, `Program.cs` redirects to `/login`.

### OAuth 2.0 / Azure AD (Cloud Services)

Cloud integrations (OneDrive, SharePoint, Teams, Exchange) authenticate via **MSAL (Microsoft Authentication Library)**. Tokens are acquired for scopes such as `User.ReadWrite.All`, `Files.ReadWrite.All`, and `Sites.ReadWrite.All`.

### Legacy IdM Authentication

The legacy IRIS.Law code uses an `AuthenticationDetails` service with token-based session tracking. This co-exists with the Wisej session layer.

### Microservice Authentication (Solicitors.WebApi)

Each microservice implements HTTP Basic Authentication via a `BasicAuthenticationIdentity` handler in `Solicitors.Authentication`.

---

## Data Access Architecture

Two ORMs are used in combination:

### Entity Framework 6

Used for structured data operations with auditing support. The main contexts are:

| Context | Domain |
|---|---|
| `IrisLegalContext` | Workflow screens and elements |
| `IlbDataModel` | ILB (International Law) domain |
| `MetaDataModel` | Metadata storage |
| `EomDataModel` | EOM domain |

All EF contexts are registered `InstancePerLifetimeScope` — one per Autofac lifetime scope (effectively one per user session request).

### Dapper

Used for **performance-critical ad-hoc queries** and cases where full EF overhead is unnecessary. Dapper is used directly with `SqlConnection` and `DynamicParameters`:

```csharp
var parameters = new DynamicParameters();
parameters.Add("@sysParamName", paramName, DbType.String);
var result = connection.QueryFirstOrDefault<string>(
    "SELECT TOP 1 SysParamValue FROM SysParam WHERE SysParamName = @sysParamName",
    parameters);
```

Always use parameterized queries — never string-concatenated SQL.

### Stored Procedures (Legacy Data Layer)

The legacy `IRIS.Law.*Data` projects call SQL Server stored procedures via ADO.NET `DataSet` adapters:

```csharp
// Pattern: CommandType.StoredProcedure with DataSet fill
```

This is the oldest pattern in the codebase. New code should prefer Dapper or EF over this pattern, but do not refactor existing stored procedure calls without understanding the impact.

---

## Transaction Management

Transactions are managed via `SqlUnitOfWork`, which wraps `System.Transactions.TransactionScope`:

```csharp
// ReadCommitted isolation level
var options = new TransactionOptions { IsolationLevel = IsolationLevel.ReadCommitted };
var transaction = new TransactionScope(TransactionScopeOption.Required, options);
```

Usage pattern:

```csharp
using (var uow = unitOfWorkFactory.Create())
{
    // perform repository operations
    uow.Commit();
}
```

If `Commit()` is not called before the `using` block exits, the transaction is rolled back.

---

## Key Singleton Services

Two important singletons live in `IRIS.Law.PmsCommonData` and are accessed throughout the codebase:

| Singleton | Access | Purpose |
|---|---|---|
| `ApplicationSettings.Instance` | `IRIS.Law.PmsCommonData` | System-wide application settings fetched from the database |
| `UserInformation.Instance` | `IRIS.Law.PmsCommon` | Current logged-in user's context (DbUid, username, branch, department) |

These are not injected — they are static singletons. Do not replace or re-initialize them accidentally.
