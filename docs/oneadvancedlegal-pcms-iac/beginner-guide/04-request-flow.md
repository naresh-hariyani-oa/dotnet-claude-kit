# 04 — Application Flows

This document explains the major end-to-end flows in the system, step by step.

---

## Flow 1: Onboarding a New Customer

This is the most important flow to understand. It combines Terraform provisioning with Azure Function invocations.

```
Developer / Harness Pipeline
        │
        ▼
1. Create config/prod/<firmname>.tfvars.json
        │
        ▼
2. terraform apply (customer layer)
   ├── Creates SQL Database (on shared SQL server)
   ├── Creates Windows App Service
   ├── Creates Application Insights
   ├── Runs B2C app registration deployment script
   ├── Uploads B2C Trust Framework XML custom policy
   └── Updates Application Gateway routing
        │
        ▼
3. Harness pipeline invokes: DatabaseUpgrade HTTP function
   ├── Function reads Firm object from request body
   ├── Fetches DB connection string from Key Vault
   ├── Reads DatabaseUpgrade.json (version manifest)
   ├── Compares current DB version against target version
   ├── Executes SQL scripts in order (tables, functions)
   ├── Updates DatabaseUpgradeHistory table
   ├── Sends success/failure email via SendGrid
   └── Returns HTTP 200 or error
        │
        ▼
4. (Optional) Harness pipeline invokes: OneDriveIntegration HTTP function
   ├── Authenticates to customer's Azure AD tenant (ConfidentialClientApplication)
   ├── Creates Microsoft 365 Group + Teams
   ├── Provisions SharePoint site with custom settings
   ├── Configures library permissions
   └── Updates system parameters in customer database
        │
        ▼
5. (Optional) Harness pipeline invokes: ByobiSetup HTTP function
   ├── Fetches DB connection string from Key Vault
   ├── Executes [support].[usp_BYOBI_InitialSetup] stored procedure
   └── Executes [support].[usp_BYOBI_TableSetup] stored procedure
        │
        ▼
6. Customer is live at: https://<firmname>.legalcloud.com
```

---

## Flow 2: HTTP Request to a Customer's App Service

This shows how an end-user request travels through the infrastructure:

```
User Browser
      │  HTTPS: https://contoso.legalcloud.com
      ▼
Application Gateway (WAF)
      │
      ├── WAF inspection: check OWASP rules, custom rules
      ├── TLS termination: decrypt HTTPS
      ├── Route lookup: hostname 'contoso.legalcloud.com' → backend pool 'contoso'
      └── Forward HTTP to App Service
      │
      ▼
Windows App Service (contoso)
      │
      ├── Auth check (Azure AD B2C): validate JWT bearer token
      ├── Application logic
      └── Database query → SQL Database (contoso) via private endpoint
      │
      ▼
Response returned through same path
```

The App Service never receives raw internet traffic — all traffic must pass through the Application Gateway. Direct access to App Service IPs is blocked by APIM IP restrictions and VNet integration.

---

## Flow 3: Database Upgrade (Incremental Schema Migration)

```
DatabaseUpgrade function receives HTTP POST:
{
  "firmId": "contoso",
  "connectionStringKvKey": "SqlConnectionStringContoso",
  ...
}
        │
        ▼
KeyVaultManager.GetSecretAsync(kvEndpoint, connectionStringKvKey)
  → Returns SQL connection string
        │
        ▼
Read DatabaseUpgrade.json
  → List of versions, each with ordered SQL files
  → Each file has: Order, FileName, Folder, ExecuteOn flag
        │
        ▼
Query SysParam table in customer DB:
  SELECT value FROM SysParam WHERE name = 'Database Version'
        │
        ├── If current version = target version → return "already up to date"
        │
        └── If current version < target version:
                │
                ▼
            For each version to apply (in order):
              For each SQL file in version (by Order):
                ├── Load script from embedded resource (Data/tables/ or Data/functions/)
                ├── Execute via SMO: server.ExecuteNonQuery(script)
                ├── Log to DatabaseUpgradeHistory table
                └── On failure:
                    ├── Record "Last Failed File Info" in SysParam
                    ├── Send failure email via SendGrid
                    └── Throw exception (stops processing)
                │
                ▼
            Update SysParam: 'Database Version' = new version
            Send success email
```

**Key behaviour:**
- Upgrades are **incremental** — only versions newer than current are applied.
- Scripts are **idempotent** (enforced by convention — always use `IF NOT EXISTS`, `CREATE OR ALTER`, etc.)
- On failure, the function records which file failed so the next run can retry from that point.
- The `ExecuteOn` flag on each file controls whether it runs in cloud (1), utility databases (2), or both (0).

---

## Flow 4: Secret Rotation (Timer-Triggered)

This runs automatically every 10 days at midnight UTC:

```
Timer trigger fires (0 0 0 */10 * *)
        │
        ▼
TenantDBProvider.GetAllTenantApplicationsAsync()
  → Returns all O365TenantApplications records from CosmosDB
        │
        ▼
For each tenant application:
        │
        ▼
  KeyVaultManager.GetSecretAsync(appClientId + "_Secret")
  → Check current secret expiry
        │
        ├── If expires in > 30 days → skip
        │
        └── If expires in ≤ 30 days:
              │
              ▼
          GraphClient.AddPasswordAsync(appId)
          → Creates new secret in Azure AD (Graph API)
              │
              ▼
          KeyVaultManager.SetSecretAsync(appClientId + "_Secret", newSecret)
          → Store new secret in Key Vault
              │
              ▼
          TenantDBProvider.UpsertTenantApplicationAsync(updatedRecord)
          → Update CosmosDB with new secret metadata
              │
              ▼
          On 429/503 from Graph API:
            → Read RetryAfter header (default 5s)
            → Wait and retry
              │
              ▼
          On any error:
            → Post to Teams webhook (TEAMS_WEBHOOK_URL)
            → Log error
            → Continue to next tenant (does not abort entire run)
```

---

## Flow 5: Long-Running Database Upgrade (Durable Functions)

Used when a database upgrade is expected to take more than a few minutes:

```
HTTP POST /api/longrunningdbupdate/start
        │
        ▼
HTTP Starter function:
  ├── Reads Firm from request body
  ├── Generates new orchestration instance ID
  ├── StartNewAsync("O_PerformDbUpdate", instanceId, firm)
  └── Returns 202 Accepted + Location header (status URL)
        │
        ▼ (async)
Orchestrator function (O_PerformDbUpdate):
  ├── Defines retry policy: 3 attempts, 20 min → 60 min backoff
  ├── CallActivityWithRetryAsync("A_PerformActualDbUpdate", retryPolicy, firm)
  └── On completion: return result
        │
        ▼ (async)
Activity function (A_PerformActualDbUpdate):
  └── Same as Flow 3 above (DatabaseOperations.UpgradeDatabase)

Caller polls status URL:
  GET /api/longrunningdbupdate/status/{instanceId}
  → Returns: { "runtimeStatus": "Running" | "Completed" | "Failed", "output": ... }
```

**Why the 202 pattern?** The caller (Harness pipeline) doesn't block — it polls the status URL until the orchestration completes. This prevents HTTP timeout failures on long-running operations.

---

## Flow 6: Terraform State Read During Customer Deployment

Understanding this flow helps when debugging "why is customer deployment failing to read base values":

```
terraform apply (customer layer)
        │
        ▼
data "terraform_remote_state" "base" resolves:
  ├── Connects to Azure Storage using ARM_ACCESS_KEY env var
  ├── Container: tfstate
  ├── Key: base-{environment}.tfstate
  └── Parses outputs block from state JSON
        │
        ▼
local values computed (customer/locals.tf):
  ├── sql_server_fqdn = data.terraform_remote_state.base.outputs.sql_server_fqdn
  ├── key_vault_uri   = data.terraform_remote_state.base.outputs.key_vault_uri
  ├── app_service_plan_id = data.terraform_remote_state.base.outputs.app_service_plan_ids[var.customer.appServicePlanIndex]
  └── etc.
        │
        ▼
Resources created using computed locals
```

**Common failure mode:** If `ARM_ACCESS_KEY` is not set or is wrong, this step fails with an authentication error, not a "resource not found" error.

---

## Flow 7: BYOBI Setup

BYOBI (Bring Your Own Business Intelligence) connects a customer's database to a Power BI workspace:

```
HTTP POST /api/byobi-initial-setup
{
  "firmId": "contoso",
  "keyVaultEndpoint": "https://..."
}
        │
        ▼
SecretClient (DefaultAzureCredential).GetSecretAsync("SqlConnectionStringContoso")
  → SQL connection string from Key Vault
        │
        ▼
SqlConnection.Open()
SqlCommand(CommandType.StoredProcedure, "[support].[usp_BYOBI_InitialSetup]")
SqlParameter("@Tables", pcmsTableListFromEnvVar)
.ExecuteNonQuery()
        │
        ▼
HTTP 200 OK (or error details)
```

The second endpoint (`/api/byobi-table-setup`) follows the same pattern but calls `[support].[usp_BYOBI_TableSetup]`.

---

## Flow 8: Terraform Customer Provisioning via Harness

The full Harness pipeline flow:

```
Harness: terraformprovisioncustomer pipeline
        │
        ▼
Stage 1: Terraform Init
  terraform init \
    -backend-config="storage_account_name=<account>" \
    -backend-config="container_name=tfstate" \
    -backend-config="key=<customername>.terraform.tfstate"
        │
        ▼
Stage 2: Terraform Plan
  terraform plan -var-file="../config/{env}/{customer}.tfvars.json"
        │ (plan output reviewed or auto-approved)
        ▼
Stage 3: Terraform Apply
  terraform apply -var-file="../config/{env}/{customer}.tfvars.json"
        │
        ▼
Stage 4: Invoke Azure Functions (via Run_Base_Function pipeline template)
  POST https://<function-app>.azurewebsites.net/api/databaseupgrade
  POST https://<function-app>.azurewebsites.net/api/onedriveintegration
        │
        ▼
Stage 5: Verification
  Check App Service is responding
  Check Application Gateway routing
```

Authentication in pipelines uses ARM service principal credentials set as Harness secrets:
- `ARM_CLIENT_ID`, `ARM_CLIENT_SECRET`, `ARM_SUBSCRIPTION_ID`, `ARM_TENANT_ID`
- `ARM_ACCESS_KEY` for state backend
