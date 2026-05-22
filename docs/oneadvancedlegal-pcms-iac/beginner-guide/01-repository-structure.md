# 01 — Repository Structure

## Top-Level Layout

```
oneadvancedlegal-pcms-iac/
├── base/                        ← Shared Azure infrastructure (deployed once per environment)
├── customer/                    ← Per-customer Azure resources
├── sas-sql-storage/             ← Customer blob storage + SAS token provisioning
├── config/                      ← Customer-specific Terraform variable files
│   ├── nonprod/                 ← Test/CI customer configs (~10 files)
│   ├── prod/                    ← Production customer configs (~270 files)
│   └── template/                ← Cookiecutter template for new customers
├── functions/                   ← C# .NET 8 Azure Functions (post-deployment automation)
├── byobi/                       ← Business Intelligence provisioning prompts
├── environments/                ← Environment variable overrides
├── docs/                        ← Documentation (including this guide)
├── .harness/                    ← Harness CI/CD pipeline YAML definitions
│   ├── *.yaml                   ← Root-level pipelines (build, deploy, backup)
│   └── orgs/Legal/projects/PCMS/
│       ├── pipelines/           ← Application and operational pipelines
│       └── templates/           ← Reusable Harness step templates
├── CLAUDE.md                    ← AI assistant instructions (also read this!)
├── CODEOWNERS                   ← Who approves PRs for which paths
├── PopulateTenantIdentifierDB.ps1  ← One-off CosmosDB seeding script
└── plugin-build-tool.json       ← Harness build tool config
```

---

## Terraform Layers (`base/`, `customer/`, `sas-sql-storage/`)

Each Terraform layer is a **self-contained root module** with its own:
- `main.tf` — resource definitions
- `variables.tf` — input variable declarations
- `outputs.tf` / `output.tf` — exported values
- `locals.tf` — computed local values
- `data.tf` — data source lookups (including remote state)
- `provider.tf` — Azure provider configuration
- `versions.tf` — Terraform and provider version constraints
- `backend.tf` — remote state backend (Azure Storage)

### `base/` — Shared Infrastructure

```
base/
├── main.tf              ← Creates: KV, SQL Server, VNet, Storage, CosmosDB, Service Bus,
│                          Log Analytics, App Gateway, Function App, Logic App, API Web App
├── variables.tf         ← 50+ variables: naming, tags, networking, DB pool config, WAF rules
├── output.tf            ← 40+ outputs consumed by customer layer via remote_state
├── locals.tf            ← Lighthouse assignments, CosmosDB VNet rules
├── data.tf              ← External data lookups
├── network.tf           ← VNet, subnets, NSGs
├── agw.tf               ← Application Gateway / WAF
├── dns.tf               ← DNS zones
├── firewall.tf          ← Azure Firewall rules
├── identity.tf          ← Managed identities, role assignments
├── lighthouse_assignments.tf   ← Azure Lighthouse cross-tenant access
├── app_service_customer_base.tf ← App Service Plan for customers
├── nonprod.tfvars.json  ← Nonprod environment variable values
├── prod.tfvars.json     ← Prod environment variable values
└── modules/             ← 18 reusable sub-modules (see below)
```

**Base modules** — each is a focused, reusable Terraform module:

| Module | Creates |
|---|---|
| `caf_name` | CAF-compliant resource names via `azurecaf_name` |
| `application_gateway` | App Gateway with WAF, SSL, MSI |
| `app_service_plan` | App Service Plan |
| `app_service_certificate_order` | SSL certificate management |
| `cosmosdb` | CosmosDB account + database + container |
| `data_shared_resources_usage` | Usage tracking |
| `dns_zone` | Azure DNS zone |
| `function_app` | Azure Function App |
| `key_vault` | Key Vault with RBAC + secrets |
| `log_analytics_workspace` | Log Analytics workspace |
| `logic_app` | Logic App via ARM template |
| `private_endpoint` | Private endpoint for PaaS services |
| `servicebus_namespace` | Service Bus namespace |
| `servicebus_queue` | Service Bus queue |
| `sql_server` | SQL Server + elastic pools + auditing |
| `storage_account` | Storage account + containers |
| `windows_web_app` | App Service (Windows) |
| `windows_web_app_auth_settings` | AAD B2C auth settings for App Service |

### `customer/` — Per-Customer Resources

```
customer/
├── main.tf              ← B2C setup, app registration, SQL DB creation, deployment scripts
├── variables.tf         ← Customer object shape, SKU maps, SQL creds, B2C config
├── locals.tf            ← Computed values: connection strings, hostnames, B2C URIs
├── data.tf              ← Remote state reference to base layer + KV secret lookups
├── web_app.tf           ← App Service resource
├── inbound_traffic_config.tf  ← App Gateway routing rules for this customer
├── output.tf            ← DB name, server name, URL, App Service ID
├── backend.tf           ← Per-customer state file pointer
├── b2c/                 ← B2C Trust Framework XML policies
├── scripts/             ← Bash/PowerShell: B2C app reg, App Gateway config, Graph perms
└── modules/             ← 5 customer-specific modules
```

**Customer modules:**

| Module | Creates |
|---|---|
| `application_insights` | App Insights resource |
| `azapi_resource` | Generic AzAPI deployment script wrapper |
| `mssql_database` | SQL Database + elastic pool assignment + diagnostics |
| `service_plan` | App Service Plan |
| `windows_web_app` | Customer App Service + auth + IP restrictions |

### `sas-sql-storage/` — SAS Token Provisioning

Small layer that creates a blob storage container for a customer and generates a SAS token, storing it in Key Vault. Used by some workflows to provide time-limited storage access without sharing permanent credentials.

---

## `config/` — Customer Variable Files

Every customer is represented by a single `.tfvars.json` file:

```json
{
  "customer": {
    "name": "firmname",
    "firmDomain": "firm.onmicrosoft.com",
    "isAlBClient": false,
    "dbSku": "GP_Gen5",
    "firmAdTenantId": "...",
    "firmAppClientId": "...",
    ...
  },
  "bacpacUrl": "https://...",
  "sqlAdministratorLogin": "...",
  "sqlAdministratorLoginPassword": "..."
}
```

To add a new customer: copy `config/template/customer_template.json`, fill in values, save as `config/{env}/{firmname}.tfvars.json`.

---

## `functions/` — C# .NET 8 Azure Functions

```
functions/
├── Legal.Cloud.Provisioning.Functions.sln   ← Solution file (open this in VS/Rider)
├── Legal.Cloud.Provisioning.Functions/      ← Main Azure Functions host project
│   ├── Program.cs                           ← DI setup, app bootstrap
│   ├── host.json                            ← Function runtime config (timeouts, retries)
│   ├── *.cs                                 ← Individual function classes (one per domain)
│   ├── Common/                              ← Constants, enums, KeyVaultProvider
│   ├── OneDriveIntegration/                 ← Managers, Models, Persistence, Providers
│   └── Tenants/                             ← Cosmos DB tenant models and providers
├── Legal.Cloud.Provisioning.Common/         ← Shared auth, Key Vault, models
│   ├── Managers/                            ← AuthenticationManager, GraphClientManager, KeyVaultManager
│   ├── Models/                              ← Firm, DatabaseVersionSchema, O365TenantApplications, etc.
│   └── Providers/                           ← TokenProvider
├── Legal.Cloud.DatabaseUpgrade/             ← Database migration engine
│   ├── DatabaseOperations.cs               ← Upgrade orchestration logic
│   ├── DatabaseUpgrade.json                ← Version/script manifest
│   ├── SendEmail.cs                         ← SendGrid email notifications
│   └── Data/                               ← SQL scripts (tables/, functions/)
├── Legal.Cloud.UdfDatabaseUpgrade/          ← UDF-specific migration engine (same shape)
├── Legal.Cloud.Provisioning.Console/        ← CLI for running DB upgrades locally
└── Legal.Cloud.Udf.Console/                ← CLI for UDF DB upgrades locally
```

**Excluded from compilation** (legacy/disabled):
- `DatabaseImport.cs`
- `DatabaseRestore.cs`

---

## `.harness/` — CI/CD Pipelines

Harness (not GitHub Actions or Azure DevOps) is the CI/CD platform. Pipelines are YAML files in two locations:

```
.harness/
├── *.yaml                                        ← Root pipelines (build, deploy, terraform ops)
└── orgs/Legal/projects/PCMS/
    ├── pipelines/                                ← Application and operational pipelines
    ├── templates/
    │   ├── pipeline/                             ← Reusable pipeline templates
    │   └── step/                                 ← Reusable step templates
    └── inputSets/                                ← Parameter sets for pipeline runs
```

Key root pipelines:
- `deploybasenonprod.yaml` / `deploybaseprod.yaml` — Deploy shared base infra
- `deployapplicationnonprod.yaml` / `deployapplicationprod.yaml` — Deploy customer app
- `deployfunctionsnonprod.yaml` — Build and deploy Azure Functions
- `terraformprovisioncustomer.yaml` — Full customer provisioning
- `backupcustomerdatabase.yaml` — Database backup

---

## Files You Will Edit Most Often

| Scenario | File(s) to Edit |
|---|---|
| Add a new production customer | `config/prod/<firmname>.tfvars.json` (new file) |
| Change shared infrastructure | `base/main.tf`, `base/variables.tf` |
| Change customer app configuration | `customer/main.tf`, `customer/variables.tf` |
| Add a new database migration | `functions/Legal.Cloud.DatabaseUpgrade/Data/` + `DatabaseUpgrade.json` |
| Add a new Azure Function | `functions/Legal.Cloud.Provisioning.Functions/<NewFunction>.cs` |
| Modify a CI/CD pipeline | `.harness/*.yaml` or `.harness/orgs/.../pipelines/*.yaml` |

---

## Files You Should NOT Casually Edit

| File/Path | Reason |
|---|---|
| Any file with `<!-- BEGIN_TF_DOCS -->` | Auto-generated — will be overwritten |
| `config/prod/*.tfvars.json` | Requires `@advancedcsg/pcms-maintain` PR approval |
| `base/modules/` | Shared by all customers — changes affect everything |
| Existing SQL scripts in `DatabaseUpgrade/Data/` | Migration scripts — never edit, only add new |
| `customer/data.tf` | Remote state config — follow exact pattern already there |
