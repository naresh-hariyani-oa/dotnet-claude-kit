# 00 — Project Overview

## What Is This Repository?

This repository is the **Infrastructure as Code (IaC)** codebase for **PCMS Legal Cloud** — a multi-tenant SaaS platform that deploys isolated Azure infrastructure for hundreds of law firms.

Each law firm (called a **customer** or **firm**) gets their own:
- Windows App Service (the web application)
- SQL Database (firm-specific legal data)
- Application Insights (telemetry)
- B2C Authentication integration (Azure AD B2C)

All firms share centrally-managed infrastructure: SQL servers, Application Gateway (reverse proxy + WAF), Key Vault, Log Analytics, Service Bus, CosmosDB, and VNet.

---

## What Does "IaC" Mean Here?

Infrastructure as Code means that Azure cloud resources are defined in configuration files checked into Git, not clicked together in the Azure Portal. This repository uses **Terraform** to declare what Azure resources should exist. Harness CI/CD pipelines run `terraform apply` to make Azure match that declaration.

If you want to add a new customer or change a configuration, you edit a file in this repo and the pipeline handles the rest.

---

## Who Uses This?

| Role | What They Do |
|---|---|
| Platform / Infra engineers | Modify shared base infrastructure |
| Customer onboarding team | Add new customer configs, run provisioning pipelines |
| DevOps / Release engineers | Update and run Harness pipelines |
| C# developers | Maintain Azure Functions that run post-deployment provisioning steps |

---

## Technology Stack at a Glance

| Layer | Technology | Purpose |
|---|---|---|
| Infrastructure | Terraform >= 1.0 | Declare Azure resources as code |
| Naming | azurecaf 2.0.0-preview3 | Enforce Cloud Adoption Framework names |
| Azure Providers | azurerm 3.x, azuread 2.x, azapi 1.x | Interact with Azure APIs |
| Provisioning Automation | C# .NET 8, Azure Functions v4 | Post-deployment tasks (DB upgrades, B2C, OneDrive) |
| CI/CD | Harness | Pipeline orchestration |
| Secrets | Azure Key Vault | All credentials — never in code |
| Identity | Azure Managed Identity | Service-to-service auth — no stored passwords |
| Observability | Application Insights + Log Analytics | Telemetry, logs, metrics |

---

## High-Level System Diagram

```
┌──────────────────────────────────────────────────────────┐
│                      Azure Subscription                  │
│                                                          │
│  ┌──────────────────────────────────────────────────┐    │
│  │                  BASE LAYER (shared)             │    │
│  │  App Gateway (WAF) ─── VNet ─── Key Vault        │    │
│  │  SQL Server ─── CosmosDB ─── Service Bus         │    │
│  │  Log Analytics ─── Storage ─── Function App      │    │
│  └──────────────────────────────────────────────────┘    │
│            ▲ referenced via terraform_remote_state       │
│                                                          │
│  ┌────────────────────┐  ┌────────────────────┐          │
│  │  CUSTOMER: firmA   │  │  CUSTOMER: firmB   │  ...     │
│  │  App Service       │  │  App Service       │          │
│  │  SQL Database      │  │  SQL Database      │          │
│  │  App Insights      │  │  App Insights      │          │
│  └────────────────────┘  └────────────────────┘          │
└──────────────────────────────────────────────────────────┘
```

---

## Environments

| Environment | Purpose | Config Folder |
|---|---|---|
| nonprod | Development, QA, CI testing | `config/nonprod/` |
| prod | Live customer deployments (270+ firms) | `config/prod/` |

---

## Key Numbers (as of this writing)

- **~270 production customers** (one `.tfvars.json` per firm in `config/prod/`)
- **10 nonprod test environments** in `config/nonprod/`
- **106 Harness pipeline YAML files**
- **18 base Terraform modules**
- **5 customer Terraform modules**
- **6 C# projects** in the functions solution

---

## What Happens When a New Customer Is Onboarded?

1. A new `config/prod/<firmname>.tfvars.json` file is created
2. A Harness pipeline runs `terraform apply` for the customer layer
3. Azure Functions are invoked to: upgrade the database schema, configure B2C, set up OneDrive, provision BYOBI (optional)
4. The Application Gateway is updated to route `<firmname>.legalcloud.com` traffic to the new App Service
5. The firm is live

---

## Where to Go Next

| Topic | Document |
|---|---|
| Repository folder structure | [01-repository-structure.md](01-repository-structure.md) |
| How the three Terraform layers work together | [02-architecture-overview.md](02-architecture-overview.md) |
| Design patterns in the codebase | [03-design-patterns.md](03-design-patterns.md) |
| Step-by-step provisioning flows | [04-request-flow.md](04-request-flow.md) |
| How to build and run things locally | [05-build-and-run.md](05-build-and-run.md) |
| Coding standards | [07-coding-standards.md](07-coding-standards.md) |
| Risky areas and safe development guidelines | [10-safe-development-workflow.md](10-safe-development-workflow.md) |
