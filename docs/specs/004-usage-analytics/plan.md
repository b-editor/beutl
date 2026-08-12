# Implementation Plan: Privacy-preserving usage analytics

## Architecture

- Keep event primitives in `Beutl.Core` with only BCL `ActivitySource` and `Meter` dependencies.
- Add a shared MIT `Beutl.Telemetry` project for OpenTelemetry provider lifecycle, consent gating, identity persistence, semantic event validation, five-minute bounded summaries (with shutdown flush and 256-feature overflow protection), and safe remote diagnostics.
- Reference that project from the desktop executable and PackageTools UI instead of source-linking `Telemetry.cs`.
- Keep exact extension type mappings internal. A static package manifest is evaluated only when persisted Marketplace provenance agrees with release metadata.

## Delivery Order

1. Add the specification and event/manifest contracts.
2. Add `UsageAnalytics`, identity storage, provider lifecycle, and safe diagnostic logging.
3. Move both executable hosts to the shared telemetry project and remove source linking.
4. Instrument bounded product journeys and fix observed outcome accounting defects.
5. Add SDK packing, package provenance, client release metadata, and manifest validation.
6. Add NUnit/headless tests and run focused then solution verification.

## Boundaries

- No MIT project gains a compile reference to `Beutl.FFmpegWorker`.
- Product identifiers are never used as metric labels or generic trace/log attributes.
- The Collector, ClickHouse, Grafana, Tempo, Loki, Prometheus, and Web policy implementation are maintained separately.
