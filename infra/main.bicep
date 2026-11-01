// D365 Portal — Azure hosting
//   Azure Container Apps (web: public, api: internal) + Azure Container Registry
//   + Azure Database for PostgreSQL Flexible Server + Key Vault + Log Analytics.
// Deployed in two passes by .github/workflows/deploy.yml:
//   1) deployApps=false  → platform resources (registry, vault, database, environment)
//   2) images built into the registry, then deployApps=true with imageTag

targetScope = 'resourceGroup'

@description('Azure region. UAE North is the closest GA region to Egypt/KSA with every service used here.')
param location string = resourceGroup().location

@description('Short lowercase prefix for resource names (3-10 chars).')
@minLength(3)
@maxLength(10)
param namePrefix string = 'd365portal'

@description('Create/update the two container apps. False on the first pass (no images yet).')
param deployApps bool = true

@description('Image tag to run (git SHA).')
param imageTag string = 'latest'

@description('PostgreSQL administrator login.')
param postgresAdminLogin string = 'portaladmin'

@secure()
@description('PostgreSQL administrator password (from a GitHub secret).')
param postgresAdminPassword string

@description('Entra "D365 Portal - API" app registration client id.')
param apiClientId string

@description('Entra "D365 Portal - Web" (SPA) app registration client id.')
param webClientId string

@description('Platform super-admin: Entra directory (tenant) id.')
param superAdminTenantId string = ''

@description('Platform super-admin: user object id.')
param superAdminObjectId string = ''

@description('Optional demo seed (first run).')
param seedEnabled bool = false
param seedAdminEmail string = ''
param seedEntraTenantIds string = ''
param seedCompanies string = 'USMF'

@description('Expose Swagger UI on the API (internal ingress only, so not reachable from the internet).')
param swaggerEnabled bool = false

var suffix = uniqueString(resourceGroup().id)
var acrName = take(replace('${namePrefix}acr${suffix}', '-', ''), 50)
var kvName = take('${namePrefix}-kv-${suffix}', 24)
var pgName = '${namePrefix}-pg-${suffix}'
var apiAppName = '${namePrefix}-api'
var webAppName = '${namePrefix}-web'
var tags = { app: 'd365-portal' }

// Built-in role ids
var roleAcrPull = '7f951dda-4ed3-4680-a7ca-43fe172d538d'
var roleKvSecretsUser = '4633458b-17de-408a-b874-0445c86b69e6'
var roleKvCryptoUser = '12338af0-0e69-4776-bea7-57ae8d297424'

// ---------------- Monitoring ----------------
resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${namePrefix}-logs'
  location: location
  tags: tags
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

// ---------------- Identity used by both apps ----------------
resource appIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: '${namePrefix}-id'
  location: location
  tags: tags
}

// ---------------- Container registry ----------------
resource acr 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: acrName
  location: location
  tags: tags
  sku: { name: 'Basic' }
  properties: {
    adminUserEnabled: false
  }
}

resource acrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(acr.id, appIdentity.id, roleAcrPull)
  scope: acr
  properties: {
    principalId: appIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleAcrPull)
  }
}

// ---------------- Key Vault (secrets + Data Protection key) ----------------
resource kv 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: kvName
  location: location
  tags: tags
  properties: {
    tenantId: subscription().tenantId
    sku: { family: 'A', name: 'standard' }
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 30
    enablePurgeProtection: true
    publicNetworkAccess: 'Enabled'
  }
}

resource kvSecretsUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(kv.id, appIdentity.id, roleKvSecretsUser)
  scope: kv
  properties: {
    principalId: appIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleKvSecretsUser)
  }
}

resource kvCryptoUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(kv.id, appIdentity.id, roleKvCryptoUser)
  scope: kv
  properties: {
    principalId: appIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleKvCryptoUser)
  }
}

// Wraps the ASP.NET Data Protection key ring (which encrypts tenant D365 client secrets).
resource dpKey 'Microsoft.KeyVault/vaults/keys@2023-07-01' = {
  parent: kv
  name: 'dataprotection'
  properties: {
    kty: 'RSA'
    keySize: 2048
    keyOps: [ 'wrapKey', 'unwrapKey' ]
  }
}

// ---------------- PostgreSQL ----------------
resource pg 'Microsoft.DBforPostgreSQL/flexibleServers@2022-12-01' = {
  name: pgName
  location: location
  tags: tags
  sku: {
    name: 'Standard_B1ms'
    tier: 'Burstable'
  }
  properties: {
    version: '16'
    administratorLogin: postgresAdminLogin
    administratorLoginPassword: postgresAdminPassword
    storage: { storageSizeGB: 32 }
    backup: {
      backupRetentionDays: 14
      geoRedundantBackup: 'Disabled'
    }
    highAvailability: { mode: 'Disabled' }
  }
}

resource pgDb 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2022-12-01' = {
  parent: pg
  name: 'd365portal'
  properties: {
    charset: 'UTF8'
    collation: 'en_US.utf8'
  }
}

// Allows Azure-hosted services (the container apps) to connect. Traffic is TLS + password.
// For stricter isolation, move to VNet integration + private access (see docs/deployment.md).
resource pgAllowAzure 'Microsoft.DBforPostgreSQL/flexibleServers/firewallRules@2022-12-01' = {
  parent: pg
  name: 'AllowAzureServices'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource dbConnectionSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: kv
  name: 'db-connection'
  properties: {
    value: 'Host=${pg.properties.fullyQualifiedDomainName};Port=5432;Database=d365portal;Username=${postgresAdminLogin};Password=${postgresAdminPassword};SSL Mode=Require;Trust Server Certificate=false'
  }
}

// ---------------- Container Apps environment ----------------
resource env 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: '${namePrefix}-env'
  location: location
  tags: tags
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logs.properties.customerId
        sharedKey: logs.listKeys().primarySharedKey
      }
    }
  }
}

var identityRef = {
  type: 'UserAssigned'
  userAssignedIdentities: { '${appIdentity.id}': {} }
}

// ---------------- API (internal ingress only) ----------------
resource api 'Microsoft.App/containerApps@2024-03-01' = if (deployApps) {
  name: apiAppName
  location: location
  tags: tags
  identity: identityRef
  dependsOn: [ acrPull, kvSecretsUser, kvCryptoUser, pgDb, pgAllowAzure ]
  properties: {
    managedEnvironmentId: env.id
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: false
        targetPort: 8080
        transport: 'auto'
        allowInsecure: false
      }
      registries: [ { server: acr.properties.loginServer, identity: appIdentity.id } ]
      secrets: [
        { name: 'db-connection', keyVaultUrl: dbConnectionSecret.properties.secretUri, identity: appIdentity.id }
      ]
    }
    template: {
      containers: [
        {
          name: 'api'
          image: '${acr.properties.loginServer}/d365portal-api:${imageTag}'
          resources: { cpu: json('0.5'), memory: '1Gi' }
          env: [
            { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
            { name: 'ConnectionStrings__Default', secretRef: 'db-connection' }
            { name: 'Database__Provider', value: 'Postgres' }
            { name: 'Database__MigrateOnStartup', value: 'true' }
            { name: 'AzureAd__TenantId', value: 'organizations' }
            { name: 'AzureAd__ClientId', value: apiClientId }
            { name: 'Platform__SuperAdmins__0__TenantId', value: superAdminTenantId }
            { name: 'Platform__SuperAdmins__0__ObjectId', value: superAdminObjectId }
            // TLS terminates at the Container Apps ingress; the API is not reachable from the internet.
            { name: 'Hosting__RequireHttps', value: 'false' }
            { name: 'Swagger__Enabled', value: string(swaggerEnabled) }
            { name: 'DataProtection__KeyVaultKeyId', value: dpKey.properties.keyUri }
            { name: 'AZURE_CLIENT_ID', value: appIdentity.properties.clientId } // DefaultAzureCredential → this identity
            { name: 'Seed__Enabled', value: string(seedEnabled) }
            { name: 'Seed__AdminEmail', value: seedAdminEmail }
            { name: 'Seed__EntraTenantIds', value: seedEntraTenantIds }
            { name: 'Seed__Companies', value: seedCompanies }
          ]
          probes: [
            { type: 'Liveness', httpGet: { path: '/health', port: 8080 }, initialDelaySeconds: 10, periodSeconds: 30 }
            { type: 'Readiness', httpGet: { path: '/health/db', port: 8080 }, initialDelaySeconds: 10, periodSeconds: 15 }
          ]
        }
      ]
      scale: { minReplicas: 1, maxReplicas: 3 }
    }
  }
}

// ---------------- Web (public HTTPS) ----------------
resource web 'Microsoft.App/containerApps@2024-03-01' = if (deployApps) {
  name: webAppName
  location: location
  tags: tags
  identity: identityRef
  dependsOn: [ acrPull ]
  properties: {
    managedEnvironmentId: env.id
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
        allowInsecure: false
      }
      registries: [ { server: acr.properties.loginServer, identity: appIdentity.id } ]
    }
    template: {
      containers: [
        {
          name: 'web'
          image: '${acr.properties.loginServer}/d365portal-web:${imageTag}'
          resources: { cpu: json('0.25'), memory: '0.5Gi' }
          env: [
            { name: 'API_UPSTREAM', value: deployApps ? 'https://${api.properties.configuration.ingress.fqdn}' : '' }
            { name: 'ENTRA_CLIENT_ID', value: webClientId }
            { name: 'ENTRA_AUTHORITY', value: 'https://login.microsoftonline.com/organizations' }
            { name: 'API_SCOPE', value: 'api://${apiClientId}/access_as_user' }
            { name: 'API_BASE_URL', value: '/api' }
          ]
          probes: [
            { type: 'Liveness', httpGet: { path: '/', port: 8080 }, initialDelaySeconds: 5, periodSeconds: 30 }
          ]
        }
      ]
      scale: { minReplicas: 1, maxReplicas: 3 }
    }
  }
}

output acrName string = acr.name
output acrLoginServer string = acr.properties.loginServer
output keyVaultName string = kv.name
output postgresHost string = pg.properties.fullyQualifiedDomainName
output webUrl string = deployApps ? 'https://${web.properties.configuration.ingress.fqdn}' : ''
