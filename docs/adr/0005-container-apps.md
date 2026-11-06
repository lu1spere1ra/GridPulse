# ADR-0005: Azure Container Apps rather than AKS or App Service

- **Status:** Accepted
- **Date:** 2026-11-06

## Context

GridPulse is seven containers:

- an MQTT broker that needs **TCP** ingress;
- a single-replica worker;
- an HTTP API with WebSockets (SignalR);
- a Blazor Server app;
- a simulator;
- an Azure Function;
- a one-shot migration job.

It must be deployable by one developer, cheaply, with infrastructure as code and no stored credentials.

## Options considered

1. **Azure Kubernetes Service.**
   - ✅ Full Kubernetes: operators, service mesh, any workload.
   - ❌ Someone has to own the cluster: upgrades, node pools, ingress controller, certificates,
     monitoring add-ons. For one developer and seven containers, most of the work would be the
     platform, not the product.
2. **App Service (Web App for Containers).**
   - ✅ Simple for HTTP apps.
   - ❌ No TCP ingress for the broker, awkward for workers and jobs, one plan per scaling profile.
3. **Azure Container Apps.**
   - ✅ Serverless containers on Kubernetes + KEDA, with none of the cluster operations.
   - ✅ HTTP and TCP ingress, sticky sessions, scale-to-zero, Jobs for one-shot work.
   - ✅ Azure Functions hosted in the same environment, and managed identity for registry pulls.
   - ❌ Less control: no custom operators or DaemonSets, fewer networking knobs.

## Decision

Azure Container Apps, defined in Bicep (`deploy/bicep`).

- One environment hosts every app.
- The migrator is a Container Apps **Job**.
- The report Function runs in the same environment (`kind: functionapp`).
- A single user-assigned managed identity pulls images and reaches Blob Storage.
- Plain Kubernetes manifests (`deploy/k8s`, validated by `kubeconform`) show the same system on
  any cluster, for teams that already run Kubernetes.

**Kept small on purpose for the demo:**

- PostgreSQL uses public access restricted to Azure services, with TLS.
- The database password is a `@secure()` parameter stored as a Container Apps secret.

## Consequences

- **Production upgrades not done here:**
  - VNet integration with private endpoints for PostgreSQL and Storage;
  - Key Vault references for secrets, or better, Entra ID authentication to PostgreSQL with the
    managed identity, so there is no password at all;
  - authenticated MQTT (client certificates or Event Grid's MQTT broker);
  - Entra ID authentication on the API and dashboard.
- **Rollouts.** The ingestion app is pinned to one replica. Container Apps creates a new revision
  before stopping the old one, so for a few seconds two ingestion replicas can be running. QoS 1
  redeliveries and idempotent writes make that safe for data. Live windows may briefly be computed
  twice, which is acceptable for a dashboard.
- **When to revisit.** If the platform grows to tens of services with platform engineers to run
  it, AKS becomes worth its operational cost; the Kubernetes manifests are the starting point.
