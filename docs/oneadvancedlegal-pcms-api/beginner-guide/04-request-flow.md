# 04 — Request Flow

This document traces what actually happens from the moment an HTTP request arrives to when the response is sent back. Understanding this flow is essential before making changes.

---

## Full Request Lifecycle

```
Client (external)
      │
      │  HTTPS POST /v1/matters  { ... }
      │  Authorization: Bearer <JWT>
      ▼
┌─────────────────────────────┐
│   1. ExceptionHandlingMiddleware  │  ← generates/reads X-Correlation-ID
│      validates Content-Type       │     validates no body on GET
│      catches any BaseException    │     returns ProblemDetails on error
└──────────────┬──────────────┘
               ▼
┌─────────────────────────────┐
│   2. IpValidateMiddleware         │  ← checks IP against IpSafeList
│                                   │     throws ForbiddenException if blocked
└──────────────┬──────────────┘
               ▼
┌─────────────────────────────┐
│   3. SecurityHeadersMiddleware    │  ← adds CSP, X-Frame-Options, etc.
└──────────────┬──────────────┘
               ▼
┌─────────────────────────────┐
│   4. AuthenticationMiddleware     │  ← validates JWT against IdentityServer
│      (Microsoft.Identity.Web)     │     populates ClaimsPrincipal
└──────────────┬──────────────┘
               ▼
┌─────────────────────────────┐
│   5. AuthorizationMiddleware      │  ← enforces [Authorize] on controller
└──────────────┬──────────────┘
               ▼
┌─────────────────────────────┐
│   6. Controller (e.g. MattersController) │
│      - extracts route/query params       │
│      - calls service method              │
│      - returns ActionResult<T>           │
└──────────────┬──────────────┘
               ▼
┌─────────────────────────────┐
│   7. Service (e.g. MatterService)        │
│      - validates business rules          │
│      - calls IRequestService for context │
│      - calls repository method(s)        │
│      - maps entity → DTO (AutoMapper)    │
└──────────────┬──────────────┘
               ▼
┌─────────────────────────────┐
│   8. Repository (e.g. MatterRepository) │
│      - calls IMultiTenantService        │
│         to resolve connection string    │
│      - executes SQL (EF Core or ADO.NET)│
│      - maps DataReader → entity         │
└──────────────┬──────────────┘
               ▼
        SQL Server (tenant DB)
               │
               ▼
       Response flows back up the chain
```

---

## Authentication Flow in Detail

```
1. Client sends:  Authorization: Bearer eyJhbGciOiJSUzI1NiJ9...

2. AuthenticationMiddleware decodes the JWT.
   Expected claims:
   - organisation_reference  → maps to IRequestService.OrgRef
   - sub / user_id           → maps to IRequestService.UserId
   - firm_id                 → maps to IRequestService.FirmId
   - permissions             → maps to IRequestService.Permissions
   - licenses                → maps to IRequestService.Licenses

3. JWT is validated against the IdentityServer configuration
   (issuer, audience, signing key).

4. If valid: ClaimsPrincipal is set, request continues.
   If invalid: 401 Unauthorized returned.

5. IRequestService is populated from the claims and made available
   to all services and repositories via constructor injection.
```

> **Important:** The `OrgRef` from the JWT is the tenant key. Every database call uses it to resolve which SQL Server database to connect to. A missing or wrong `OrgRef` claim will cause connection failures.

---

## Multi-Tenant Database Resolution Flow

```
Repository method called
        │
        ▼
GetConnectionString() in BaseRepository
        │
        ▼
IMultiTenantService.GetConnectionString(IRequestService.OrgRef)
        │
   ┌────┴────────────────┐
   │                     │
   ▼                     ▼
Single-tenant mode    Multi-tenant mode
(BaseMultiTenant)     (DevMultiTenant / ProdMultiTenant)
   │                     │
   │                     ▼
   │               Looks up OrgRef in tenant registry
   │               Returns tenant-specific connection string
   ▼                     │
Fixed connection ────────┘
string from config        │
                          ▼
                   SqlConnection opened to tenant DB
```

---

## Error Handling Flow

When anything goes wrong in the service or repository layer:

```
1. Code throws a typed exception:
   throw new ResourceNotFoundException("Matter 123 not found.");

2. Exception bubbles up through service → controller → middleware.

3. ExceptionHandlingMiddleware catches it.

4. Middleware maps exception type → HTTP status:
   ResourceNotFoundException → 404
   BadDataException          → 400
   ForbiddenException        → 403
   InternalServerErrorException → 500
   (etc.)

5. Middleware renders RFC 7807 ProblemDetails JSON:
   {
     "type": "...",
     "title": "Not Found",
     "status": 404,
     "detail": "Matter 123 not found.",
     "correlationId": "abc-123-..."
   }

6. For 400/409 errors, an "issues" array is included
   with field-level validation details.

7. For unrecognised exceptions (not a BaseException),
   middleware returns 500 with a correlation ID only
   (no internal detail exposed to the client).
```

> **Rule:** Never return error details from a controller directly. Always throw the appropriate `BaseException` subclass and let the middleware handle the response.

---

## Document Upload Flow

Documents involve extra steps because files are stored in an external document management system (SharePoint/cloud), not in SQL Server.

```
POST /v1/matters/{matterId}/documents

1. Controller validates multipart/form-data request.

2. MatterService.PostDocument() called.

3. Service validates:
   - matterId exists (MatterRepository)
   - User has permission to upload (GetMatterUserPermission())
   - File metadata is valid

4. Service calls document storage service
   (IGraphAPIHelper / cloud storage client)
   to upload the file binary.

5. On success, service records document metadata
   in SQL Server via MatterRepository.

6. Service returns DocumentDto with the new document ID and URL.
```

---

## Typical GET Flow (simple read)

```
GET /v1/clients/abc-123

1. Controller:
   public async Task<ActionResult<ClientDto>> GetClient(Guid clientId)
   {
       var result = await _clientService.GetClientById(clientId);
       return Ok(result);
   }

2. Service:
   public async Task<ClientDto> GetClientById(Guid clientId)
   {
       _logger.LogMethodEntry(nameof(GetClientById), clientId);
       var entity = await _clientRepository.Get(clientId);
       if (entity == null)
           throw new ResourceNotFoundException($"Client {clientId} not found.");
       return _mapper.Map<ClientDto>(entity);
   }

3. Repository:
   - Calls GetConnectionString() → resolves tenant DB
   - Executes parameterized SQL or stored procedure
   - Maps SqlDataReader rows → Client entity

4. AutoMapper converts Client entity → ClientDto.

5. Controller returns 200 OK with ClientDto JSON body.
```

---

## Audit Trail

Several operations (Create Matter, Create Client, etc.) write an audit record via `CreateApiAuditAsync()` in the repository. This records:
- Which API endpoint was called
- Which user made the call
- What data was submitted
- Timestamp

This is automatic for create/update operations — do not remove it from new endpoints that modify data.

---

## Request Flow Cheat Sheet

| Layer | File pattern | Does |
|---|---|---|
| HTTP entry | `ExceptionHandlingMiddleware.cs` | Correlation ID, error mapping |
| Security | `IpValidateMiddleware.cs` | IP whitelist enforcement |
| JWT | `AddAdvancedIdentityAuthentication()` | Token validation |
| Route | `{Domain}Controller.cs` | Parameter extraction, service call |
| Business | `{Domain}Service.cs` | Validation, orchestration, mapping |
| Data | `{Domain}Repository.cs` | SQL execution |
| Tenant | `IMultiTenantService` | Connection string per firm |
