# 10 — Safe Development Workflow

## Risk Classification of Changes

Before making any change, classify its risk:

| Change Type | Risk | Required Steps |
|---|---|---|
| New customer config (`config/nonprod/`) | Low | terraform plan review, PR |
| New customer config (`config/prod/`) | Med | terraform plan review, PR, `@advancedcsg/pcms-maintain` approval |
| Customer module change (`customer/`) | Med | terraform plan for multiple customers, PR |
| New Azure Function (additive) | Med | Local test, nonprod deploy test, PR |
| Base module change (`base/modules/`) | **High** | Read impact section below |
| Base main.tf change | **High** | Read impact section below |
| Existing SQL migration script | **High** | **Never modify — append only** |
| Harness pipeline change | Med | Test in nonprod pipeline first |
| Secrets or Key Vault structure | **High** | Coordinate with team, verify all consumers |

---

## Safe Workflow for Any Change

### Step 1: Understand Before Touching

Before editing anything:
1. Read the file you are about to change.
2. Identify everything that depends on it (who calls this module? who reads this output?).
3. Run `terraform plan` or build the solution before making changes, so you have a baseline.

### Step 2: Make the Smallest Possible Change

Do not fix multiple unrelated things in one PR. Focused changes are easier to review, easier to roll back, and have smaller blast radius when something goes wrong.

### Step 3: Validate Locally First

**For Terraform:**
```bash
terraform validate      # syntax check
terraform fmt -check    # formatting check
terraform plan          # dry run — read the entire output
```

**For C#:**
```bash
dotnet build            # compilation
func start              # smoke test locally
```

### Step 4: Test Against Nonprod

All changes should be applied to nonprod before prod. For infrastructure changes, this means running the nonprod pipeline first and verifying:
- No unexpected resource changes
- Existing customer apps still work
- New resources are correctly configured

### Step 5: PR Review

Write a clear PR description using the template in `.github/pull_request_template.md`. Include:
- Why you are making this change
- What specifically changed
- Impact area (which customers, which environments)
- Risk level
- How you tested it

### Step 6: Merge and Monitor

After merging:
- Watch the CI/CD pipeline run in Harness
- Monitor Application Insights for errors in the 30 minutes following deployment
- For base changes, check all customer App Services are still healthy

---

## High-Risk Areas — Read This Before Touching

### `base/modules/` — Shared Infrastructure Modules

**Why risky:** These modules are used by the base layer which is deployed once and shared by all customers. A bug introduced here affects every customer simultaneously.

**Before modifying:**
1. Run `terraform plan` against nonprod with the change applied
2. Verify the plan only modifies what you intend — no unexpected rebuilds
3. Check if the module outputs are consumed by `customer/data.tf` or `locals.tf` — changes to outputs can break customer deployments
4. Get a second review from a team member who knows the module

**Especially risky resources:**
- `application_gateway` — routing changes affect all customer traffic
- `key_vault` — access policy or RBAC changes affect all services
- `sql_server` — elastic pool changes affect all customer databases
- `vnet/network` — subnet changes can disconnect private endpoints

---

### `customer/data.tf` and `customer/locals.tf`

**Why risky:** These files compute values used by every single customer deployment. A bug here means every customer deployment fails.

**Before modifying:**
1. Run `terraform plan` for at least two different customer configs
2. Verify locals compute correctly for customers with different configurations (different elastic pool index, different app service plan index, etc.)
3. Do not change the `data.terraform_remote_state.base` configuration without understanding the key pattern

---

### `functions/Legal.Cloud.DatabaseUpgrade/Data/` and `DatabaseUpgrade.json`

**Why risky:** The database upgrade system modifies customer SQL databases. A wrong migration can corrupt data for hundreds of customers.

**Rules:**
1. Never modify existing SQL scripts in the `Data/` folder
2. Never modify existing entries in `DatabaseUpgrade.json`
3. Only append new version entries
4. Test all new scripts for idempotency (runs cleanly a second time without errors)
5. Test on a non-production database first

---

### `base/agw.tf` and Application Gateway Configuration

**Why risky:** The Application Gateway is the single entry point for all customer traffic. Misconfiguring it takes all customers offline.

**Before modifying:**
1. Verify you understand the listener/rule/backend pool relationships
2. Run terraform plan and verify only the intended AGW rules change
3. After applying, check AGW backend health (Azure Portal → AGW → Backend Health) for all pools
4. Test a customer URL immediately after applying

---

### `SecretRotation.cs` and Key Vault Secret Names

**Why risky:** The secret rotation function updates credentials for all O365 tenant applications. A naming mismatch between what is stored in CosmosDB and what is stored in Key Vault breaks authentication for those tenants.

**Before modifying:**
1. Understand the naming convention used for Key Vault secrets (clientId + `_Secret`)
2. Do not change secret naming without updating both the rotation function and all consumers
3. Test by verifying one tenant application's credentials are correctly rotated

---

## Tightly Coupled Components — Handle With Care

| Component | What It's Coupled To | Why |
|---|---|---|
| `base/output.tf` | `customer/data.tf` locals | Every output is referenced by customer layer |
| `customer/modules/mssql_database` | `base/modules/sql_server` | Elastic pool IDs flow from base outputs |
| `DatabaseUpgrade.json` | `DatabaseOperations.cs` | JSON schema must match the parser exactly |
| `KeyVaultProvider.cs` + `KeyVaultManager.cs` | Every function class | All secrets access flows through these |
| `TenantDBProvider.cs` | `O365TenantAppManagementFunction.cs` + `SecretRotation.cs` | Both read/write CosmosDB via same provider |
| `host.json` | All function triggers | Timeout and retry settings affect all functions |

---

## Safe Extension Points

These are the areas where adding new functionality is relatively low risk:

| Extension Point | What You Can Safely Add |
|---|---|
| `config/nonprod/` or `config/prod/` | New customer configs (additive, isolated) |
| New `.cs` file in Functions project | New function (no changes to existing files) |
| `DatabaseUpgrade/Data/` + `DatabaseUpgrade.json` | New SQL scripts + new version entry (append only) |
| `customer/scripts/` | New deployment scripts (not called unless wired up) |
| New Harness pipeline YAML | New pipeline (no impact on existing pipelines) |
| `Legal.Cloud.Provisioning.Common/Models/` | New model classes (no impact on existing) |

---

## Recommended Learning Order for New Developers

Follow this order to build understanding incrementally before making changes:

1. **Read** [00-project-overview.md](00-project-overview.md) — understand what the system does
2. **Read** [01-repository-structure.md](01-repository-structure.md) — understand the folder layout
3. **Read** [02-architecture-overview.md](02-architecture-overview.md) — understand how layers relate
4. **Browse** `config/nonprod/als-app-ci.tfvars.json` — see what a customer config looks like
5. **Browse** `customer/main.tf` and `customer/locals.tf` — see how configs become resources
6. **Browse** `base/output.tf` — see what the base layer publishes
7. **Browse** `functions/Legal.Cloud.Provisioning.Functions/DatabaseUpgrade.cs` — see a simple function
8. **Browse** `functions/Legal.Cloud.DatabaseUpgrade/DatabaseUpgrade.json` — see the migration manifest
9. **Read** [04-request-flow.md](04-request-flow.md) — trace the full onboarding flow
10. **Read** [09-common-pitfalls.md](09-common-pitfalls.md) — avoid known mistakes
11. **Read** [07-coding-standards.md](07-coding-standards.md) — before writing code
12. **Make a small change** — add a nonprod customer config and run terraform plan

Do not make production changes until you have made at least a few nonprod changes and understand how the pipelines work.

---

## Before Raising a PR — Checklist

- [ ] `terraform validate` passes (for Terraform changes)
- [ ] `terraform plan` reviewed — no unexpected destroys
- [ ] `dotnet build` passes (for C# changes)
- [ ] No hardcoded credentials, connection strings, or API keys
- [ ] No SQL string interpolation (SQL injection risk)
- [ ] All new async methods use `await`, not `.Result`
- [ ] All new Azure resources use `azurecaf_name` for naming
- [ ] All new Azure resources have required tags
- [ ] PR description follows the template
- [ ] Impact area is accurately described
- [ ] Risk level is honestly assessed (when in doubt, go higher)
- [ ] Tested in nonprod before requesting prod approval
