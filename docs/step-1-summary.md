# Step 1 — Skeleton, schema, Microsoft login, tenant resolution

## Built
- **Solution**: `backend/` (Core, Infrastructure, Api, Tests) + `frontend/` (Vite React TS, Fluent UI v9) + `docker-compose.yml` (Postgres, API, nginx web).
- **Schema + migration** (PostgreSQL): tenants, d365_connections, app_users, user_company_access, audit_logs, data_protection_keys. SQL Server supported via `Database:Provider=SqlServer` with its own migration folder.
- **Auth**: MSAL (PKCE) "Sign in with Microsoft" → API validates Entra tokens (multi-tenant, `access_as_user` scope required on every endpoint).
- **Tenant resolution**: `POST /api/session` maps the account to tenants, binds first-time users (UPN + allowed Entra directory), records login, audits it. Not provisioned → Not authorized page. Several tenants → picker. `X-Tenant-Id` validated per request.
- **Isolation**: EF global query filters on every tenant-owned table (including `tenants`), plus a SaveChanges guard blocking cross-tenant writes.
- **Hardening already in**: problem+json errors with codes, per-user rate limiting, CORS allow-list, HTTPS/HSTS, forwarded headers only from configured proxies, Key Vault config + Data Protection key wrapping, secrets encrypted at rest.
- **UI**: F&O-style top bar (company code read-only until step 3, user menu with name/email/tenant/switch/sign out), left nav with role-based sections, home workspace tiles, placeholders for steps 2–8, i18n scaffold with RTL switch.
- **Tests**: backend — tenant isolation (7), user binding incl. attack cases (9), API pipeline (9). Frontend — 19 passing.

## Verification status
- Frontend: typecheck, production build and 19 tests **pass**.
- Backend: **not compiled here** — the .NET SDK and NuGet were blocked in the build environment. Code was desk-checked by an independent review pass; its one compile error and all security findings were fixed. The EF migration was hand-written and cross-checked against the model.
  Run once on your machine: `cd backend && dotnet test` and `dotnet ef migrations has-pending-model-changes --project src/D365Portal.Infrastructure --startup-project src/D365Portal.Api --context PostgresAppDbContext` (should report no changes).

## D365 entity/field names
None used yet (D365 calls start in step 2's Test connection and step 3).

## You need to configure
1. Two Entra app registrations (Web SPA + API with `access_as_user`) — README §Setup 1.
2. `.env` from `.env.example`: client ids, your super-admin tid/oid, seed admin email, **`SEED_ENTRA_TENANT_IDS` = your directory id** (without it the seeded admin can't bind).
3. `docker compose up --build`, sign in at http://localhost:8080.

## Carried into step 2
- Super-admin screen must set each tenant's **allowed Entra directories** (defaults to the D365 connection's Entra tenant id is a sensible UX choice).
- Tenant-admin user management writes AppUser + UserCompanyAccess under the tenant filter.
