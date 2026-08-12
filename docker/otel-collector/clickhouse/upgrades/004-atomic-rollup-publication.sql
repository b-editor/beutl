-- Existing-database migration from the INSERT-only v1 rollup table. Pause the
-- scheduled rollup container while applying this file.
ALTER TABLE beutl_analytics.analytics_rollups
    ADD COLUMN IF NOT EXISTS RunId String AFTER DefinitionVersion,
    MODIFY ORDER BY
        (DefinitionVersion, MetricDate, MetricName, Dimension1, Dimension2, Dimension3, RunId);

CREATE TABLE IF NOT EXISTS beutl_analytics.analytics_rollup_publications
(
    DefinitionVersion LowCardinality(String),
    MetricDate Date,
    MetricFamily LowCardinality(String),
    RunId String,
    CompletedAt DateTime64(3)
)
ENGINE = ReplacingMergeTree(CompletedAt)
PARTITION BY toYYYYMM(MetricDate)
ORDER BY (DefinitionVersion, MetricDate, MetricFamily)
TTL MetricDate + INTERVAL 13 MONTH DELETE
SETTINGS index_granularity = 8192;

-- Upgrade the first atomic-publication schema, which keyed only by date. Its
-- existing rows receive the type's empty value and are therefore not exposed
-- by the family-aware view. Reconstruct each family's latest
-- successful staged run. A RunId is successful only when it occurs anywhere
-- in the non-FINAL publication history; failed staging runs never do. Looking
-- across the complete published-run set also recovers a daily mapping whose
-- old date-specific row has already been merged away after a monthly-only run
-- replaced the former date-only key.
ALTER TABLE beutl_analytics.analytics_rollup_publications
    ADD COLUMN IF NOT EXISTS MetricFamily LowCardinality(String) AFTER MetricDate,
    MODIFY ORDER BY (DefinitionVersion, MetricDate, MetricFamily);

INSERT INTO beutl_analytics.analytics_rollup_publications
    (DefinitionVersion, MetricDate, MetricFamily, RunId, CompletedAt)
SELECT
    r.DefinitionVersion,
    r.MetricDate,
    if(r.MetricName = 'retention_monthly_cohort', 'monthly', 'daily') AS MetricFamily,
    argMax(r.RunId, r.ComputedAt) AS RunId,
    now64(3, 'UTC') AS CompletedAt
FROM beutl_analytics.analytics_rollups AS r FINAL
INNER JOIN
(
    SELECT DISTINCT DefinitionVersion, RunId
    FROM beutl_analytics.analytics_rollup_publications
    WHERE RunId != ''
) AS successful
    ON r.DefinitionVersion = successful.DefinitionVersion
   AND r.RunId = successful.RunId
GROUP BY r.DefinitionVersion, r.MetricDate, MetricFamily;

-- Existing pre-atomic rows receive RunId's empty default. Seed each family as
-- a complete legacy snapshot. The next scheduled run replaces only its own
-- family/date keys without deleting this fallback.
INSERT INTO beutl_analytics.analytics_rollup_publications
    (DefinitionVersion, MetricDate, MetricFamily, RunId, CompletedAt)
SELECT
    DefinitionVersion,
    MetricDate,
    if(MetricName = 'retention_monthly_cohort', 'monthly', 'daily') AS MetricFamily,
    '',
    max(ComputedAt)
FROM beutl_analytics.analytics_rollups FINAL
WHERE RunId = ''
GROUP BY DefinitionVersion, MetricDate, MetricFamily;

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

CREATE OR REPLACE VIEW beutl_analytics.grafana_analytics
SQL SECURITY DEFINER
AS
SELECT
    DefinitionVersion, ComputedAt, MetricDate, MetricName,
    Dimension1, Dimension2, Dimension3, Value, SampleSize
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

CREATE OR REPLACE VIEW beutl_analytics.grafana_pipeline_metrics
SQL SECURITY DEFINER
AS
SELECT
    DefinitionVersion, ComputedAt, MetricDate, MetricName, Value, SampleSize
FROM beutl_analytics.published_analytics
WHERE MetricName IN
(
    'pipeline_duplicate_events',
    'pipeline_late_events',
    'pipeline_ingest_lag_p95_minutes',
    'pipeline_feature_cardinality'
);

GRANT INSERT, SELECT ON beutl_analytics.analytics_rollups TO beutl_rollup;
GRANT INSERT, SELECT ON beutl_analytics.analytics_rollup_publications TO beutl_rollup;
