-- A run writes all aggregate rows under an opaque RunId, then atomically
-- publishes the run for every covered UTC date with one INSERT. Views join only
-- to published RunIds, so a failed or partial staging INSERT never replaces the
-- previous complete snapshot.
CREATE TABLE IF NOT EXISTS beutl_analytics.analytics_rollups
(
    DefinitionVersion LowCardinality(String),
    RunId String,
    ComputedAt DateTime64(3),
    MetricDate Date,
    MetricName LowCardinality(String),
    Dimension1 LowCardinality(String),
    Dimension2 LowCardinality(String),
    Dimension3 LowCardinality(String),
    Value Float64,
    SampleSize UInt64
)
ENGINE = ReplacingMergeTree(ComputedAt)
PARTITION BY toYYYYMM(MetricDate)
ORDER BY (DefinitionVersion, MetricDate, MetricName, Dimension1, Dimension2, Dimension3, RunId)
TTL MetricDate + INTERVAL 13 MONTH DELETE
SETTINGS index_granularity = 8192;

CREATE TABLE IF NOT EXISTS beutl_analytics.analytics_rollup_publications
(
    DefinitionVersion LowCardinality(String),
    MetricDate Date,
    -- Monthly cohort rows share month-start dates with daily metrics but have
    -- a wider recomputation window. Publish the families independently so a
    -- monthly refresh cannot hide a retained daily snapshot for that date.
    MetricFamily LowCardinality(String),
    RunId String,
    CompletedAt DateTime64(3)
)
ENGINE = ReplacingMergeTree(CompletedAt)
PARTITION BY toYYYYMM(MetricDate)
ORDER BY (DefinitionVersion, MetricDate, MetricFamily)
TTL MetricDate + INTERVAL 13 MONTH DELETE
SETTINGS index_granularity = 8192;

CREATE OR REPLACE VIEW beutl_analytics.published_analytics
SQL SECURITY DEFINER
AS
SELECT
    r.DefinitionVersion,
    r.RunId,
    r.ComputedAt,
    r.MetricDate,
    r.MetricName,
    r.Dimension1,
    r.Dimension2,
    r.Dimension3,
    r.Value,
    r.SampleSize
FROM beutl_analytics.analytics_rollups AS r FINAL
INNER JOIN beutl_analytics.analytics_rollup_publications AS p FINAL
    ON r.DefinitionVersion = p.DefinitionVersion
   AND r.MetricDate = p.MetricDate
   AND if(r.MetricName = 'retention_monthly_cohort', 'monthly', 'daily') = p.MetricFamily
   AND r.RunId = p.RunId;

-- Grafana receives only suppressed aggregate results through definer-secured
-- views. A cell with fewer than five installations is omitted entirely.
CREATE OR REPLACE VIEW beutl_analytics.grafana_analytics
SQL SECURITY DEFINER
AS
SELECT
    DefinitionVersion,
    ComputedAt,
    MetricDate,
    MetricName,
    Dimension1,
    Dimension2,
    Dimension3,
    Value,
    SampleSize
FROM beutl_analytics.published_analytics
WHERE SampleSize >= 5;

CREATE OR REPLACE VIEW beutl_analytics.grafana_pipeline_health
SQL SECURITY DEFINER
AS
SELECT
    DefinitionVersion,
    MetricDate,
    countIf(SampleSize < 5) AS SuppressedCellCount,
    max(ComputedAt) AS ComputedAt
FROM beutl_analytics.published_analytics
GROUP BY DefinitionVersion, MetricDate;

-- Pipeline values are already system-wide numeric counters/percentiles, not
-- installation breakdowns. Expose this separate aggregate-only view so the
-- operational dashboard can detect a low-volume ingestion fault without
-- weakening small-cell suppression for product analytics.
CREATE OR REPLACE VIEW beutl_analytics.grafana_pipeline_metrics
SQL SECURITY DEFINER
AS
SELECT
    DefinitionVersion,
    ComputedAt,
    MetricDate,
    MetricName,
    Value,
    SampleSize
FROM beutl_analytics.published_analytics
WHERE MetricName IN
(
    'pipeline_duplicate_events',
    'pipeline_late_events',
    'pipeline_ingest_lag_p95_minutes',
    'pipeline_feature_cardinality'
);

CREATE USER IF NOT EXISTS beutl_grafana IDENTIFIED WITH no_password;
GRANT SELECT ON beutl_analytics.grafana_analytics TO beutl_grafana;
GRANT SELECT ON beutl_analytics.grafana_pipeline_health TO beutl_grafana;
GRANT SELECT ON beutl_analytics.grafana_pipeline_metrics TO beutl_grafana;

-- The scheduled job is deliberately separate from the write-only exporter and
-- the Grafana read-only account. It stages a complete run, publishes it, and
-- probes only the last successful publication time.
CREATE USER IF NOT EXISTS beutl_rollup IDENTIFIED WITH no_password;
GRANT SELECT ON beutl_analytics.product_spans TO beutl_rollup;
GRANT INSERT, SELECT ON beutl_analytics.analytics_rollups TO beutl_rollup;
GRANT INSERT, SELECT ON beutl_analytics.analytics_rollup_publications TO beutl_rollup;
