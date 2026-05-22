# 01 — Repository Structure

## Top-Level Layout

```
oneadvancedlegal-pcms-api/
├── api/                          # All C# source code lives here
│   ├── IntegrationApi/           # Startup project (entry point)
│   ├── Business/                 # Business logic layer — 32 domain projects
│   ├── Data/                     # Data access layer — 31 domain projects
│   ├── Common/                   # Shared infrastructure — 9 projects
│   └── Test/                     # Tests — 2 projects
├── .harness/                     # CI/CD pipeline definitions (Harness)
├── docs/                         # Documentation (this folder)
├── CLAUDE.md                     # AI assistant instructions
└── .github/                      # GitHub configuration
```

## The `api/` Directory in Detail

### `IntegrationApi/` — The Startup Project

This is the **only runnable project**. Everything else is a library it references.

```
api/IntegrationApi/
├── Controllers/                  # HTTP controllers, organised by domain
│   ├── Accounts/v1/
│   ├── Client/v1/
│   ├── Matter/v1/
│   ├── Document/v1/
│   └── ... (one subfolder per domain)
├── Program.cs                    # Application entry point, DI wiring, middleware pipeline
├── appsettings.json              # Base configuration
├── appsettings.Development.json  # Local overrides
├── Properties/launchSettings.json
└── Logs/                         # Runtime log files (gitignored)
```

**Rule:** Never add business logic to a controller. Controllers only orchestrate — they call a service and return its result.

### `Business/` — Business Logic Layer

Each domain has its own project named `Legal.Pcms.Integration.Api.Business.{Domain}`.

```
api/Business/
├── Legal.Pcms.Integration.Api.Business.Matter/
│   └── v1/
│       ├── IMatterService.cs             # Interface
│       └── Implementation/
│           └── MatterService.cs          # Concrete implementation
├── Legal.Pcms.Integration.Api.Business.Client/
├── Legal.Pcms.Integration.Api.Business.Document/
├── Legal.Pcms.Integration.Api.Business.Note/
├── Legal.Pcms.Integration.Api.Business.JCode/
├── Legal.Pcms.Integration.Api.Business.Models/   # DTOs shared across Business
└── ... (one project per domain)
```

**Rule:** Business projects know nothing about HTTP. They receive and return DTOs. They call repositories via injected interfaces.

### `Data/` — Data Access Layer

Mirror structure to Business — one project per domain named `Legal.Pcms.Integration.Api.Data.{Domain}`.

```
api/Data/
├── Legal.Pcms.Integration.Api.Data.Matter/
│   └── v1/
│       ├── IMatterRepository.cs          # Interface
│       └── Implementation/
│           └── MatterRepository.cs       # Concrete implementation
├── Legal.Pcms.Integration.Api.Data.Client/
├── Legal.Pcms.Integration.Api.Data.Models/  # Database entity classes
└── ... (one project per domain)
```

**Rule:** Data projects know nothing about HTTP or business rules. They execute queries and return entities.

### `Common/` — Shared Infrastructure

Nine projects providing infrastructure that every layer uses:

| Project | Purpose |
|---|---|
| `Legal.Pcms.Api.Common` | Core abstractions: `IRequestService`, constants (`AppConstants`), base exceptions |
| `Legal.Pcms.Api.Common.Business` | `BaseService<TEntity, TEntityRepo, TKey>` — base class all services inherit |
| `Legal.Pcms.Api.Common.Data` | `BaseRepository<TEntityRepo, TKey>`, `AppDbContext`, `IMultiTenantService` |
| `Legal.Pcms.Api.Common.Models` | DTOs and view models shared across the whole solution |
| `Legal.Pcms.Api.Common.Logging` | `IBaseLogger` — structured logging abstraction over Serilog |
| `Legal.Pcms.Api.Common.Caching` | Caching abstractions (In-Memory / Redis) |
| `Legal.Pcms.Api.Common.Web` | Middleware: `ExceptionHandlingMiddleware`, `SecurityHeadersMiddleware`, `IpValidateMiddleware`, CORS, Swagger wiring |
| `Legal.Pcms.Api.Common.Test` | Test utilities and shared fixtures |

> **Warning:** Changes to Common projects affect **every** domain. Always run the full test suite before merging Common changes.

### `Test/` — Test Projects

```
api/Test/
├── Business/
│   └── Legal.Pcms.Integration.Api.Business.Test/
│       ├── AccountTest/
│       ├── MatterTest/
│       ├── ClientTest/
│       └── ... (one subfolder per domain)
└── Data/
    └── Legal.Pcms.Integration.Api.Data.Test/
```

See [06-testing-guide.md](06-testing-guide.md) for patterns.

## Domain List

These are the business domains currently implemented. Each appears in all three layers (Controller / Business / Data):

| Domain | Description |
|---|---|
| Accounts | Billing codes, VAT rates, charge descriptions |
| AddressTypes | Address type lookups |
| ApplicationSettings | System parameter management |
| Audit | API request/response audit trail |
| Branches | Office branch management |
| BusinessSources | Lead/referral source lookups |
| ChargeDescriptions | Charge description lookups |
| Client | Client creation, update, search |
| Contact | Contact management |
| ContactMethods | Contact method lookups |
| Courts | Court lookups |
| CustomFields | Custom field definitions and values (separate database) |
| Departments | Department management |
| Disbursement | Disbursement records |
| Document | Document upload, download, folder navigation |
| FeeEarners | Fee earner lookups |
| Flow | Workflow/flow management |
| Identity | AML identity verification |
| JCode | Activity, task, phase, stage lookups |
| Jobs | Async job tracking |
| Matter | Matter/project management — the most complex domain |
| MatterContactAssociations | Links between matters and contacts |
| MatterTypes | Matter type lookups |
| Metadata | Field/entity metadata definitions |
| Note | Note creation and management |
| Order | Order management |
| ProjectTypes | Project type lookups |
| PropertyTypes | Property type lookups |
| TenureTypes | Tenure type lookups |
| Time | Time recording |
| Title | Salutation/title lookups |

## Naming Convention for Files

When you see a file name, you can decode it:

| Pattern | Meaning |
|---|---|
| `I{Domain}Service.cs` | Service interface in Business layer |
| `{Domain}Service.cs` | Service implementation |
| `I{Domain}Repository.cs` | Repository interface in Data layer |
| `{Domain}Repository.cs` | Repository implementation |
| `{Domain}Profile.cs` | AutoMapper profile mapping DTO ↔ Entity |
| `{Domain}Controller.cs` | HTTP controller in IntegrationApi |
| `{Domain}Fixture.cs` | xUnit test fixture |
| `{Domain}ServiceTest.cs` | xUnit test class |
| `{Domain}DataSource.cs` | Test data provider for `[MemberData]` |
