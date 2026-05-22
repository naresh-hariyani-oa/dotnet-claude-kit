# 09 — Common Pitfalls

This document lists the most common mistakes new developers make in this codebase, and how to avoid them.

---

## Terraform Pitfalls

### Pitfall 1: Editing `customer/data.tf` Without Understanding Remote State

**What happens:** Developers copy a "simplified" remote state config from documentation or a Stack Overflow answer, replacing the exact configuration in `customer/data.tf`.

**Why it breaks:** The remote state config in `customer/data.tf` has a specific key pattern:
```hcl
key = trimprefix("${join("", compact([var.base_remote_state_file_prefix]))}-base-${var.environment}.tfstate", "-")
```
This handles an optional prefix. Replacing it with a simpler `key = "base-${var.environment}.tfstate"` breaks deployments for customers that use the prefix.

**Rule:** Always reference `customer/data.tf` directly. Never "simplify" it based on examples.

---

### Pitfall 2: Renaming a Resource Without Understanding Terraform's Behaviour

**What happens:** A developer renames a local or variable that feeds into a resource's `name` argument, thinking it's a simple rename.

**Why it breaks:** Terraform sees the old name as "destroy" and the new name as "create". For a SQL database or App Service, this means downtime. For a Key Vault with `soft_delete_enabled`, it means the old vault sits in deleted state and blocks re-creation.

**Rule:** Before renaming anything that affects resource names, run `terraform plan` and check for destroys. If a destroy appears, use `terraform state mv` to rename in state rather than letting Terraform destroy+recreate.

---

### Pitfall 3: Adding a Resource to a Base Module for a Single Customer

**What happens:** A developer needs a resource for one specific customer. For convenience, they add it to a base module.

**Why it breaks:** Base modules are applied for every customer. A resource added to `base/modules/sql_server/` will be created for all environments, affecting all customers and incurring costs.

**Rule:** Customer-specific resources belong in the `customer/` layer. If you need something only for one customer, it goes in their customer config, not in base.

---

### Pitfall 4: Committing tfvars with Real Credentials

**What happens:** A developer adds a customer config with a real `sqlAdministratorLoginPassword` value in the JSON.

**Why it breaks:** This is a security incident. Credentials in Git are compromised even after deletion (git history).

**Rule:** SQL admin passwords and connection strings must not appear in `*.tfvars.json` files. They should be passed as pipeline variables from Harness secrets, not stored in the repository.

---

### Pitfall 5: Running `terraform apply` Against Production Without Reading the Plan

**What happens:** Developer runs apply directly, skips reading the plan, and accidentally destroys resources.

**Rule:** Always read `terraform plan` output fully before `terraform apply`. Look for `destroy` keywords. If you see any unexpected destroys, **stop and investigate** before proceeding.

---

### Pitfall 6: Modifying Existing DatabaseUpgrade.json Entries

**What happens:** Developer edits an existing SQL script reference in `DatabaseUpgrade.json` to fix a typo or change parameters.

**Why it breaks:** The database upgrade system tracks which versions have been applied per customer. If you change an existing entry, customers that already ran that version will not re-run it. Customers that haven't yet run it will run the modified version, creating inconsistency.

**Rule:** Never modify existing entries in `DatabaseUpgrade.json`. Always append new version entries. If a script needs to be corrected, create a new script for a new version that fixes the issue.

---

## C# / Azure Functions Pitfalls

### Pitfall 7: Using `.Result` or `.Wait()` on Async Methods

**What happens:** Developer calls `someAsync().Result` because the calling method isn't async and they don't want to change its signature.

**Why it breaks:** This can cause deadlocks in the ASP.NET/Functions execution model. The thread is blocked while waiting for a task that may itself be waiting for a thread in the same pool.

**Rule:** Make the calling method `async Task` and use `await`. Propagate async up the call chain all the way to the function entry point.

---

### Pitfall 8: Hardcoding Connection Strings or Endpoints

**What happens:** Developer hardcodes a connection string as a fallback "for testing":
```csharp
var connStr = Environment.GetEnvironmentVariable("SQL_CONN") 
    ?? "Server=dev-sql.database.windows.net;...";  ← dangerous
```

**Why it breaks:** The fallback can accidentally run against a real dev database if the env var is missing in any environment. Credentials in code get committed to Git.

**Rule:** If the environment variable is missing, throw a clear exception with a helpful message. Do not provide hardcoded fallbacks for connection strings or endpoints.

---

### Pitfall 9: Not Handling the `ExecuteOn` Flag in Database Scripts

**What happens:** Developer writes a SQL script intended only for cloud databases but doesn't set `ExecuteOn` correctly in `DatabaseUpgrade.json`.

**Why it breaks:** Setting `ExecuteOn: 0` (both) on a cloud-only script will cause it to run against utility databases too, which may not have the required schema.

**Rule:** Check the `ExecuteOn` flag when adding new database script entries: `1` = cloud databases only, `2` = utility databases only, `0` = both.

---

### Pitfall 10: Adding Logic to an Orchestrator Function

**What happens:** Developer adds database calls or HTTP requests directly to the `O_PerformDbUpdate` Durable Functions orchestrator.

**Why it breaks:** Durable orchestrators must be **deterministic** — they are replayed from history on every event. Non-deterministic operations (I/O, `DateTime.Now`, random values) will behave unpredictably during replay, returning different results than the original execution.

**Rule:** Orchestrators may only: call activity functions, call sub-orchestrators, start timers, and return results. All actual work must be in activity functions.

---

### Pitfall 11: Logging Sensitive Data

**What happens:** Developer adds a debug log that includes a connection string or token:
```csharp
_logger.LogDebug("Connecting with: {ConnectionString}", connectionString);
```

**Why it breaks:** Application Insights stores logs. Connection strings and tokens in logs become accessible to anyone with Application Insights read access, and may be retained for months.

**Rule:** Never log connection strings, API keys, tokens, or passwords at any log level. Log identifiers (firm ID, resource name) but not credential values.

---

### Pitfall 12: Ignoring Customer Isolation in Functions

**What happens:** Developer writes a function that processes all customers in a shared loop and uses a single shared SQL connection.

**Why it breaks:** If one customer's operation fails partway through, it may corrupt state or leave partial work for that customer. With a shared connection, a timeout for one customer could block others.

**Rule:** Each customer operation should use its own connection, fetched from Key Vault per customer. Errors for one customer should be caught and logged per-customer, allowing processing to continue for others.

---

## Process Pitfalls

### Pitfall 13: Skipping the PR Template

**What happens:** Developer raises a PR without filling in the impact area, risk level, or testing section.

**Why it matters:** The PR template exists so reviewers can assess risk quickly. Infrastructure PRs without a risk assessment may be approved without proper scrutiny, leading to production incidents.

**Rule:** Always fill in the full PR template. For base infrastructure changes, always set risk level to at least "Med" and describe testing steps.

---

### Pitfall 14: Not Checking CODEOWNERS Before Merging Production Configs

**What happens:** Developer merges a PR modifying `config/prod/*.tfvars.json` without waiting for `@advancedcsg/pcms-maintain` approval.

**Why it matters:** Production customer configs directly affect live customer deployments. The CODEOWNERS approval gate exists to ensure changes are reviewed by the right people.

**Rule:** PRs touching `config/prod/` require approval from `@advancedcsg/pcms-maintain`. Do not merge without this approval.

---

### Pitfall 15: Running Customer Pipeline Without Verifying Base State

**What happens:** Developer runs a customer deployment pipeline while a base infrastructure update is still in progress.

**Why it breaks:** The customer layer reads base outputs from remote state. If base is mid-apply, the state file may be locked or incomplete, causing the customer deployment to fail or produce inconsistent results.

**Rule:** Verify base deployment is fully complete before running customer pipelines. Check the base state is not locked (Azure Storage blob has no lease) before starting customer deployments.
