# ADR-0008: Daily report as an Azure Function (timer trigger), PDF with MIT-licensed PDFsharp 6.2.4

- **Status:** Accepted
- **Date:** 2026-11-06

## Context

Once a day, the previous UTC day must be summarised per meter (energy, average, peak, alerts) as
CSV and PDF and stored in Blob Storage. The job runs for seconds once per day, and has to be
re-runnable for a given day.

## Options considered

**Host**

1. **Azure Function, isolated worker, timer trigger.**
   - Purpose-built for scheduled work, and pays for seconds of execution.
   - The schedule lives in code (NCRONTAB).
   - An HTTP trigger can expose the same code for manual runs.
2. **A Container Apps Job on a cron schedule.** Also a good fit and simpler to host next to the
   other containers, but it has no built-in HTTP "run now" endpoint, and the brief asks for a Function.
3. **A `BackgroundService` inside the API.** It would run in every replica (so it needs leader
   election) and couples a batch job to an HTTP service.

**PDF library**

| Library | Licence | Notes |
|---|---|---|
| QuestPDF | Community licence, **commercial above a revenue threshold** | Excellent API; excluded for the same reason as FluentAssertions |
| PDFsharp/MigraDoc **6.5+** | **AGPL-3.0** (or commercial) | Discovered during implementation: the package fails at run time without a licence |
| PDFsharp/MigraDoc **6.2.4** | **MIT** | The last MIT release; cross-platform "Core" build; needs an explicit font resolver on Linux |

## Decision

- **An isolated-worker Function on .NET 10.**
  - `DailyReport` runs on a timer at 00:15 UTC, after the last hourly bucket of the day has settled.
  - `DailyReportOnDemand` is `POST /api/reports/daily?date=…`, protected by a function key.
- **Data:** one Dapper query over the hourly rollup plus the day's alerts.
  - Energy is the register difference (`max(last) - min(first)`).
  - The average is `sum(sum_power) / sum(samples)`, which is exact.
- **CSV** is written by hand: RFC 4180 quoting, invariant culture, UTF-8 without a BOM. One fixed
  table does not need a library.
- **PDF** uses MigraDoc **pinned to 6.2.4**, with a comment in `Directory.Packages.props` saying not
  to upgrade. The PDF uses IBM Plex Sans (SIL OFL), embedded as a resource and served through an
  `IFontResolver`, so rendering behaves the same on Windows, Linux and in containers.
- **Storage path:** `reports/daily/yyyy/MM/dd/gridpulse-daily-yyyy-MM-dd.{csv,pdf}`.
  - Uploads overwrite, so regenerating a day is idempotent.
  - In Azure, the Function reaches Blob Storage through a managed identity (`DefaultAzureCredential`
    + the account URL). Locally it uses Azurite with the well-known development key.
- **Local Functions keys:** a development-only key file is mounted into the Functions host
  (`deploy/functions/secrets`), so `docker compose` can call the HTTP trigger with
  `x-functions-key: local-dev-key`.

## Consequences

- PDFsharp 6.2.4 will receive no further fixes under MIT. If a security fix is ever needed, the
  options are to buy a licence, move to another MIT renderer, or generate HTML/CSV only.
  The PDF code is about 120 lines behind `DailyReportPdf.Render`, so the switch is contained.
- A pinned transitive dependency (`System.Security.Cryptography.Pkcs`) closes known
  vulnerabilities pulled in by PDFsharp.
- The lesson for the library policy: **check the licence of every version you upgrade to**, not
  only the first one you adopt. A package's licence can change between minor versions.
