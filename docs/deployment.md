# Publishing the portal

## Recommendation

| Need | Choice | Why |
|---|---|---|
| **Hosting** | **Microsoft Azure — Azure Container Apps** | Same tenant/identity as Entra ID and D365 F&O; managed HTTPS, autoscale, no servers to patch; runs the same Docker images as local. |
| **Region** | **UAE North** now; **Saudi Arabia East** for KSA customers needing in-Kingdom data once it is generally available | Closest GA region to Cairo/Riyadh with all services used. |
| **Database** | Azure Database for PostgreSQL – Flexible Server (Burstable B1ms, 32 GB, 14-day backups) | Managed, backups, scale up later without code changes. |
| **Secrets** | Azure Key Vault + managed identity | DB connection string and the Data Protection key that encrypts tenant D365 secrets; no secrets in code or pipeline logs. |
| **Source code** | **Private GitHub repository** in the Grow Path organization | Private by default, branch protection, GitHub Actions does build → test → deploy, OIDC to Azure (no stored Azure password). Azure DevOps Repos works equally if Grow Path already standardizes on it. |

Rough running cost at this size: low hundreds of USD per year per environment, dominated by the always-on API/web replicas and the database. Check the [Azure pricing calculator](https://azure.microsoft.com/pricing/calculator/) for current figures.

```
Internet ──HTTPS──► web (Container App, public, nginx + SPA)
                        │  /api  (internal HTTPS)
                        ▼
                    api (Container App, internal only) ──► PostgreSQL Flexible Server
                        │           │
                        │           └──► Key Vault (db secret, DP key)  via managed identity
                        └──► Entra ID / customer D365 environments
```

The API has **no public endpoint**; only the web app is reachable from the internet.

## What gets deployed (`infra/main.bicep`)
Log Analytics · user-assigned managed identity · Container Registry (Basic) · Key Vault (RBAC, purge protection) · PostgreSQL 16 Flexible Server + database · Container Apps environment · `d365portal-api` (internal) · `d365portal-web` (public).

## One-time setup (≈30 minutes)

### 1. Put the code in GitHub
```bash
# create an empty PRIVATE repo, e.g. growpath/d365-portal (no README), then:
cd ylo-d365-portal
git remote add origin https://github.com/<org>/d365-portal.git
git push -u origin main
```
Settings → Branches → protect `main` (require pull request + the CI checks).

### 2. Azure identity for GitHub Actions (OIDC, no secret)
In the Azure subscription that will host the portal (Cloud Shell):
```bash
SUB=<subscription-id>; RG=rg-d365portal-prod; LOC=uaenorth; REPO=<org>/d365-portal
az group create -n $RG -l $LOC
APP=$(az ad app create --display-name "d365portal-github-deploy" --query appId -o tsv)
az ad sp create --id $APP
az ad app federated-credential create --id $APP --parameters '{
  "name":"github-main","issuer":"https://token.actions.githubusercontent.com",
  "subject":"repo:'$REPO':environment:production","audiences":["api://AzureADTokenExchange"]}'
# Contributor to create resources, User Access Administrator to grant the app identity
# AcrPull / Key Vault roles — both scoped to this resource group only.
az role assignment create --assignee $APP --role Contributor --scope /subscriptions/$SUB/resourceGroups/$RG
az role assignment create --assignee $APP --role "User Access Administrator" --scope /subscriptions/$SUB/resourceGroups/$RG
echo "AZURE_CLIENT_ID=$APP  AZURE_TENANT_ID=$(az account show --query tenantId -o tsv)  AZURE_SUBSCRIPTION_ID=$SUB"
```

### 3. GitHub repository settings
Settings → Environments → **production** (optionally add yourself as required reviewer).

**Secrets** (Settings → Secrets and variables → Actions → Secrets):

| Name | Value |
|---|---|
| `AZURE_CLIENT_ID` / `AZURE_TENANT_ID` / `AZURE_SUBSCRIPTION_ID` | from step 2 |
| `POSTGRES_ADMIN_PASSWORD` | a strong generated password (16+ chars, mixed) |

**Variables**:

| Name | Value |
|---|---|
| `AZURE_RESOURCE_GROUP` | `rg-d365portal-prod` |
| `AZURE_LOCATION` | `uaenorth` |
| `NAME_PREFIX` | `d365portal` |
| `API_CLIENT_ID` / `WEB_CLIENT_ID` | Entra app registrations (README §Setup 1) |
| `SUPERADMIN_ENTRA_TENANT_ID` / `SUPERADMIN_OBJECT_ID` | your Entra directory id / user object id |
| `SEED_ENABLED` | `true` for the first deploy, then `false` |
| `SEED_ADMIN_EMAIL`, `SEED_ENTRA_TENANT_IDS`, `SEED_COMPANIES` | demo tenant admin, your directory id, e.g. `USMF` |

### 4. Deploy
Push to `main` (or Actions → *Deploy to Azure* → Run workflow). The run: builds + tests everything → creates platform resources → builds images in the registry → deploys both apps → smoke-tests the URL. The URL is shown in the run summary (`https://d365portal-web.<random>.uaenorth.azurecontainerapps.io`).

### 5. Point Entra at the live URL
App registration **D365 Portal – Web** → Authentication → Single-page application → add the portal URL as a redirect URI. Sign in.

### 6. Custom domain (recommended), e.g. `portal.growpath.net`
Container App `d365portal-web` → Custom domains → add domain → create the CNAME + TXT records it shows → choose a free managed certificate. Add `https://portal.growpath.net` as a redirect URI in the Web app registration.

## Operations
- **Logs**: Container App → Log stream, or Log Analytics (`ContainerAppConsoleLogs_CL`).
- **Rollback**: Container App → Revisions → activate the previous revision, or re-run the deploy workflow for an older commit.
- **Database backups**: point-in-time restore up to 14 days (PostgreSQL → Backup and restore).
- **Hardening later**: VNet-integrated environment + private PostgreSQL access (removes the "Allow Azure services" firewall rule); zone redundancy; a staging environment (second resource group + `staging` GitHub environment).
- **Key Vault** has purge protection: if you delete the resource group, the vault name is reserved for 30 days.
