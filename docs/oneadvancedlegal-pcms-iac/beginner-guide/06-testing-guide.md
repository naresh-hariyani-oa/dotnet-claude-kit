# 06 — Testing Guide

## Current State

There are **no automated test projects** in the `functions/` solution. No xUnit, NUnit, or MSTest projects exist. This is a known gap.

This means:
- There is no `dotnet test` step in CI for the C# code
- Correctness is currently validated through manual testing and integration with real Azure services
- New features must be manually tested before merging

This guide covers how to test effectively within these constraints.

---

## Terraform: Validation and Plan Testing

Terraform has built-in validation that runs in the Harness PR pipelines (`buildbasepr.yaml`, `buildcustomerpr.yaml`). To run them locally:

### Syntax Validation

```bash
cd base        # or customer/, or sas-sql-storage/
terraform validate
```

This checks HCL syntax and basic type errors without connecting to Azure. Always run this before raising a PR.

### Plan Review (Safe Dry Run)

```bash
terraform plan -var-file="nonprod.tfvars.json"
```

Read the plan output carefully:

```
# module.key_vault.azurerm_key_vault.main will be updated in-place
~ resource "azurerm_key_vault" "main" {
    + soft_delete_enabled = true     ← addition: safe
    ~ sku_name = "standard" -> "premium"  ← change: evaluate impact
    - access_policy = []             ← removal: potentially risky
  }
```

**Signals to investigate before applying:**
- Any `- destroy` for a resource you did not intend to delete
- Changes to `key_vault`, `sql_server`, or `application_gateway` modules — these affect all customers
- Changes to `azurerm_subnet` or VNet — networking changes can disconnect resources
- `terraform_remote_state` configuration changes — can break cross-layer references

### Testing a New Customer Config

Before merging a new `config/nonprod/<firmname>.tfvars.json`:

```bash
cd customer
terraform init -backend-config="storage_account_name=..." ...
terraform plan -var-file="../config/nonprod/<firmname>.tfvars.json"
```

Expected: `Plan: X to add, 0 to change, 0 to destroy.` (all additions, no modifications to existing resources).

---

## Azure Functions: Manual Testing Approach

Since no unit tests exist, the testing strategy for functions is:

### 1. Local Smoke Test (against nonprod Azure)

Run the function locally pointed at the nonprod Key Vault and nonprod databases:

```bash
# In local.settings.json set:
# AZURE_KEY_VAULT_ENDPOINT = nonprod Key Vault URI
# Environment = Development (enables interactive auth)

cd functions/Legal.Cloud.Provisioning.Functions
func start
```

Then call the endpoint:
```bash
curl -X POST http://localhost:7071/api/databaseupgrade \
  -H "Content-Type: application/json" \
  -d '{ "firmId": "als-app-ci", ... }'
```

Check the function console output for errors.

### 2. Integration Test via Harness (nonprod)

After merging to the main branch, the `executeautomationtestsnonprod` pipeline runs integration tests against the nonprod environment. These test actual end-to-end flows.

### 3. Verify in Application Insights

After a function runs in Azure (nonprod), check Application Insights:
- Go to Azure Portal → Application Insights resource → Logs
- Query: `traces | where message contains "firmId" | order by timestamp desc`
- Look for errors in the `exceptions` table

---

## Testing the DatabaseUpgrade Function

The database upgrade function is the most critical — bugs here can corrupt customer databases. Test carefully.

### Checklist Before Merging SQL Scripts

- [ ] Script uses `IF NOT EXISTS` or `CREATE OR ALTER` (idempotent)
- [ ] Script runs without error on a clean database
- [ ] Script runs without error when run a second time (idempotent check)
- [ ] Script is listed in `DatabaseUpgrade.json` with correct Order value
- [ ] Order value does not conflict with existing entries
- [ ] `ExecuteOn` flag is correct: 1 = cloud only, 2 = utility only, 0 = both
- [ ] New entry is appended to the correct version block (never modify existing entries)

### Test Against Dev SQL Instance

```bash
# Connect to a dev/test SQL Server
# Run the script manually in SSMS or Azure Data Studio
# Verify it succeeds
# Run it again — verify it succeeds again (idempotency)
```

---

## Testing Harness Pipelines

Pipelines cannot be unit tested. The recommended approach:

1. **Use nonprod first:** All pipelines have nonprod variants. Test there before touching prod.
2. **Review the plan step output:** For Terraform pipelines, the plan output appears in Harness logs. Read it before approving the apply step.
3. **Check approval gates:** Production pipelines have manual approval gates. Do not bypass them.
4. **Monitor the run:** After triggering a pipeline, watch the Harness UI for step failures.

---

## What to Check After Any Infrastructure Change

### After `terraform apply` for base:
- [ ] Application Gateway health probes are healthy (Azure Portal → App Gateway → Backend Health)
- [ ] Key Vault is accessible (test with `az keyvault secret list --vault-name <name>`)
- [ ] All existing customer App Services are still running (Harness: `restartallapps` pipeline)

### After `terraform apply` for a customer:
- [ ] New App Service is running and returns HTTP 200 on health check URL
- [ ] App Service is visible in Application Gateway backend pool
- [ ] SQL Database is accessible (check connection via SSMS)
- [ ] Application Insights shows telemetry from the new App Service

### After deploying Functions:
- [ ] Functions list is complete in Azure Portal → Function App → Functions
- [ ] Timer-triggered functions (SecretRotation) are enabled
- [ ] Application Insights shows no startup errors

---

## Gaps and Recommendations

The following test coverage gaps exist:

| Gap | Risk |
|---|---|
| No unit tests for C# code | Logic errors in functions not caught before deployment |
| No Terraform module tests | Module regressions only caught by plan review |
| No contract tests for Database Upgrade JSON | Malformed entries fail silently until runtime |
| No load tests | Unknown failure modes under concurrent function invocations |

If you add unit tests, the recommended approach for C# is:
- Use **xUnit** (standard for .NET) in a new project `Legal.Cloud.Provisioning.Functions.Tests`
- Use **Moq** or **NSubstitute** for mocking Azure SDK clients
- Focus on `DatabaseOperations.cs` logic first — it has the most complex branching
