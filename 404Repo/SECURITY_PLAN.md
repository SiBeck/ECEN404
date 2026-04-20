# Security Plan — BioMetrix Medical Imaging System

## Scope

Three integrated components:
- **BioMetrixDatabase** — REST API (ASP.NET Core, JWT auth)
- **PatientPortal** — Patient-facing web app (ASP.NET Core, cookie auth)
- **DesktopApp** — Provider WPF client (JWT via BioMetrixDatabase)

---

## Current Security Posture (as of integration)

### What is already implemented
| Control | Component | Detail |
|---|---|---|
| JWT authentication | BioMetrixDatabase | HS256, 60-min access token, 7-day refresh |
| Role-based authorization | BioMetrixDatabase | Admin / Physician / Nurse / Technician policies |
| PBKDF2-SHA256 password hashing | DesktopApp | 100k iterations, random salt per hash |
| BCrypt password hashing | PatientPortal | Work factor default (≈12) |
| AES-256-CBC local encryption | DesktopApp | Patient `.enc` files |
| Audit logging | BioMetrixDatabase | Every file and patient operation logged to `AuditLogs` |
| HTTPS redirect | All three | Enforced via `UseHttpsRedirection` |
| Cookie security flags | PatientPortal | HttpOnly, SlidingExpiration, 8-hour session |
| Offline auth fallback | DesktopApp | Demo accounts only; API is primary |

---

## Identified Security Gaps and Mitigations

### GAP 1 — Fixed PBKDF2 salt in DesktopApp patient encryption (HIGH)
**File:** `DesktopApp/403DesktopApp/Services/PatientService.cs:42`

The AES-256 encryption key is derived from a fixed passphrase + fixed salt
(`"BioMetrix-Salt-v1"`). Any attacker who learns the passphrase can decrypt
all patient files without knowing per-record secrets.

**Mitigation:**
1. Generate a random 16-byte salt per patient file at creation time.
2. Store the salt prepended to the encrypted blob (before the IV):
   `[16-byte salt][16-byte IV][ciphertext]`
3. On decrypt, extract the salt from the blob and re-derive the key.
4. Derive the passphrase from DPAPI (`ProtectedData.Protect`) bound to
   the current Windows user, so the key is machine-and-user-specific.

```csharp
// Example: derive key per-record
byte[] salt = RandomNumberGenerator.GetBytes(16);
using var kdf = new Rfc2898DeriveBytes(passphrase, salt, 100_000, HashAlgorithmName.SHA256);
byte[] key = kdf.GetBytes(32);
```

---

### GAP 2 — Hardcoded demo credentials in DesktopApp (HIGH)
**File:** `DesktopApp/403DesktopApp/Services/AuthenticationServices.cs`

The offline fallback still contains MD001/demo123, MD002/demo456, NP001/demo789.

**Mitigation:**
1. Remove the offline fallback entirely in production builds; compile it out
   with `#if DEBUG`.
2. Provision all providers exclusively through BioMetrixDatabase. The
   `DatabaseSeeder` already seeds the same accounts; keep them in sync.
3. Add a `appsettings.Production.json` overlay that sets
   `"OfflineAuthEnabled": false`, checked in `AuthenticateProviderOffline`.

---

### GAP 3 — Service account password in plaintext appsettings (HIGH)
**File:** `PatientPortal/appsettings.json` — `BioMetrixDatabase:ServiceAccountPassword`

The service-account password `CHANGE_ME_IN_PRODUCTION` is a placeholder
committed to source control.

**Mitigation (choose one):**
- **Environment variable:** `BioMetrixDatabase__ServiceAccountPassword` (ASP.NET
  Core reads double-underscore paths from env automatically).
- **User Secrets** in development: `dotnet user-secrets set "BioMetrixDatabase:ServiceAccountPassword" "<value>"`.
- **Azure Key Vault / AWS Secrets Manager** in production.

**Never commit real credentials** to this file.

---

### GAP 4 — JWT secret key is a placeholder (HIGH)
**File:** `BioMetrixDatabase/appsettings.json` — `JwtSettings:SecretKey`

`"YOUR_SECRET_KEY_AT_LEAST_32_CHARACTERS_LONG_FOR_SECURITY"` is committed
to source control.

**Mitigation:**
1. Replace with a randomly generated 256-bit key stored in environment
   variables or a secrets manager, not in `appsettings.json`.
2. Rotate the key on any suspected compromise; all active tokens will
   immediately invalidate (which is the desired behaviour).

```bash
# Generate a suitable key
openssl rand -base64 32
```

---

### GAP 5 — No rate limiting on authentication endpoints (MEDIUM)
**Affected:** `BioMetrixDatabase /api/auth/login`, `PatientPortal /Account/Login`

An attacker can brute-force credentials without throttling.

**Mitigation:**
1. Add `Microsoft.AspNetCore.RateLimiting` (built into .NET 8):

```csharp
// In BioMetrixDatabase Program.cs
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("LoginPolicy", opt =>
    {
        opt.PermitLimit = 5;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        opt.QueueLimit = 0;
    });
});
// Apply to the login endpoint:
app.MapControllers().RequireRateLimiting("LoginPolicy");
```

2. Return `429 Too Many Requests` with a `Retry-After` header.
3. Optionally log failed attempts to `AuditLogs`.

---

### GAP 6 — PatientPortal does not log audit events (MEDIUM)
**File:** `PatientPortal/Services/PatientAuthService.cs`, `PatientPortal/Services/PatientFileService.cs`

Every login, file view, and file download by a patient is invisible in the
audit trail. HIPAA requires accounting of disclosures.

**Mitigation:**
1. Add an `AuditService` to PatientPortal (or call BioMetrixDatabase's
   `/api/audit` endpoint from the bridge service).
2. Log at minimum: patient login/logout, file list view, file download.
3. Store audit records in a separate append-only table with no delete permission
   for the application DB user.

---

### GAP 7 — PatientPortal int PatientId is enumerable (MEDIUM)
**File:** `PatientPortal/Controllers/Api/AuthController.cs` (and file download)

Sequential integer IDs can be guessed. A logged-in patient querying
`/api/files?patientId=N` for small N can access other patients' records
if authorization is not checked server-side.

**Mitigation (already partially implemented — verify completeness):**
- Every file query includes `&& f.PatientId == patientId` from the
  authenticated session claim, not from the URL.
- Add an integration test that asserts patient A cannot read patient B's files.

---

### GAP 8 — CORS wildcard risk if misconfigured (LOW)
**File:** `BioMetrixDatabase/Program.cs` — `WithOrigins(allowedOrigins)`

The default fallback `"https://localhost:7000"` is safe for development but
production origins must be set explicitly.

**Mitigation:**
1. Set `Cors:AllowedOrigins` in environment-specific `appsettings.Production.json`
   or environment variables.
2. Never set `WithOrigins("*")` since `AllowCredentials()` is also enabled
   (browsers reject that combination anyway, but it is a misconfig signal).

---

### GAP 9 — DesktopApp self-signed certificate acceptance (LOW)
**File:** `DesktopApp/403DesktopApp/Services/BioMetrixApiService.cs:34`

`DangerousAcceptAnyServerCertificateValidator` is always enabled, making
the app vulnerable to MITM in any environment.

**Mitigation:**
1. Only disable certificate validation in `DEBUG` builds:

```csharp
#if DEBUG
ServerCertificateCustomValidationCallback =
    HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
#endif
```

2. In production, install a valid TLS certificate on BioMetrixDatabase
   (e.g. Let's Encrypt or your institution's CA) and remove the override.

---

## Recommended Implementation Order

| Priority | Gap | Effort |
|---|---|---|
| 1 | GAP 3 — Move service account password to env/secrets | ~30 min |
| 2 | GAP 4 — Replace JWT secret with env variable | ~30 min |
| 3 | GAP 5 — Add rate limiting to login endpoints | ~2 hours |
| 4 | GAP 1 — Per-record salt + DPAPI for local encryption | ~4 hours |
| 5 | GAP 6 — Audit logging in PatientPortal | ~3 hours |
| 6 | GAP 2 — Remove offline fallback from production builds | ~1 hour |
| 7 | GAP 7 — Integration test for cross-patient access | ~2 hours |
| 8 | GAP 8 — Production CORS origins | config change |
| 9 | GAP 9 — Conditional cert validation | ~30 min |

---

## Ongoing Security Practices

- **Dependency scanning:** Run `dotnet list package --vulnerable` in CI.
- **Secrets scanning:** Enable GitHub secret scanning on this repo.
- **TLS everywhere:** All three components must communicate over HTTPS in
  production; do not allow HTTP in any service-to-service path.
- **Token storage (DesktopApp):** Store the refresh token using Windows DPAPI
  (`ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser)`)
  rather than plain memory, to survive app restarts securely.
- **Database encryption at rest:** Both SQLite databases currently store data
  unencrypted. Consider SQLCipher or disk-level encryption (BitLocker / LUKS).
- **Least privilege for PORTAL_SVC:** The service account has `Technician` role,
  which grants `RequireMedicalStaff` access (file upload). Review whether it
  also needs delete access (`RequirePhysician`) and restrict if not.
