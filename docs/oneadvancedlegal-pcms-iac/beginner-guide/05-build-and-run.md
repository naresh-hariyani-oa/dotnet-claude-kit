# 05 — Build and Run Guide

This document covers how to set up your development environment, build the code, and run things locally.

---

## Prerequisites

### Required Tools

| Tool | Version | Purpose |
|---|---|---|
| Terraform | >= 1.0 | Infrastructure provisioning |
| .NET SDK | 8.0 | Build and run C# functions |
| Azure Functions Core Tools | v4 | Run functions locally |
| Azure CLI (`az`) | latest | Azure authentication, script execution |
| Git | any | Source control |

### Optional but Recommended

| Tool | Purpose |
|---|---|
| Visual Studio 2022 or Rider | C# development with full debugging |
| VS Code with Terraform extension | Terraform editing with syntax highlighting |
| VS Code with Azure Functions extension | Functions development and debugging |
| PowerShell 7+ | Some deployment scripts are `.ps1` |
| Bash (WSL2 or Git Bash on Windows) | Required for `.sh` scripts |

---

## Azure Access Requirements

To work with this repository you will need:

- An Azure subscription with appropriate RBAC (ask your team lead)
- Access to the Key Vault (to read secrets)
- Access to the Terraform state storage account
- Contributor or Owner on the relevant resource group(s)

For local development of Azure Functions:
- Access to the Key Vault (functions use `DefaultAzureCredential` → falls back to `az login`)

---

## Initial Setup

### 1. Clone the Repository

```bash
git clone <repository-url>
cd oneadvancedlegal-pcms-iac
```

### 2. Authenticate with Azure

```bash
az login
az account set --subscription <subscription-id>
```

Functions running locally will use this login via `DefaultAzureCredential`.

For local development with interactive browser credential, set the environment variable:
```bash
export Environment=Development
```
(On Windows: `set Environment=Development` or set in `local.settings.json`)

---

## Terraform

### Running Base Infrastructure Locally

> **Warning:** Only do this if you are an infrastructure engineer with permission to modify shared base resources. A mistake here affects all customers.

```bash
cd base

terraform init \
  -backend-config="storage_account_name=<tfstate-storage-account>" \
  -backend-config="container_name=tfstate" \
  -backend-config="key=base-nonprod.tfstate"

# Set authentication environment variables first:
export ARM_CLIENT_ID=<service-principal-client-id>
export ARM_CLIENT_SECRET=<service-principal-secret>
export ARM_SUBSCRIPTION_ID=<subscription-id>
export ARM_TENANT_ID=<tenant-id>
export ARM_ACCESS_KEY=<storage-account-access-key>

terraform plan -var-file="nonprod.tfvars.json"
# Review the plan output carefully before applying
terraform apply -var-file="nonprod.tfvars.json"
```

### Running Customer Provisioning Locally

```bash
cd customer

terraform init \
  -backend-config="storage_account_name=<tfstate-storage-account>" \
  -backend-config="container_name=tfstate" \
  -backend-config="key=<customer-name>.terraform.tfstate"

# Set auth variables (same as above) plus:
export ARM_ACCESS_KEY=<storage-account-access-key>
export TFSTATE_SECRET=<same-access-key>

terraform plan -var-file="../config/nonprod/<customer-name>.tfvars.json"
terraform apply -var-file="../config/nonprod/<customer-name>.tfvars.json"
```

### Validating Terraform Without Deploying

```bash
# Validate syntax without needing backend:
terraform validate

# Format check:
terraform fmt -check -recursive

# Format all files:
terraform fmt -recursive
```

---

## C# Azure Functions

### Build the Solution

```bash
cd functions
dotnet build Legal.Cloud.Provisioning.Functions.sln
```

Expected output: `Build succeeded. 0 Warning(s) 0 Error(s)`

If you see errors about missing packages, run:
```bash
dotnet restore Legal.Cloud.Provisioning.Functions.sln
```

### Run Functions Locally

```bash
cd functions/Legal.Cloud.Provisioning.Functions
func start
```

This starts the Functions host on `http://localhost:7071`. You will see output like:
```
Functions:
    DatabaseUpgrade: [POST] http://localhost:7071/api/databaseupgrade
    ByobiSetup: [POST] http://localhost:7071/api/byobi-initial-setup
    ...
```

### Local Settings Configuration

The functions project requires a `local.settings.json` file (not checked into git). Create it at `functions/Legal.Cloud.Provisioning.Functions/local.settings.json`:

```json
{
  "IsEncrypted": false,
  "Values": {
    "AzureWebJobsStorage": "UseDevelopmentStorage=true",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
    "AZURE_KEY_VAULT_ENDPOINT": "https://<your-keyvault>.vault.azure.net/",
    "COSMOS_DB_ENDPOINT": "https://<your-cosmosdb>.documents.azure.com:443/",
    "COSMOS_DB_DATABASE": "<database-name>",
    "COSMOS_DB_CONTAINER": "tenantidentifier",
    "OnboardingServiceBus": "<service-bus-connection-string>",
    "TEAMS_WEBHOOK_URL": "https://...",
    "Environment": "Development",
    "ApplicationInsights:ConnectionString": "<app-insights-connection-string>"
  }
}
```

> **Note:** All values above come from Key Vault or Azure portal. Ask a team member for the nonprod values.

### Run the Console App Locally (Database Upgrades)

The Console app is useful for running database upgrades manually outside of Azure Functions:

```bash
cd functions/Legal.Cloud.Provisioning.Console
dotnet run
```

Configure it via `appsettings.json` in the same folder (not committed — copy from team).

### Run Unit Tests

There are currently no automated test projects in this solution. Manual testing is done by invoking HTTP endpoints against deployed Functions or locally.

---

## Testing Functions Manually (HTTP)

Once `func start` is running, test endpoints with curl or a tool like Postman:

```bash
# Test DatabaseUpgrade
curl -X POST http://localhost:7071/api/databaseupgrade \
  -H "Content-Type: application/json" \
  -d '{
    "firmId": "test-firm",
    "connectionStringKvKey": "SqlConnectionStringTestFirm",
    "targetVersion": "1.0.0"
  }'

# Test ByobiSetup
curl -X POST http://localhost:7071/api/byobi-initial-setup \
  -H "Content-Type: application/json" \
  -d '{
    "firmId": "test-firm",
    "keyVaultEndpoint": "https://<your-keyvault>.vault.azure.net/"
  }'
```

---

## Common Build Issues

### `dotnet build` fails with package restore errors

```
Error: Unable to find package 'Microsoft.Azure.Cosmos' with version (>= 3.57.0)
```

**Fix:** Ensure you're on the corporate network or have NuGet feed access:
```bash
dotnet restore --interactive
```
This may prompt for authentication to a private NuGet feed.

### `func start` fails — "Can't determine Project to build"

**Fix:** Ensure you run `func start` from inside `Legal.Cloud.Provisioning.Functions/`, not from the solution root.

### `func start` fails with "Could not load file or assembly"

**Fix:** Build the solution first:
```bash
cd functions
dotnet build Legal.Cloud.Provisioning.Functions.sln
cd Legal.Cloud.Provisioning.Functions
func start
```

### Key Vault access denied (403) locally

**Fix:** Ensure you are logged in with `az login` and your user account has `Key Vault Secrets User` RBAC role on the nonprod Key Vault. Or set `Environment=Development` to use interactive browser credential.

### Terraform init fails — "Backend configuration changed"

**Fix:** Delete the `.terraform/` directory in the layer folder and re-run `terraform init`:
```bash
rm -rf .terraform/
terraform init -backend-config=...
```

### Terraform plan fails — "Error retrieving state: could not read blob"

**Fix:** The `ARM_ACCESS_KEY` environment variable is missing or incorrect. Verify the storage account access key.

---

## Deployment via Harness

Normal deployments are handled by Harness pipelines — you do not run `terraform apply` directly in production.

To trigger a pipeline:
1. Log in to the Harness platform
2. Navigate to Project: PCMS, Organization: Legal
3. Find the relevant pipeline (e.g., `terraformprovisioncustomer`)
4. Click "Run" and fill in parameters (customer name, environment, etc.)

For functions deployment: the `deployfunctionsnonprod` pipeline builds, packages, and deploys the Functions app to Azure.

---

## Tips for Working Locally

- **Terraform changes:** Always run `terraform plan` before `terraform apply`. Read the entire plan output.
- **New customer config:** Copy from `config/template/customer_template.json`, fill in all fields, and validate with `terraform plan` before merging.
- **Functions changes:** Test locally first, then raise a PR. The pipeline will rebuild and redeploy the Function App.
- **Database scripts:** Test new SQL scripts against a local or dev SQL instance before adding to `DatabaseUpgrade.json`.
