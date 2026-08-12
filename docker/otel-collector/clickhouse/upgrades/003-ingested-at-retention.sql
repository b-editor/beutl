-- Existing-database migration. Stop the Collector before applying this file.
-- ALTER immediately moves TTL enforcement to the exact server arrival instant;
-- no midnight truncation is permitted. The
-- copy-and-EXCHANGE rebuild then moves the immutable partition key atomically.
ALTER TABLE beutl_analytics.product_spans
    MODIFY TTL toDateTime(IngestedAt) + INTERVAL 90 DAY DELETE;

DROP TABLE IF EXISTS beutl_analytics.product_spans_ingested_at_v2;
CREATE TABLE beutl_analytics.product_spans_ingested_at_v2
AS beutl_analytics.product_spans
ENGINE = ReplacingMergeTree(IngestedAt)
PARTITION BY toYYYYMM(IngestedAt)
ORDER BY EventId
TTL toDateTime(IngestedAt) + INTERVAL 90 DAY DELETE
SETTINGS index_granularity = 8192;

INSERT INTO beutl_analytics.product_spans_ingested_at_v2
(
    Timestamp, IngestedAt, TraceId, SpanId, ParentSpanId, TraceState, SpanName,
    SpanKind, ServiceName, ResourceAttributes, ScopeName, ScopeVersion,
    SpanAttributes, Duration, StatusCode, StatusMessage,
    `Events.Timestamp`, `Events.Name`, `Events.Attributes`,
    `Links.TraceId`, `Links.SpanId`, `Links.TraceState`, `Links.Attributes`
)
SELECT
    Timestamp, IngestedAt, TraceId, SpanId, ParentSpanId, TraceState, SpanName,
    SpanKind, ServiceName, ResourceAttributes, ScopeName, ScopeVersion,
    SpanAttributes, Duration, StatusCode, StatusMessage,
    `Events.Timestamp`, `Events.Name`, `Events.Attributes`,
    `Links.TraceId`, `Links.SpanId`, `Links.TraceState`, `Links.Attributes`
FROM beutl_analytics.product_spans;

EXCHANGE TABLES
    beutl_analytics.product_spans
    AND beutl_analytics.product_spans_ingested_at_v2;
DROP TABLE beutl_analytics.product_spans_ingested_at_v2;

GRANT INSERT ON beutl_analytics.product_spans TO beutl_ingest;
GRANT SELECT ON beutl_analytics.product_spans TO beutl_rollup;
