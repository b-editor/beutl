# Beutl observability reference stack

This directory is the local/staging reference deployment for Beutl product
analytics and operational diagnostics. It is intentionally a collector and
storage configuration, not a production infrastructure deployment.

## Data boundary

| Signal | Destination | Gate | Retention |
| --- | --- | --- | --- |
| Product journey spans | ClickHouse `product_spans` | `UsageAnalytics` | 90 days from server ingest |
| Numeric rollups | ClickHouse published aggregate views | derived from product spans | 13 months |
| Low-cardinality quality metrics | Prometheus | `UsageAnalytics` | 395 days |
| Sanitized diagnostic traces | Tempo | existing trace gate | 90 days |
| Structured diagnostic logs | Loki | existing logging gate | 90 days |
| Full application logs | local files | no remote export | application-owned |

Only OTLP gRPC (`4317`), OTLP HTTP (`4318`), and Grafana (`3000`) are host
ports, and each is bound to `127.0.0.1`. ClickHouse, Tempo, Loki, Prometheus,
the collector health endpoint, and the Prometheus exporter remain on an
internal Docker network.

`product_spans` is accepted only when the following contract holds:

```text
resource.beutl.telemetry.stream = product
resource.beutl.analytics.schema = v1
scope.name / scope.version          = Beutl.ProductAnalytics / v1
span.name                         = one of the fixed event names
span.beutl.event.id               = lowercase 32-hex UUID
span.beutl.outcome                = success|partial|failed|cancelled|blocked|queued
```

The required product resource fields are fixed service name, bounded semantic
version, lowercase 32-hex installation/session IDs, `first_seen_month`, release
channel, OS family, architecture, and renderer. Trigger, error, feature, count,
resolution, and project-size values are checked against the desktop v1 catalog
or its canonical bounded pattern. Missing, malformed, overlong, or unknown
values fail closed just like an unknown schema or event name.

Trusted feature values are exactly `generic`, `overflow`,
`builtin/<kind>/<key>`, or
`extension/<canonical-marketplace-package-id>/<kind>/<key>`. Kinds and keys
use bounded lowercase kebab-case; package IDs use the desktop contract's
bounded lowercase marketplace form. CLR names and arbitrary package names are
not accepted. Event timestamps more than five minutes in the future or more
than seven days behind Collector time are rejected at ingress. Span end times
must not precede their start, end more than five minutes in the future, or make
the span exceed 24 hours. Latency rollups use the separately validated bounded
`beutl.duration_ms`, not the exporter-native end-time delta. Raw partitioning
and the 90-day TTL use ClickHouse's server-generated `IngestedAt` at the
engine's one-second TTL precision, without midnight truncation, so a forged
event timestamp cannot extend identifier retention.

The collector strips every attribute that is not listed in its allowlist. A
malformed product span is never persisted; it reaches only the bounded
`beutl.analytics.rejected.spans` count metric. Product event names, attributes,
and output values are defined in `otel-collector-config.yaml`. Instrumentation
scope attributes and trace state are removed, and accepted scope name/version
are canonicalized before the raw insert. Diagnostic scope, trace state, status
messages, log body, and severity text receive the same fixed-value treatment.

Installation and session IDs are retained only in the raw ClickHouse table.
The diagnostic trace/log and metric pipelines independently remove them, and
Prometheus has resource-to-label conversion disabled and drops the collector's
own `service.instance.id` before TSDB ingestion. There is no disk-backed
collector queue or event spool, so data not delivered in memory is discarded.
The Prometheus exporter omits source timestamps; Prometheus stamps each scrape,
preventing a periodically exported aggregate from disappearing after the
query lookback window.

Quality input is restricted to `Beutl.Quality` scope version `v1` from
`beutl.desktop` or `beutl.package-tools`. The only accepted instruments are
`beutl.quality.operation.duration`, `beutl.quality.operation.total`, and
`beutl.quality.unclean_session.total`. Duration and operation counts have
exactly the fixed `beutl.operation` and `beutl.outcome` tags; unclean-session
counts have no source tags. Both application processes export delta data. The
Collector converts it to cumulative Prometheus series and adds only the fixed
`beutl.host` enum after validation, allowing the two sources to be summed
without retaining their resource identity. Counter deltas must be positive
Int64 values. Duration histograms must contain a positive count, a finite sum
between zero and 24 hours per observation, bucket counts whose sum equals the
reported count, and exactly the OpenTelemetry standard explicit boundaries
`[0,5,10,25,50,75,100,250,500,750,1000,2500,5000,7500,10000]`. This fixes the
Prometheus `le` label set even when the public OTLP endpoint receives hostile
payloads; exemplars are rejected and source min/max values are not exported.

## Start and verify

```powershell
Set-Location docker/otel-collector
Copy-Item .env.example .env
# Replace the placeholder in .env with a unique local secret.
docker compose up -d
./scripts/verify.ps1 -Mode All
```

Grafana is available at `http://localhost:3000`. Its bootstrap password is a
required untracked `GRAFANA_ADMIN_PASSWORD` value in `.env`; compose and the
verification script reject missing, placeholder, or common default values. The
ClickHouse Grafana account has `SELECT` only on the suppression-aware aggregate
views; it has no grant on raw events.

The verification script covers compose/config validation, the live Collector
health endpoint, loopback-only bindings, OTLP fixture ingestion, ClickHouse
event-id deduplication and rollups, and privacy canaries across ClickHouse raw,
Tempo, Loki, and all Prometheus series/metadata. It executes every provisioned
ClickHouse, Prometheus, Loki, and Tempo panel target through Grafana and asserts
a known current-run fixture result from each datasource. Every signal mode
creates a nonce plus unique event and trace IDs, records pre-send baselines, and
checks server ingest or backend timestamps; persisted fixtures from an earlier
run cannot make a broken ingress pass. `-Mode Privacy -NoStart` submits its own
fixtures. `-Mode Negative` deliberately targets a closed OTLP endpoint and
asserts that the smoke test fails. Snapshots under `artifacts/` are disposable.

## Rollups and dashboards

`analytics-rollup` recalculates the most recent 40 UTC days after startup and
once per day. A D30 event delivered seven days late belongs to a cohort 37 days
in the past; forty complete UTC days cover that case plus date-boundary safety.
Monthly cohorts are recalculated from the beginning of the month containing
that daily boundary, so a late D30 event near a month edge updates the original
cohort month.

Each run writes a complete staging set under a new opaque `RunId`. Only after
every staging query succeeds does one publication insert replace the
date/family-to-run map. Daily metrics and monthly cohorts have independent
publication keys, so refreshing an older month-start cohort cannot hide the
retained daily snapshot for that date. Grafana views join rollups to that map,
so an interrupted insert cannot expose a partial run and a disappeared
dimension cannot remain visible from an older run. The health probe removes
its local ready marker before every run and
also requires a recent successful publication in ClickHouse. Numeric results
are retained for 13 months and never contain installation IDs, session IDs, or
membership state. Journey and activation funnels use ordered `windowFunnel`
sequences; an earlier out-of-order event cannot create a false stage or prevent
a later valid sequence from being counted.

Existing databases must apply the manual upgrades while the Collector and
scheduled rollup are paused:

```powershell
Get-Content clickhouse/upgrades/003-ingested-at-retention.sql |
  docker compose exec -T clickhouse clickhouse-client --multiquery
Get-Content clickhouse/upgrades/004-atomic-rollup-publication.sql |
  docker compose exec -T clickhouse clickhouse-client --multiquery
```

Upgrade 003 rebuilds the raw table with second-precision `IngestedAt` TTL and
arrival-month partitioning while preserving the ClickHouse exporter insert schema. Upgrade 004
adds run-scoped staging, independent daily/monthly publication families, and
seeds legacy rows into the publication map. Fresh volumes use the equivalent
definitions in `clickhouse/migrations/` directly.

The provisioned dashboards are:

- Usage Overview
- Product Journey
- Retention and Cohorts
- Feature Adoption
- Quality and Reliability
- Telemetry Pipeline Health
- Safe Diagnostics

Safe Diagnostics stores `trace_id` as sanitized Loki structured metadata. The
provisioned derived field turns that value into a clickable Tempo link; the
dashboard verifier resolves the linked current-run trace through both
datasources rather than relying on manual copy and paste.

An active installation means an installation with a successful
`app.session.start` in the selected UTC period. Every dashboard is explicitly
an observation of telemetry that was sent; it does not estimate offline or
opted-out activity.

## Production runbook requirements

DNS/TLS, IAM, secret management, backup topology, object storage, HA, and a
real deployment are intentionally outside this repository. Before production,
operators must:

- replace local no-password users and the Grafana bootstrap credential with
  secret-managed least-privilege credentials;
- terminate OTLP and Grafana with authenticated TLS and restrict network access;
- prevent IP addresses from being retained in proxy, collector, database, or
  access logs;
- apply the same 90-day / 13-month retention policy to database replicas,
  snapshots, WAL, object storage, and backups; and
- confirm schema-v1 collection is accepted end-to-end before releasing the
  desktop build.

The stack does not alter the GPL FFmpeg worker or any existing CI workflow.
