# Deploying GridPulse to Azure

The `deploy-azure` workflow deploys GridPulse to **Azure Container Apps + Azure Database for
PostgreSQL Flexible Server + Storage**. It only runs when started by hand (`workflow_dispatch`),
and it authenticates with **OpenID Connect**: GitHub issues a short-lived token that Azure trusts,
so no Azure password or client secret is stored in GitHub.

> The workflow is ready but has not been run against a subscription. Infrastructure changes are
> validated on every push (`bicep build` + linter, `kubeconform`), and the workflow previews every
> deployment with `what-if` before applying it.

## What gets deployed

| Resource | SKU / settings | Purpose |
|---|---|---|
| Container Apps environment | Consumption workload profile, logs to Log Analytics | Hosts every service |
| `mosquitto` | 1 replica, **internal** TCP ingress on 1883 | MQTT broker (not reachable from the internet) |
| `ingestion` | exactly 1 replica, no ingress | Channels pipeline, alerts (ADR-0002) |
| `api` | 1–3 replicas, HTTP autoscaling, sticky sessions | REST API + SignalR hub |
| `web` | 1–2 replicas, sticky sessions | Blazor dashboard |
| `simulator` | 1 replica, 200 meters by default | Demo load |
| `reports` | Azure Functions on Container Apps, scales to zero | Daily CSV/PDF report |
| `migrator` | Container Apps **Job**, manual trigger | EF Core migrations + TimescaleDB setup |
| PostgreSQL Flexible Server 16 | Burstable B1ms, `timescaledb` allow-listed and preloaded | Database (TimescaleDB **Apache-2 edition**, see ADR-0001) |
| Storage account | shared keys **disabled**, `reports` container | Report files, Functions host state |
| Container Registry | Basic, admin user disabled | Images, built with ACR Tasks |
| User-assigned managed identity | `AcrPull`, `Storage Blob Data Owner` | Every app authenticates with it: no registry passwords, no storage keys |

**Cost.** Idle cost is dominated by the database (B1ms) and the always-on container apps.
Delete the resource group when you are done:

```bash
az group delete --name rg-gridpulse-demo --yes
```

## One-time setup

You need the Azure CLI, an Azure subscription, and admin rights on this GitHub repository.

### 1. Resource group

```bash
az group create --name rg-gridpulse-demo --location westeurope
```

### 2. An identity for GitHub Actions

Create the app registration and its service principal:

```bash
APP_ID=$(az ad app create --display-name gridpulse-github-deploy --query appId -o tsv)
```

```bash
az ad sp create --id "$APP_ID"
```

### 3. Trust this repository's `production` environment (federated credential)

Replace `<owner>` with your GitHub user or organisation:

```bash
az ad app federated-credential create --id "$APP_ID" --parameters '{
  "name": "github-production",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:<owner>/GridPulse:environment:production",
  "audiences": ["api://AzureADTokenExchange"]
}'
```

Only workflow runs that target the `production` environment of this repository can obtain a token.

### 4. Permissions on the resource group

The templates create resources, and they also assign two roles to the apps' managed identity, so
the deploying identity needs both of these:

```bash
SCOPE=$(az group show --name rg-gridpulse-demo --query id -o tsv)
```

```bash
az role assignment create --assignee "$APP_ID" --role Contributor --scope "$SCOPE"
```

```bash
az role assignment create --assignee "$APP_ID" --role "Role Based Access Control Administrator" --scope "$SCOPE"
```

For least privilege, add a condition to the second assignment that restricts it to the `AcrPull`
and `Storage Blob Data Owner` roles.

### 5. GitHub environment, variables and secret

In **Settings → Environments**, create an environment named `production`. Add yourself as a
**required reviewer**, so every deployment waits for an explicit approval.

Then add these **variables**. They are identifiers, not secrets.

| Variable | Value |
|---|---|
| `AZURE_CLIENT_ID` | `$APP_ID` |
| `AZURE_TENANT_ID` | `az account show --query tenantId -o tsv` |
| `AZURE_SUBSCRIPTION_ID` | `az account show --query id -o tsv` |
| `AZURE_RESOURCE_GROUP` | `rg-gridpulse-demo` |

And one **secret**:

| Secret | Value |
|---|---|
| `POSTGRES_ADMIN_PASSWORD` | A strong password, used to create the database server |

## Running a deployment

Go to **Actions → deploy-azure → Run workflow**. The workflow then:

1. Logs in to Azure with OIDC.
2. Runs a `what-if` preview of the infrastructure.
3. **Stage 1:** deploys the identity, registry, storage, database and monitoring.
4. Restarts PostgreSQL once if the TimescaleDB preload setting is pending.
5. **Stage 2:** builds the six images inside the registry (`az acr build`), tagged with the commit SHA.
6. **Stage 3:** deploys the Container Apps environment, the apps and the migrator job.
7. Starts the **migrator job** and waits for it to succeed.
8. Smoke-tests `GET /health/ready` on the API.
9. Writes the dashboard and API URLs to the run summary.

The deployment is split into stages because Container Apps need their images to exist when they are created.

## Differences from the local environment

| Topic | Local (`docker compose`) | Azure |
|---|---|---|
| TimescaleDB | Community edition: continuous aggregates, compression, retention | Apache-2 edition: rollups are views computed at query time; no automatic retention (ADR-0001) |
| Metrics | OTLP → Prometheus → Grafana | Container logs and platform metrics in Log Analytics. Exporting the custom OTel metrics needs Azure Monitor managed Prometheus or the Azure Monitor exporter (not included) |
| Email alerts | Mailpit | Not configured: set `Email__Host` / `Email__Port` on `ingestion` to a real SMTP relay (e.g. Azure Communication Services) |
| Network | Single compose network | Public endpoints for `web`, `api` and `reports` only; the database accepts Azure services only, with TLS. Private networking is the production upgrade (ADR-0005) |
