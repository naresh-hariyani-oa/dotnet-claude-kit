# 01 — Repository Structure

## Top-Level Layout

```
oneadvancedlegal-pcms/
├── app/                        ← All C# source code
├── docs/                       ← Technical documentation
│   ├── beginner-guide/         ← This guide
│   └── performance/            ← Performance analysis and plans
├── .github/                    ← GitHub Actions workflows, PR templates, Copilot config
├── .harness/                   ← Harness CI/CD pipeline YAML definitions
├── .vscode/                    ← VS Code workspace settings
├── CLAUDE.md                   ← Development guidance (MUST READ)
├── CPMReadme.md                ← NuGet Central Package Management guide
├── CODEOWNERS                  ← Code ownership for PR reviews
└── Directory.Packages.props    ← Central NuGet version definitions
```

---

## The `app/` Directory — Detailed Breakdown

The `app/` folder contains everything. It is organized as follows:

```
app/
├── Legal.Cloud.sln                     ← MAIN SOLUTION — use this for development
├── Legal.Cloud/                        ← Core web application projects
├── Legal.Domain/                       ← Domain model projects
├── Legal.Integration.Mapper/           ← External system field-mapping engine
├── Legal.NWRIntegration/               ← National Will Register integration
├── LegalCloud.UnitTests/               ← ALL unit tests (single test project)
├── Solicitors.WebApi/                  ← Independent REST microservices
├── Solicitors.Diary/                   ← Diary/calendar system
├── Solicitors.Workflow/                ← Workflow engine
├── Solicitors.InteropToOpenXml/        ← Document production / mail-merge
├── Source/                             ← Legacy IRIS.Law application (migrated from WinForms)
├── SqlScripts/                         ← Database scripts (stored procs, views, migrations)
├── Tools/                              ← Internal developer tools
├── Packages/                           ← DevExpress and Wisej NuGet packages (local feed)
└── Template_Conversion_Tool/           ← Template format converter utility
```

---

## `app/Legal.Cloud/` — Core Web Application

This is where you will spend most of your time as a new developer.

```
Legal.Cloud/
├── Legal.Cloud/                        ← Main web app project (entry point)
│   ├── Program.cs                      ← Application entry point
│   ├── Startup.net48.cs                ← OWIN startup (.NET 4.8)
│   ├── Startup.netcore.cs              ← ASP.NET Core startup (.NET 7)
│   ├── Web.config                      ← IIS / OWIN configuration
│   ├── appsettings.json                ← Application settings
│   └── otel/netfx/                     ← Grafana OpenTelemetry DLLs (not NuGet)
│
├── Legal.Cloud.Main/                   ← Application shell, startup, navigation
│   ├── CloudApplicationStartup.cs      ← Service registration and app init
│   ├── Services/
│   │   ├── ConfigurationManager.cs     ← App configuration access
│   │   ├── NavigationManager.cs        ← Screen navigation
│   │   ├── UrlManager.cs               ← URL routing
│   │   └── BackgroundTasksService.cs   ← Background job runner
│   └── Controls/                       ← Shared UI controls (MainView, Splash, etc.)
│
├── Legal.Cloud.Azure/                  ← Azure Blob, CosmosDB, AD integration
├── Legal.Cloud.Bubbles/                ← UI tooltip/bubble components
├── Legal.Cloud.ColumnFilter/           ← Grid column filter controls
├── Legal.Cloud.DxDashboard/            ← DevExpress dashboard integration
├── Legal.Cloud.DxRichEditor/           ← Rich text editor component
├── Legal.Cloud.Exchange/               ← Microsoft Exchange / Outlook integration (49 files)
├── Legal.Cloud.Icons/                  ← Application icon resources
├── Legal.Cloud.Integrations/           ← Third-party service integrations
├── Legal.Cloud.NavigationBar/          ← Top navigation menu component
├── Legal.Cloud.Notifications/          ← User notification system
├── Legal.Cloud.Office365/              ← Teams, SharePoint, Office 365 integration (25 files)
├── Legal.Cloud.Reporting/              ← Report definitions — DevExpress reports (327 files, largest module)
├── Legal.Cloud.Security/               ← Authentication / authorization support
└── Legal.Cloud.Webjob/                 ← Background job processing (Azure WebJob)
```

---

## `app/Source/` — Legacy IRIS.Law Application

This is the ported WinForms application. It is organized by **business domain**, and each domain follows a consistent **four-layer pattern**:

```
IRIS.Law.{Domain}Application/   ← UI forms and controls (Wisej, ported from WinForms)
IRIS.Law.{Domain}Business/      ← Business logic (Br* classes: BrMatter, BrClients, etc.)
IRIS.Law.{Domain}Data/          ← Data access (DataSets, stored procedures)
IRIS.Law.{Domain}CommonData/    ← Shared DTOs and constants for this domain
```

**Major domains present:**

| Domain prefix | Business area |
|---|---|
| `IRIS.Law.Pms*` | Practice Management System (largest domain) |
| `IRIS.Law.Accounts*` | Accounts and financial management |
| `IRIS.Law.Diary*` | Calendar and appointments |
| `IRIS.Law.Dm*` | Document management |
| `IRIS.Law.CloudDMS*` | Cloud document management |
| `IRIS.Law.DocumentProduction` | Document generation |
| `IRIS.Law.Convey*` | Conveyancing |
| `IRIS.Law.Probate*` | Probate / will administration |
| `IRIS.Law.PersonalInjury*` | PI claims |
| `IRIS.Law.Family*` | Family law |

**Shared infrastructure (cross-cutting, used by all domains):**

| Project | Purpose |
|---|---|
| `IRISLegal/` | Core utilities, Unit of Work, base classes |
| `IRISLegal.Infrastructure/` | Autofac modules, integration services |
| `IRISLegal.SqlRepositories/` | SQL Repository base classes, EF contexts, Autofac `RepositoryModule` |
| `IRISLegal.EntityFramework/` | Entity Framework 6 DbContext configurations |
| `IRISLegal.Logging/` | NLog-based logging with Autofac interceptor support |
| `IRISLegal.Fields/` | Master Autofac `ContainerConfiguration` — wires the entire DI container |
| `IRIS.Law.Entities/` | Core entity definitions used across domains |
| `IRIS.Law.MetaData/` | Metadata model definitions |
| `IRIS.Law.ControlLib/` | Reusable Wisej UI controls |
| `IRIS.Law.Services/` | Core service abstractions and interfaces |
| `IRIS.Law.Server.DataAccess/` | Data access services (FeeEarnerService, MatterService, ContactService) |
| `IRIS.Law.PmsCommon/` | PMS-wide constants, `UserInformation`, `ApplicationSettings` |
| `IRIS.Law.PmsCommonData/` | PMS shared data models |
| `IRIS.Law.PmsInfrastructure/` | PMS-specific Autofac module (`PmsModule`) |
| `Legal.CloudDMSApi/` | Cloud DMS API abstraction layer |

---

## `app/Solicitors.WebApi/` — Independent REST Microservices

These are **standalone web API projects** that run independently. Each has its own build output, Web.config, and DI setup. They do not reference each other via project references — only via REST.

```
Solicitors.WebApi/
├── Solicitors.Authentication/          ← User login and authentication
├── Solicitors.BillingCodes/            ← Billing code management
├── Solicitors.Client/                  ← Client / contact API
├── Solicitors.Dms/                     ← Document management API
├── Solicitors.FeeEarners/              ← Fee earner management
├── Solicitors.Matter/                  ← Matter management API
└── Solicitors.TimeActivities/          ← Time recording API
```

Each microservice follows this internal structure:

```
Solicitors.{Service}/
├── Adapters/
│   └── {Service}Controller.cs          ← REST endpoints
├── App_Start/
│   ├── DefaultDependencyResolver.cs    ← DI container setup
│   └── WebApiConfig.cs                 ← Route configuration
├── Domain/                             ← Domain models
├── Ports/                              ← Response/request models
└── Global.asax.cs                      ← OWIN pipeline
```

---

## `app/Legal.Domain/` — Domain Model Projects

These projects contain **clean domain model interfaces and implementations** used by the web layer for data binding and display. They are separate from the legacy IRIS.Law entity models.

```
Legal.Domain/
├── Legal.Contact/          ← IContactQueryModel, ContactQueryModel
├── Legal.Matter/           ← IMatterQueryModel, MatterQueryModel
├── Legal.Will/             ← IWillQueryModel, WillQueryModel
├── Legal.Deed/             ← IDeedQueryModel, DeedQueryModel
├── Legal.Debt.Matter/      ← Debt matter models
├── Legal.Licensing/        ← License entity models
├── Legal.UdFields/         ← User-defined field metadata
└── Monads/                 ← Functional programming utilities (Result, Option types)
```

---

## `app/LegalCloud.UnitTests/` — All Tests in One Project

All unit tests live in a **single test project**. It is organized into subdirectories mirroring the domain structure:

```
LegalCloud.UnitTests/
├── BaseTestFixture.cs                              ← Base class for all tests
├── Cloud/
│   ├── Legal.Cloud.Office365.UnitTests/
│   └── Legal.Cloud.Reporting.UnitTests/
├── DocumentProduction/
├── IRIS.Law.AccountsApplication.UnitTest/
├── IRIS.Law.Diary.Services.UnitTest/
├── IRIS.Law.PmsApplication.UnitTest/
├── IRIS.Law.PmsBusiness.UnitTest/
├── IRIS.Law.PmsData.UnitTest/
├── IRIS.Law.Services.UnitTest/
├── IRISLegal.SqlRepositories.UnitTest/
├── IRISLegal.Infrastructure.UnitTest/
├── Legal.Cloud/
├── InfoTrackUnitTest/
├── Workflow/
└── UtilityForms/
```

---

## `app/SqlScripts/` — Database Scripts

```
SqlScripts/
├── functions/          ← SQL scalar and table-valued functions
├── sprocs/             ← Stored procedures
├── views/              ← Database views
├── up/                 ← Schema migration / upgrade scripts
└── onboarding/         ← Demo data and environment setup scripts
```

---

## `app/Solicitors.Diary/` — Diary / Calendar System

```
Solicitors.Diary/
├── IRIS.Law.Diary.Services/            ← Business logic for diary operations
├── IRIS.Law.Diary/                     ← Diary UI forms (Wisej)
├── IRIS.Law.DiaryControlLib/           ← Reusable diary controls
├── IRIS.Law.DiaryProviders.ExchangeProvider/   ← Exchange calendar sync
└── IRIS.Law.DiaryProviders.MSDiaryProvider/    ← Microsoft Diary provider
```

---

## `app/Solicitors.Workflow/` — Workflow Engine

```
Solicitors.Workflow/
├── IRIS.Law.DesignerStudio.Shared/         ← Workflow designer utilities
├── IRIS.Law.Server.Workflow.Activities/    ← Activity definitions
├── IRISLegal.UI.UserDefinedScreens/        ← Dynamic screen generation
└── Solicitors.Service.Workflow/            ← Core workflow service
```

---

## Solution Files

There are multiple `.sln` files, but you only need to know about one for day-to-day development:

| Solution | Purpose |
|---|---|
| **`app/Legal.Cloud.sln`** | **PRIMARY — use this for all development** |
| `app/Solicitors.WebApi/Solicitors.WebApi.sln` | Microservices only (independent build) |
| `app/Source/_BuildSolution.sln` | Legacy IRIS.Law source (CI/CD compilation) |
| `app/Legal.Cloud/Legal.Cloud.Main/Legal.Cloud.Main.sln` | NavigationBar/Main modules only |

---

## Package Management

NuGet versions are managed centrally in [`Directory.Packages.props`](../../Directory.Packages.props) at the repo root. **Never specify a version in a `.csproj` file** — put it in `Directory.Packages.props` instead.

Two exceptions to this rule:
1. **Grafana OpenTelemetry DLLs** — referenced directly from `app/Legal.Cloud/Legal.Cloud/otel/netfx/` (not NuGet). See [`CPMReadme.md`](../../CPMReadme.md).
2. **DevExpress / Wisej packages** — served from the local `app/Packages/` folder (a local NuGet feed).

See [`CPMReadme.md`](../../CPMReadme.md) for full details on adding or updating packages.
