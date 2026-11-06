# ADR-0007: Blazor Web App with Interactive Server rendering, and a hand-written SVG chart

- **Status:** Accepted
- **Date:** 2026-11-06

## Context

The dashboard is an operator tool: a handful of concurrent users watching live data that changes
once per second. It needs live updates, server-side paging, forms for alert rules, and component
tests. The brief asks for a Blazor Web App with MudBlazor.

## Options considered

**Render mode**

1. **Interactive Server.** UI state lives on the server, and the browser holds a SignalR circuit.
   - ✅ The SignalR client to the API runs server-side, so the browser never calls the API: no CORS
     and no API credentials in the browser.
   - ✅ Small download, instant start.
   - ❌ Server memory per open tab, and the UI depends on the circuit's connection.
2. **Interactive WebAssembly.** The browser runs .NET and calls the API and hub directly.
   - ✅ Scales to many viewers.
   - ❌ CORS, token handling in the browser, several MB to download.
3. **Auto.** Both, plus the complexity of both.

**Charts**

1. MudBlazor 9's chart components. Their time-series support is new and its API is still changing.
2. A JS charting library through interop (ApexCharts, Chart.js): powerful, but it adds a JS dependency and interop.
3. A small SVG line chart written as a Razor component (~100 lines).

## Decision

- **Interactive Server, without prerendering.** Prerendering would run each page's initialisation
  twice and open the live feed during a render that cannot use it.
- **One `SignalRTelemetryFeed` per circuit (scoped service)**, behind `ITelemetryFeed`, so bUnit
  tests drive pages with a fake feed. It re-subscribes to meter groups after reconnecting, because
  SignalR group membership does not survive a new connection.
- **MudBlazor** for layout, data grid, forms, chips and snackbars.
- **A hand-written SVG `LineChart`.**
  - It draws one polyline, a horizontal grid and an optional threshold.
  - Blazor's diffing re-renders it once per second, which costs far less than a frame, with no JS interop.
  - Live series are kept in a fixed-size `RingBuffer`, so a page left open all day does not grow.

## Consequences

- **Each open dashboard tab costs a circuit on the web server**, which is fine for an operator tool.
  If the audience became "every customer sees their own meter", the switch is to WebAssembly (or
  Auto) for the customer-facing pages.
- **The API sees the web server as a single client**, so per-IP rate limits apply to the web server
  as a whole. With authentication, the limiter would partition by user instead.
- The chart has deliberately few features (no zoom, no tooltips). If those become requirements, a
  charting library behind the same component parameters is a contained change.
