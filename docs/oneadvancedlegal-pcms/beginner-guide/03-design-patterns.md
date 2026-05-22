# 03 — Design Patterns

This document describes the design patterns actually present in the codebase, where they are implemented, and how to work within them safely as a new developer.

---

## 1. Repository Pattern

**Where:** `app/Source/IRISLegal.SqlRepositories/` (200+ repository classes)

**What it is:** Repositories abstract data access behind an interface. Business logic calls a repository interface; the SQL implementation is injected by Autofac.

**How it is implemented:**

```csharp
// Interface (contract)
public interface IMatterRepository
{
    Matter GetById(int matterId);
    void Save(Matter matter);
}

// Implementation (registered by RepositoryModule)
public class SqlMatterRepository : IMatterRepository
{
    // Dapper or EF-based SQL implementation
}
```

`RepositoryModule` (in `IRISLegal.SqlRepositories`) auto-registers all classes ending with `"Repository"` via assembly scanning:

```csharp
builder.RegisterAssemblyTypes(typeof(RepositoryModule).Assembly)
       .Where(t => t.Name.EndsWith("Repository") && t.IsPublic)
       .AsImplementedInterfaces()
       .EnableInterfaceInterceptors()
       .InterceptedBy(typeof(TraceLogger));
```

**How to extend it safely:**
- Name new repository classes with the `Repository` suffix — this is required for auto-registration.
- Always define an interface (`IXxxRepository`) alongside the implementation.
- Register the interface in `RepositoryModule` if the auto-scan convention does not cover it.
- Do not put business logic inside a repository. Repositories only translate between the domain and the database.

---

## 2. Unit of Work Pattern

**Where:** `app/Source/IRISLegal/UnitOfWork.cs`, `app/Source/IRISLegal.SqlRepositories/SqlUnitOfWork.cs`

**What it is:** Groups multiple repository operations into a single atomic transaction.

**How it is implemented:**

```csharp
// Abstract base
public abstract class UnitOfWork : IDisposable
{
    public abstract void Commit();
    public abstract void Dispose();
}

// SQL implementation — wraps System.Transactions.TransactionScope
public class SqlUnitOfWork : UnitOfWork
{
    private readonly TransactionScope transaction;

    public SqlUnitOfWork()
    {
        var options = new TransactionOptions { IsolationLevel = IsolationLevel.ReadCommitted };
        transaction = new TransactionScope(TransactionScopeOption.Required, options);
    }

    public override void Commit() => transaction.Complete();
    public override void Dispose() => transaction.Dispose();
}
```

**Correct usage:**

```csharp
using (var uow = _unitOfWorkFactory.Create())
{
    _matterRepository.Save(matter);
    _activityRepository.Save(activity);
    uow.Commit(); // rolls back if this line is never reached
}
```

**Warning:** If `Commit()` is not called before the `using` block exits (e.g., due to an exception), the transaction rolls back automatically. This is intentional — do not call `Commit()` in a `finally` block.

---

## 3. Autofac Module Pattern

**Where:** Every major layer has an Autofac `Module` class — `RepositoryModule`, `BusinessModule`, `InfrastructureModule`, `LoggingModule`, `PmsModule`, etc.

**What it is:** Groups related DI registrations into a cohesive unit rather than putting them all in one giant container setup.

**How it is implemented:**

```csharp
public class InfrastructureModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterAssemblyTypes(this.GetType().Assembly)
               .AsImplementedInterfaces();

        builder.RegisterType<WorkshareDocumentCompareProvider>()
               .As<IDocumentCompareProvider>();
        // ...
    }
}
```

Modules are assembled in `ContainerConfiguration.cs`:

```csharp
containerBuilder.RegisterModule<RepositoryModule>();
containerBuilder.RegisterModule<BusinessModule>();
// ... eight modules total
```

**How to extend it safely:**
- When adding a new service to an existing layer, add its registration to the appropriate existing module.
- Only create a new module if you are adding an entirely new architectural concern.
- Always use `AsImplementedInterfaces()` unless you need self-registration (`AsSelf()`).

---

## 4. Keyed Registration Pattern (Strategy via DI)

**Where:** `app/Source/IRIS.Law.PmsApplication/PmsModule.cs` (extensive use)

**What it is:** Multiple implementations of the same interface are registered with different keys. The correct implementation is resolved by key at runtime — a form of the **Strategy Pattern** managed through DI.

**Example — ISnapControl (UI panels):**

```csharp
// Register many different snap controls by key
builder.RegisterType<MsPmsDocumentControl>()
       .Keyed<ISnapControl>("Document");
builder.RegisterType<MsAgendas>()
       .Keyed<ISnapControl>("Agendas");
builder.RegisterType<MsContacts>()
       .Keyed<ISnapControl>("Contacts");
```

**Example — IClientUserTaskProcessor (workflow task handlers):**

```csharp
builder.RegisterType<AttachContactProcessor>()
       .Keyed<IClientUserTaskProcessor>("AttachContact");
builder.RegisterType<GenerateDocumentProcessor>()
       .Keyed<IClientUserTaskProcessor>("GenerateDocument");
builder.RegisterType<ConflictCheckProcessor>()
       .Keyed<IClientUserTaskProcessor>("ConflictCheck");
```

**Resolution at runtime:**

```csharp
var processor = Container.Instance.ResolveKeyed<IClientUserTaskProcessor>(taskType);
processor.Execute(taskContext);
```

**How to extend it safely:**
- To add a new snap panel or task processor, create the class implementing the interface and add a `Keyed` registration in `PmsModule`.
- The key string must match exactly what the caller passes at resolution time. Check existing keys in `PmsModule` before adding new ones.

---

## 5. Decorator Pattern (AOP via Autofac Interceptors)

**Where:** `app/Source/IRISLegal.Logging/TraceLogger.cs` + `RepositoryModule.cs`

**What it is:** The `TraceLogger` is a cross-cutting decorator that wraps every public repository method with logging calls — without the repository class knowing it.

**How it works:**

When `RepositoryModule` registers repositories with `.InterceptedBy(typeof(TraceLogger))`, Autofac uses `DynamicProxy2` to generate a proxy class at runtime. Every method call on the repository goes through `TraceLogger` first:

```
Caller ──▶ TraceLogger.BeforeInvocation() ──▶ Repository.Method() ──▶ TraceLogger.AfterInvocation()
                                                                    └──▶ TraceLogger.OnException() (if throws)
```

**Why this matters:** You should not add `Console.WriteLine` or `NLog` calls inside repositories for tracing — the interceptor handles it. Add logging only for business-level events in the business layer.

**Warning:** If you register a repository class that does not end with "Repository", the interceptor will NOT be applied. Always follow the naming convention.

---

## 6. Factory Pattern

**Where:** Multiple locations — `SqlUnitOfWorkFactory`, `BookingEntryMapperFactory`, `PmsModule` keyed registrations

**Key example — SqlUnitOfWorkFactory:**

```csharp
public class SqlUnitOfWorkFactory : IUnitOfWorkFactory
{
    public UnitOfWork Create() => new SqlUnitOfWork();
    public UnitOfWork Create(TimeSpan timeout) => new SqlUnitOfWork(timeout);
}
```

Inject `IUnitOfWorkFactory` rather than `SqlUnitOfWork` directly, so tests can substitute a mock factory.

**How to extend it safely:**
- Factories should only create objects, never contain business logic.
- Always program to the factory interface, not the concrete factory.

---

## 7. Service Layer Pattern

**Where:** `app/Source/IRIS.Law.Server.DataAccess/` (DataAccessModule), `app/Legal.Cloud/Legal.Cloud.Main/Services/`

**What it is:** Services sit above repositories and below presentation. They orchestrate multiple repository calls and enforce cross-cutting rules (e.g., auditing, authorization checks).

**Example services registered in `DataAccessModule`:**

```csharp
builder.RegisterType<FeeEarnerService>().As<IFeeEarnerService>().InstancePerLifetimeScope();
builder.RegisterType<MatterService>().As<IMatterService>().InstancePerLifetimeScope();
builder.RegisterType<ContactService>().As<IContactService>().InstancePerLifetimeScope();
```

**InstancePerLifetimeScope** means one instance is created per Autofac lifetime scope — effectively one per user session or request context.

---

## 8. Mediator Pattern (Domain-Specific)

**Where:** `app/Source/Legal.Solicitors.Client/Interfaces/IMediator.cs`, `app/Source/IRIS.Law.PmsApplication/PmsWorkflow/Mediators/`

**What it is:** A mediator couples a ViewModel to a Wisej control, providing a protocol of `Initialise`, `UpdateTaskData`, and `Validate` methods. This is a **domain-specific variant** of the mediator concept, not the MediatR library.

**Example:**

```csharp
public interface ICreateDiaryTaskMediator : IMediator
{
    void Initialise(CreateDiaryTaskViewModel viewModel);
    bool Validate(CreateDiaryTaskViewModel viewModel);
    void UpdateTaskData(CreateDiaryTaskViewModel viewModel);
}
```

This pattern is specific to the PMS workflow. Do not confuse it with MediatR or the general CQRS mediator pattern — those are not used here.

---

## 9. Processor / Strategy Pattern

**Where:** `app/Source/IRIS.Law.Server.DataAccess/ChangeProcessing/IProcessor.cs`, PmsModule keyed registrations for `IClientUserTaskProcessor`

**What it is:** Multiple workflow task types (GenerateDocument, ConflictCheck, AttachContact, etc.) implement a common `IProcessor` interface. At runtime, the correct processor for a given `ChangeType` is resolved by key from the Autofac container.

**Extending safely:**
1. Implement `IClientUserTaskProcessor` in a new class.
2. Add a `Keyed` registration in `PmsModule`.
3. The key must match the string the dispatcher uses to resolve the processor.

---

## 10. Singleton Services

**Where:** `IRIS.Law.PmsCommonData.ApplicationSettings`, `IRIS.Law.PmsCommon.UserInformation`

**What they are:** Static singletons holding session-wide state. These are **not injected via DI** — they use the `.Instance` property pattern.

```csharp
// Read user context
int userId = UserInformation.Instance.DbUid;
string userName = UserInformation.Instance.UserName;

// Read application settings
bool postOnLogout = ApplicationSettings.Instance.PostTimesheetsOnLogOut;
```

**Warning:** Do not re-initialize or replace these singletons. They are populated at login and shared across all code in the session. Mutating them outside of the login/authentication flow will cause subtle bugs.

---

## Pattern Summary

| Pattern | Where | Purpose |
|---|---|---|
| Repository | `IRISLegal.SqlRepositories/` | Isolate data access behind interfaces |
| Unit of Work | `IRISLegal/UnitOfWork.cs` | Group operations into one transaction |
| Autofac Module | `*Module.cs` throughout | Organize DI registrations by layer |
| Keyed Strategy | `PmsModule.cs` | Select implementation at runtime by key |
| AOP Decorator | `TraceLogger` + `RepositoryModule` | Cross-cutting logging without touching repositories |
| Factory | `SqlUnitOfWorkFactory`, `BookingEntryMapperFactory` | Abstract object creation |
| Service Layer | `DataAccessModule`, `Legal.Cloud.Main/Services/` | Orchestrate repositories with business rules |
| Domain Mediator | `IMediator`, PMS Workflow Mediators | Couple ViewModels to Wisej controls |
| Processor/Strategy | `IClientUserTaskProcessor` | Dispatch workflow tasks to typed handlers |
| Static Singleton | `ApplicationSettings`, `UserInformation` | Session-wide shared state |
