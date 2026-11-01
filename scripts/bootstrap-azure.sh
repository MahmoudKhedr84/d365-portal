#!/usr/bin/env bash
# One-shot publish of the D365 Portal. Run in Azure Cloud Shell (Bash):
#
#   curl -fsSL https://raw.githubusercontent.com/<owner>/<repo>/main/scripts/bootstrap-azure.sh -o bootstrap.sh
#   (private repo: clone it instead — see docs/deployment.md)
#   bash bootstrap.sh --repo <owner>/<repo> --email you@company.com
#
# What it does (idempotent — safe to re-run):
#   1. Entra app registrations "D365 Portal - API" (scope access_as_user) and "D365 Portal - Web" (SPA)
#   2. Deploy identity for GitHub Actions (OIDC federated credential) with Contributor +
#      User Access Administrator on the resource group only
#   3. GitHub secrets/variables (Postgres password is generated and never printed)
#   4. Runs the "Deploy to Azure" workflow and waits for it
#   5. Adds the live URL as a sign-in redirect on the Web app registration
set -euo pipefail

REPO=""; EMAIL=""; RG="rg-d365portal-prod"; LOC="uaenorth"; PREFIX="d365portal"; COMPANIES="USMF"
while [[ $# -gt 0 ]]; do
  case "$1" in
    --repo) REPO="$2"; shift 2 ;;
    --email) EMAIL="$2"; shift 2 ;;
    --rg) RG="$2"; shift 2 ;;
    --location) LOC="$2"; shift 2 ;;
    --prefix) PREFIX="$2"; shift 2 ;;
    --companies) COMPANIES="$2"; shift 2 ;;
    *) echo "Unknown option $1"; exit 1 ;;
  esac
done
[[ -n "$REPO" && -n "$EMAIL" ]] || { echo "Usage: bash bootstrap.sh --repo owner/repo --email you@company.com [--rg NAME] [--location uaenorth]"; exit 1; }

say() { printf '\n\033[1;36m==> %s\033[0m\n' "$*"; }
graph_patch() { az rest --method PATCH --uri "https://graph.microsoft.com/v1.0/applications/$1" \
                  --headers Content-Type=application/json --body "$2" --output none; }
app_id_by_name() { az ad app list --display-name "$1" --query "[0].appId" -o tsv; }
ensure_app() {  # name -> appId (creates multitenant app if missing)
  local id; id=$(app_id_by_name "$1")
  if [[ -z "$id" ]]; then
    id=$(az ad app create --display-name "$1" --sign-in-audience AzureADMultipleOrgs --query appId -o tsv)
  fi
  az ad sp show --id "$id" --output none 2>/dev/null || az ad sp create --id "$id" --output none
  echo "$id"
}

say "Checking sign-ins"
az account show --output none || { echo "Run 'az login' first."; exit 1; }
if ! gh auth status >/dev/null 2>&1; then
  echo "GitHub CLI is not signed in. Starting 'gh auth login' (choose GitHub.com → HTTPS → browser/device code)."
  gh auth login --hostname github.com --git-protocol https --web
fi
gh repo view "$REPO" --json name >/dev/null || { echo "Cannot access $REPO with your GitHub account."; exit 1; }

SUB=$(az account show --query id -o tsv)
TENANT=$(az account show --query tenantId -o tsv)
ME_OID=$(az ad signed-in-user show --query id -o tsv)
echo "Subscription: $(az account show --query name -o tsv) ($SUB)"
echo "Directory:    $TENANT"
echo "You (oid):    $ME_OID"
echo "Repo:         $REPO   Resource group: $RG ($LOC)"
read -r -p "Proceed with these settings? [y/N] " ok; [[ "$ok" =~ ^[Yy]$ ]] || exit 1

# ---------------------------------------------------------------- 1. Entra apps
say "Entra app: D365 Portal - API"
API_ID=$(ensure_app "D365 Portal - API")
API_OBJ=$(az ad app show --id "$API_ID" --query id -o tsv)
SCOPE_ID=$(az ad app show --id "$API_ID" --query "api.oauth2PermissionScopes[?value=='access_as_user'].id | [0]" -o tsv)
[[ -n "$SCOPE_ID" ]] || SCOPE_ID=$(cat /proc/sys/kernel/random/uuid)

say "Entra app: D365 Portal - Web"
WEB_ID=$(ensure_app "D365 Portal - Web")
WEB_OBJ=$(az ad app show --id "$WEB_ID" --query id -o tsv)

# API: identifier URI, v2 tokens, scope, and the Web app pre-authorized (no second consent prompt).
graph_patch "$API_OBJ" "{
  \"identifierUris\": [\"api://$API_ID\"],
  \"api\": {
    \"requestedAccessTokenVersion\": 2,
    \"oauth2PermissionScopes\": [{
      \"id\": \"$SCOPE_ID\", \"value\": \"access_as_user\", \"type\": \"User\", \"isEnabled\": true,
      \"adminConsentDisplayName\": \"Access D365 Portal\",
      \"adminConsentDescription\": \"Allows the D365 Portal web app to call its API as the signed-in user.\",
      \"userConsentDisplayName\": \"Access D365 Portal\",
      \"userConsentDescription\": \"Allows the D365 Portal to act on your behalf in the portal.\"
    }],
    \"preAuthorizedApplications\": [{ \"appId\": \"$WEB_ID\", \"delegatedPermissionIds\": [\"$SCOPE_ID\"] }]
  }
}"

# Web: SPA redirect URIs (portal URL added after deploy) + permission to the API scope.
EXISTING_URIS=$(az ad app show --id "$WEB_ID" --query "spa.redirectUris" -o json)
URIS=$(python3 -c "import json,sys; u=set(json.loads(sys.argv[1]) or []); u.update(['http://localhost:5173','http://localhost:8080']); print(json.dumps(sorted(u)))" "$EXISTING_URIS")
graph_patch "$WEB_OBJ" "{
  \"spa\": { \"redirectUris\": $URIS },
  \"requiredResourceAccess\": [{ \"resourceAppId\": \"$API_ID\", \"resourceAccess\": [{ \"id\": \"$SCOPE_ID\", \"type\": \"Scope\" }] }]
}"
echo "API client id: $API_ID"
echo "Web client id: $WEB_ID"

# ---------------------------------------------------------------- 2. Deploy identity
say "Resource group + GitHub deploy identity"
az group create --name "$RG" --location "$LOC" --output none
DEP_ID=$(ensure_app "d365portal-github-deploy")
DEP_OBJ=$(az ad app show --id "$DEP_ID" --query id -o tsv)
SUBJECT="repo:${REPO}:environment:production"
if [[ -z "$(az ad app federated-credential list --id "$DEP_OBJ" --query "[?subject=='$SUBJECT'].id | [0]" -o tsv)" ]]; then
  az ad app federated-credential create --id "$DEP_OBJ" --parameters "{
    \"name\": \"github-production\", \"issuer\": \"https://token.actions.githubusercontent.com\",
    \"subject\": \"$SUBJECT\", \"audiences\": [\"api://AzureADTokenExchange\"] }" --output none
fi
SCOPE="/subscriptions/$SUB/resourceGroups/$RG"
for role in "Contributor" "User Access Administrator"; do
  az role assignment create --assignee "$DEP_ID" --role "$role" --scope "$SCOPE" --output none 2>/dev/null || true
done
for p in Microsoft.App Microsoft.ContainerRegistry Microsoft.DBforPostgreSQL Microsoft.KeyVault Microsoft.OperationalInsights Microsoft.ManagedIdentity; do
  az provider register --namespace "$p" --output none
done

# ---------------------------------------------------------------- 3. GitHub config
say "GitHub secrets and variables on $REPO"
gh api -X PUT "repos/$REPO/environments/production" --silent
gh secret set AZURE_CLIENT_ID --repo "$REPO" --body "$DEP_ID"
gh secret set AZURE_TENANT_ID --repo "$REPO" --body "$TENANT"
gh secret set AZURE_SUBSCRIPTION_ID --repo "$REPO" --body "$SUB"
if ! gh secret list --repo "$REPO" | grep -q '^POSTGRES_ADMIN_PASSWORD'; then
  # Generated, sent straight to GitHub, never printed. Meets Azure's complexity rules.
  printf 'Pg%s!7a' "$(openssl rand -base64 48 | tr -dc 'A-Za-z0-9' | head -c 28)" \
    | gh secret set POSTGRES_ADMIN_PASSWORD --repo "$REPO"
fi
declare -A VARS=(
  [AZURE_RESOURCE_GROUP]="$RG" [AZURE_LOCATION]="$LOC" [NAME_PREFIX]="$PREFIX"
  [API_CLIENT_ID]="$API_ID" [WEB_CLIENT_ID]="$WEB_ID"
  [SUPERADMIN_ENTRA_TENANT_ID]="$TENANT" [SUPERADMIN_OBJECT_ID]="$ME_OID"
  [SEED_ENABLED]="true" [SEED_ADMIN_EMAIL]="$EMAIL" [SEED_ENTRA_TENANT_IDS]="$TENANT" [SEED_COMPANIES]="$COMPANIES"
)
for k in "${!VARS[@]}"; do gh variable set "$k" --repo "$REPO" --body "${VARS[$k]}"; done

# ---------------------------------------------------------------- 4. Deploy
say "Waiting 60s for role assignments to propagate, then running the deploy workflow"
sleep 60
gh workflow run deploy.yml --repo "$REPO" --ref main
sleep 10
RUN_ID=$(gh run list --repo "$REPO" --workflow deploy.yml --limit 1 --json databaseId -q '.[0].databaseId')
echo "Run: https://github.com/$REPO/actions/runs/$RUN_ID  (≈15–25 min on first deploy)"
gh run watch "$RUN_ID" --repo "$REPO" --exit-status --interval 30 || {
  echo "Deploy failed. Open the run link above and send the failing step's log to Claude."; exit 1; }

# ---------------------------------------------------------------- 5. Redirect URI
say "Registering the portal URL for sign-in"
FQDN=$(az containerapp show -n "${PREFIX}-web" -g "$RG" --query properties.configuration.ingress.fqdn -o tsv)
URL="https://$FQDN"
EXISTING_URIS=$(az ad app show --id "$WEB_ID" --query "spa.redirectUris" -o json)
URIS=$(python3 -c "import json,sys; u=set(json.loads(sys.argv[1]) or []); u.add(sys.argv[2]); print(json.dumps(sorted(u)))" "$EXISTING_URIS" "$URL")
graph_patch "$WEB_OBJ" "{ \"spa\": { \"redirectUris\": $URIS } }"
gh variable set SEED_ENABLED --repo "$REPO" --body "false"   # demo tenant exists now

say "Done"
echo "Portal:  $URL"
echo "Sign in with $EMAIL (super-admin + demo tenant admin)."
