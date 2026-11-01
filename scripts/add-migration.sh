#!/usr/bin/env bash
# Adds an EF Core migration for one provider.
#   scripts/add-migration.sh postgres AddSomething
#   scripts/add-migration.sh sqlserver InitialCreate     # first-time SQL Server setup
set -euo pipefail

provider="${1:?provider: postgres | sqlserver}"
name="${2:?migration name}"
cd "$(dirname "$0")/../backend"

dotnet tool restore >/dev/null 2>&1 || dotnet tool install --global dotnet-ef --version 8.0.11

case "$provider" in
  postgres)  ctx=PostgresAppDbContext;  dir=Migrations/Postgres ;;
  sqlserver) ctx=SqlServerAppDbContext; dir=Migrations/SqlServer ;;
  *) echo "Unknown provider '$provider'"; exit 1 ;;
esac

dotnet ef migrations add "$name" \
  --project src/D365Portal.Infrastructure \
  --startup-project src/D365Portal.Api \
  --context "$ctx" \
  --output-dir "$dir"
