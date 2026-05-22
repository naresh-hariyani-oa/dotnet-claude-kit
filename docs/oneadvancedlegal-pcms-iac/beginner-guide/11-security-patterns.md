# 11 — Security Patterns

This document describes the security mechanisms actually implemented in this codebase. It does not speculate about intended controls — only documents what exists.

---

## Authentication

### Managed Identity (Primary Production Pattern)

Azure Functions and App Services use **system-assigned managed identities** to authenticate to other Azure services without any stored credentials. The `DefaultAzureCredential` class from the Azure Identity SDK automatically uses managed identity when running in Azure.

```csharp
// No credentials in code — managed identity handles it automatically
var secretClient = new SecretClient(
    new Uri(kvEndpoint),
    new DefaultAzureCredential()
);
```

This pattern is used for:
- Functions → Key Vault (secret retrieval)
- Functions → CosmosDB (tenant data access)
- App Services → Key Vault (connection string retrieval at runtime)

### Confidential Client (Cross-Tenant Operations)

When functions must act in a **customer's** Azure AD tenant, they use a service principal with client ID + secret. These credentials are stored in Key Vault (not in code or config files):

```csharp
var app = ConfidentialClientApplicationBuilder
    .Create(clientId)
    .WithClientSecret(clientSecret)   // fetched from Key Vault
    .WithAuthority(authority)
    .Build();
```

Used by: `OneDriveIntegration.cs`, B2C deployment scripts.

### Interactive Browser (Development Only)

When `Environment == "Development"`, `InteractiveBrowserCredential` is used. This is never active in deployed environments — only for local development convenience.

---

## Secrets Management

### Key Vault as Single Secret Store

All secrets (connection strings, API keys, passwords, certificates) are stored in Azure Key Vault. No secrets appear in:
- Source code
- `*.tfvars.json` files
- Environment variables in Harness pipeline definitions (they reference Harness secrets, which are injected at runtime)
- Application Insights logs (or should not — see [09-common-pitfalls.md](09-common-pitfalls.md), Pitfall 11)

### Key Vault Access Control

Key Vault uses **RBAC authorization** (not legacy access policies, except where noted):
- Managed identities get `Key Vault Secrets User` role
- The Application Gateway's MSI gets access for certificate management
- Developer accounts get `Key Vault Secrets User` for nonprod debugging

The base `key_vault` module configures RBAC at provisioning time via `azurerm_role_assignment` resources.

### Secret Rotation

The `SecretRotation.cs` timer function automatically rotates O365 tenant application secrets every 10 days. It:
1. Checks current secret expiry against a 30-day threshold
2. Creates a new secret via Microsoft Graph API
3. Updates the secret in Key Vault
4. Updates the record in CosmosDB
5. Sends a Teams notification on failure

This means O365 secrets are never more than 30 days from expiry in the worst case.

---

## Network Security

### Application Gateway + WAF

All inbound customer traffic goes through the Application Gateway with WAF (Web Application Firewall):
- Terminates TLS — App Services do not handle SSL certificates
- Applies OWASP rule sets to inspect requests
- Custom WAF rules configurable via `base/nonprod.tfvars.json` and `prod.tfvars.json`
- Direct access to App Service IPs is blocked

### Private Endpoints

Sensitive PaaS services (SQL, CosmosDB, Storage) are accessible only via private endpoints within the VNet:
- No public internet access to these services
- App Services and Functions communicate via VNet integration
- This prevents accidental public exposure of data stores

### APIM IP Restrictions

App Services have IP restriction rules that allow only:
- APIM's public IP (for external API access)
- APIM's private IP (for internal API access)
- Application Gateway subnet (for customer web apps)

These are configured in the `windows_web_app` customer module and propagated from base outputs.

### TLS Enforcement

All App Services enforce **TLS 1.2 minimum**. HTTP is not accepted. The Application Gateway handles HTTPS termination so App Services receive plain HTTP internally (within the VNet), but external clients always use HTTPS.

---

## Webhook Security (HMAC-SHA256)

The `ThirdFortWebHook.cs` function validates incoming webhook messages:

1. The sender signs the request body with a shared secret using HMAC-SHA256
2. The signature is included in a request header
3. The function computes the expected HMAC using the same shared secret
4. It compares the computed HMAC to the received signature using **constant-time comparison**

```csharp
// Constant-time comparison prevents timing attacks
var isValid = CryptographicOperations.FixedTimeEquals(
    Encoding.UTF8.GetBytes(expectedSignature),
    Encoding.UTF8.GetBytes(receivedSignature)
);
```

Using `==` for string comparison would leak information about how many bytes match (timing side channel). Constant-time comparison eliminates this.

---

## SQL Injection Prevention

SQL queries use parameterized commands and stored procedures. Direct string interpolation into SQL queries is not used:

```csharp
// Correct (parameterized):
cmd.Parameters.AddWithValue("@FirmId", firmId);

// Correct (stored procedure):
cmd.CommandType = CommandType.StoredProcedure;
cmd.CommandText = "[support].[usp_BYOBI_InitialSetup]";
```

**Code review rule:** Flag any SQL query built by string concatenation or interpolation as a SQL injection risk.

---

## Multi-Tenant Data Isolation

Customer data isolation is enforced at the infrastructure level, not application level:
- Each customer has their own SQL database (on the shared server)
- Connection strings in Key Vault are customer-specific (e.g., `SqlConnectionStringContoso`)
- Each customer's App Service is configured with its own Key Vault secret reference
- A compromised App Service can only access its own Key Vault secrets (RBAC scoped)

This means there is no application-level tenant filtering to maintain — the database boundary itself provides isolation.

---

## Azure AD B2C Authentication

Customer end-users authenticate via Azure AD B2C:
- Custom trust framework XML policies define the sign-in/sign-up flow
- Each customer has their own app registration in the shared B2C tenant
- `auth_settings_v2` on App Services enforces B2C JWT validation — unauthenticated requests are redirected to B2C
- B2C issues JWTs that App Services validate automatically

Configuration flows:
1. B2C app registration created by deployment script (`b2cappreg.sh`) during customer provisioning
2. B2C custom policies uploaded by `DeployToB2C.ps1`
3. App Service auth settings configured by the `windows_web_app_auth_settings` Terraform module

---

## Certificate Management

SSL certificates are managed via `base/modules/app_service_certificate_order/`:
- App Service Certificate Order resource provisions certificates via Azure
- Certificates are stored in Key Vault
- Application Gateway retrieves certificates from Key Vault via its managed identity
- Certificate rotation is handled by Azure automatically for managed certificates

---

## Security Code Review Checklist

When reviewing PRs, flag:

- [ ] SQL built by string concatenation (injection risk)
- [ ] Secrets hardcoded in `.cs` or `.tf` files
- [ ] `DefaultAzureCredential` replaced with client ID/secret without justification
- [ ] New webhook endpoint without HMAC or other signature validation
- [ ] Sensitive data (connection strings, tokens) in log statements
- [ ] Missing TLS enforcement on new App Service or storage resources
- [ ] New external HTTP calls without TLS validation
- [ ] New resources missing RBAC configuration (relying on public access)
- [ ] Key Vault access policy instead of RBAC (legacy pattern — prefer RBAC)
