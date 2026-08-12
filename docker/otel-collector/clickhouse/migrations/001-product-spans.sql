-- This DDL is intentionally compatible with the exact INSERT column list of
-- otelcol-contrib 0.120.0's ClickHouse trace exporter.  Do not rename or
-- retarget the exporter without updating this migration in the same change.
CREATE DATABASE IF NOT EXISTS beutl_analytics;

CREATE TABLE IF NOT EXISTS beutl_analytics.product_spans
(
    Timestamp DateTime64(9) CODEC(Delta, ZSTD(1)),
    -- Arrival time is server-generated, never supplied by clients. It makes
    -- late-delivery health observable and gives ReplacingMergeTree a monotonic
    -- version for event-id deduplication.
    IngestedAt DateTime64(3) DEFAULT now64(3, 'UTC') CODEC(Delta, ZSTD(1)),
    TraceId String CODEC(ZSTD(1)),
    SpanId String CODEC(ZSTD(1)),
    ParentSpanId String CODEC(ZSTD(1)),
    TraceState String CODEC(ZSTD(1)),
    SpanName LowCardinality(String) CODEC(ZSTD(1)),
    SpanKind LowCardinality(String) CODEC(ZSTD(1)),
    ServiceName LowCardinality(String) CODEC(ZSTD(1)),
    ResourceAttributes Map(LowCardinality(String), String) CODEC(ZSTD(1)),
    ScopeName String CODEC(ZSTD(1)),
    ScopeVersion String CODEC(ZSTD(1)),
    SpanAttributes Map(LowCardinality(String), String) CODEC(ZSTD(1)),
    Duration UInt64 CODEC(ZSTD(1)),
    StatusCode LowCardinality(String) CODEC(ZSTD(1)),
    StatusMessage String CODEC(ZSTD(1)),
    Events Nested
    (
        Timestamp DateTime64(9),
        Name LowCardinality(String),
        Attributes Map(LowCardinality(String), String)
    ) CODEC(ZSTD(1)),
    Links Nested
    (
        TraceId String,
        SpanId String,
        TraceState String,
        Attributes Map(LowCardinality(String), String)
    ) CODEC(ZSTD(1)),

    -- The collector inserts the fields above.  These materialized columns
    -- enforce event-id based ReplacingMergeTree deduplication without changing
    -- the exporter's wire schema.
    EventId String MATERIALIZED SpanAttributes['beutl.event.id'],
    InstallationId String MATERIALIZED ResourceAttributes['beutl.installation.id'],
    SessionId String MATERIALIZED ResourceAttributes['beutl.session.id'],
    CohortMonth LowCardinality(String) MATERIALIZED ResourceAttributes['beutl.first_seen_month'],
    ReleaseChannel LowCardinality(String) MATERIALIZED ResourceAttributes['beutl.release.channel'],
    OsFamily LowCardinality(String) MATERIALIZED ResourceAttributes['os.type'],
    Architecture LowCardinality(String) MATERIALIZED ResourceAttributes['process.architecture'],
    Renderer LowCardinality(String) MATERIALIZED ResourceAttributes['beutl.renderer'],
    AppVersion LowCardinality(String) MATERIALIZED ResourceAttributes['service.version'],
    Outcome LowCardinality(String) MATERIALIZED SpanAttributes['beutl.outcome'],
    FeatureId LowCardinality(String) MATERIALIZED SpanAttributes['beutl.feature.id'],

    INDEX idx_event_id EventId TYPE bloom_filter(0.001) GRANULARITY 1,
    INDEX idx_session_id SessionId TYPE bloom_filter(0.001) GRANULARITY 1,
    INDEX idx_span_name SpanName TYPE set(64) GRANULARITY 4
)
ENGINE = ReplacingMergeTree(IngestedAt)
-- Retention and partitioning are based only on the server-generated arrival
-- clock. A forged future event timestamp therefore cannot retain an identifier
-- beyond the 90-day raw-data policy.
PARTITION BY toYYYYMM(IngestedAt)
ORDER BY EventId
TTL toDateTime(IngestedAt) + INTERVAL 90 DAY DELETE
SETTINGS index_granularity = 8192;

-- The raw table is intentionally not exposed to Grafana.  The exporter uses
-- this write-only account and has no DDL or read grants.
CREATE USER IF NOT EXISTS beutl_ingest IDENTIFIED WITH no_password;
GRANT INSERT ON beutl_analytics.product_spans TO beutl_ingest;
