# 08 — Debugging Guide

## Debugging Azure Functions Locally

### Setup

1. Open `functions/Legal.Cloud.Provisioning.Functions.sln` in Visual Studio 2022 or Rider.
2. Set `Legal.Cloud.Provisioning.Functions` as the startup project.
3. Ensure `local.settings.json` is present (see [05-build-and-run.md](05-build-and-run.md)).
4. Set `Environment=Development` in `local.settings.json` to use interactive browser auth.
5. Press F5 (or use "Attach to Process" if already running via `func start`).

### Breakpoint Behaviour

The Functions isolated worker model runs the host and worker as separate processes. Visual Studio attaches to the worker process (`dotnet`). If breakpoints aren't hitting:
- Ensure you selected "Debug" configuration (not "Release")
- Check "Enable Just My Code" is off for third-party call stacks

### Inspecting Key Vault Calls Locally

When running locally with `Environment=Development`, the `InteractiveBrowserCredential` opens a browser for Azure AD login. After signing in, subsequent Key Vault calls use that token.

If you get `403 Forbidden` accessing Key Vault locally:
- Your Azure account needs `Key Vault Secrets User` RBAC role on the nonprod Key Vault
- Ask a team lead to add this via the Azure Portal

---

## Debugging Terraform Issues

### "Error: A resource with the ID ... already exists"

This means Terraform's state file says the resource doesn't exist, but it does in Azure. This happens when resources are created outside Terraform or state was lost.

**Resolution:**
```bash
# Import the existing resource into Terraform state:
terraform import module.key_vault.azurerm_key_vault.main /subscriptions/.../resourceGroups/.../providers/Microsoft.KeyVault/vaults/...
```

Ask a senior engineer before doing this — it can have side effects.

### "Error: retrieving state: could not read blob"

The Terraform state backend is unreachable. Check:
1. `ARM_ACCESS_KEY` environment variable is set and correct
2. The storage account name is correct in the `-backend-config` arguments
3. You have network access to the storage account

### Plan Shows Unexpected Destroys

If `terraform plan` shows resources being destroyed that you didn't intend to remove:
1. **Stop.** Do not apply.
2. Check if you changed a `for_each` key or resource name — renaming causes destroy+create.
3. Check if a module's `name` output changed — changing CAF naming inputs rebuilds everything.
4. Run `terraform state list` to see what Terraform thinks exists.
5. Compare against actual Azure resources in the portal.

### Remote State Data Source Issues

If `data.terraform_remote_state.base` cannot be read:
```
Error: Unable to read remote state: unable to read container "tfstate"
```

Check:
- The base layer has been applied (`base-nonprod.tfstate` exists in the storage account)
- `TFSTATE_SECRET` env var matches the storage account's access key
- The key pattern in `customer/data.tf` matches the actual state file name

---

## Debugging Azure Functions in Production (Application Insights)

### Viewing Live Logs

Azure Portal → Function App → `Log stream` (near real-time, but drops messages under load)

For reliable querying, use Application Insights → **Logs**:

```kusto
-- Recent function invocations with errors
traces
| where timestamp > ago(1h)
| where severityLevel >= 3  -- Warning and above
| project timestamp, message, severityLevel, operation_Name
| order by timestamp desc
| limit 100
```

```kusto
-- Find a specific firm's operations
traces
| where message contains "contoso"
| order by timestamp desc
| limit 50
```

```kusto
-- All exceptions in last 24 hours
exceptions
| where timestamp > ago(24h)
| project timestamp, type, outerMessage, operation_Name
| order by timestamp desc
```

```kusto
-- Database upgrade history for a firm
traces
| where message contains "DatabaseUpgrade"
| where message contains "firmId"
| order by timestamp desc
```

### Tracing a Specific Function Invocation

Each invocation has a unique `operation_Id`. Once you have it (from function output or an error alert):

```kusto
traces
| where operation_Id == "<operation-id>"
| order by timestamp asc
```

This shows the complete log sequence for that single invocation.

---

## Debugging Durable Functions

The `LongRunningDbUpdate` function uses Durable Task Framework. To check orchestration state:

```bash
# Get status of a specific orchestration
GET https://<function-app>.azurewebsites.net/api/longrunningdbupdate/status/{instanceId}
```

Or via Azure Portal → Function App → Functions → `HttpStart_PerformDbUpdate` → Monitor → Instances

Instance states:
- `Running` — actively working or waiting for activity
- `Completed` — finished successfully
- `Failed` — failed after all retries exhausted
- `Terminated` — manually cancelled
- `Pending` — waiting for an activity function

To view detailed orchestration history in Azure Portal:
1. Go to the Storage Account used by the Function App
2. Find the table `DurableFunctionsHubHistory` (or similar per hub name `DbUpdateAppHub`)
3. Filter by `InstanceId`

---

## Debugging B2C and App Registration Scripts

Customer provisioning runs Bash scripts via Azure Container Instances (AzAPI deployment scripts). If they fail:

1. In Azure Portal, find the resource group for the customer
2. Look for resources of type `Microsoft.Resources/deploymentScripts`
3. Click on it → Logs tab → view stdout/stderr
4. The container is ephemeral — logs are only available for ~2 hours after failure (configurable in the azapi_resource module)

Common B2C script failures:
- **`az login` fails** — the service principal credentials in the deployment script environment are wrong or expired
- **Permission denied on Graph API** — the service principal lacks required AD permissions
- **App registration already exists** — idempotency issue; the script may need a `--upsert` flag or manual cleanup

---

## Debugging Service Bus Issues

For `ThirdFortWebHook.cs` and `DatabaseRestore.cs` which post to Service Bus:

**Check message delivery:**
- Azure Portal → Service Bus Namespace → Queues → select queue → Service Bus Explorer
- "Peek" messages in the queue without consuming them

**Check dead-letter queue:**
- If messages fail processing after max retries, they go to `<queue>/$DeadLetterQueue`
- In Service Bus Explorer, switch to "Dead Letter" view to see failed messages with error reasons

**Verify connection:**
```bash
# Check Service Bus connectivity from local machine
az servicebus queue show --namespace-name <namespace> --name <queue> --resource-group <rg>
```

---

## Common Error Messages and Resolutions

| Error | Likely Cause | Resolution |
|---|---|---|
| `Azure.RequestFailedException: Status: 403` (Key Vault) | Missing RBAC role on Key Vault | Add `Key Vault Secrets User` role to your identity |
| `Azure.RequestFailedException: Status: 404` (Key Vault) | Secret name typo or secret doesn't exist | Verify secret name in Key Vault portal |
| `SqlException: Login failed for user` | Wrong connection string or SQL credentials | Check connection string in Key Vault secret |
| `SqlException: Timeout expired` | Query too slow or DB overloaded | Check elastic pool DTU usage; query may need optimisation |
| `429 TooManyRequests` (Graph API) | Rate limit hit | SecretRotation handles this automatically; if manual, wait and retry |
| `Terraform: Error reading remote state` | State backend credentials wrong | Verify ARM_ACCESS_KEY |
| `Terraform: Error: Resource already exists` | State/reality mismatch | Use `terraform import` (consult senior engineer) |
| `func start: No project file found` | Running from wrong directory | Must be run from inside the Functions project folder |
| `dotnet build: Package not found` | NuGet feed unavailable | Run with `--interactive` or check corporate proxy settings |
