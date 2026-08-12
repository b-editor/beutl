-- Recompute the most recent 40 UTC days. D30 outcomes may arrive seven days
-- late, so a cohort can be 37 days old when its final event is ingested. Monthly
-- cohorts start at the month containing that daily boundary. All staged output
-- values are numeric and remain invisible until the final publication INSERT.
SET date_time_input_format = 'best_effort';

INSERT INTO beutl_analytics.analytics_rollups
    (DefinitionVersion, RunId, ComputedAt, MetricDate, MetricName, Dimension1, Dimension2, Dimension3, Value, SampleSize)
WITH
    'v1' AS definition_version,
    {run_id:String} AS run_id,
    now64(3, 'UTC') AS computed_at,
    toDate(now('UTC') - INTERVAL 40 DAY) AS daily_window_start,
    toStartOfMonth(daily_window_start) AS monthly_window_start,
    toDate(now('UTC') + INTERVAL 1 DAY) AS window_end
SELECT
    definition_version,
    run_id,
    computed_at,
    MetricDate,
    MetricName,
    Dimension1,
    Dimension2,
    Dimension3,
    Value,
    SampleSize
FROM
(
    -- Active installations, sessions, and rolling activity counts.
    SELECT
        toDate(Timestamp) AS MetricDate,
        'active_installations' AS MetricName,
        '' AS Dimension1,
        '' AS Dimension2,
        '' AS Dimension3,
        toFloat64(uniqExact(InstallationId)) AS Value,
        uniqExact(InstallationId) AS SampleSize
    FROM beutl_analytics.product_spans FINAL
    WHERE SpanName = 'app.session.start'
      AND Outcome = 'success'
        AND toDate(Timestamp) >= daily_window_start
      AND toDate(Timestamp) < window_end
    GROUP BY MetricDate

    UNION ALL

    SELECT
        toDate(Timestamp) AS MetricDate,
        'sessions' AS MetricName,
        '' AS Dimension1,
        '' AS Dimension2,
        '' AS Dimension3,
        toFloat64(uniqExact(SessionId)) AS Value,
        uniqExact(InstallationId) AS SampleSize
    FROM beutl_analytics.product_spans FINAL
    WHERE SpanName = 'app.session.start'
      AND Outcome = 'success'
        AND toDate(Timestamp) >= daily_window_start
      AND toDate(Timestamp) < window_end
    GROUP BY MetricDate

    UNION ALL

    -- First observed successful start is the new-installation definition.
    SELECT
        FirstDate AS MetricDate,
        'new_installations' AS MetricName,
        '' AS Dimension1,
        '' AS Dimension2,
        '' AS Dimension3,
        toFloat64(uniqExact(InstallationId)) AS Value,
        uniqExact(InstallationId) AS SampleSize
    FROM
    (
        SELECT
            InstallationId,
            min(toDate(Timestamp)) AS FirstDate
        FROM beutl_analytics.product_spans FINAL
        WHERE SpanName = 'app.session.start'
          AND Outcome = 'success'
          AND Timestamp >= now('UTC') - INTERVAL 90 DAY
          AND InstallationId != ''
        GROUP BY InstallationId
    )
    WHERE FirstDate >= daily_window_start
      AND FirstDate < window_end
    GROUP BY MetricDate

    UNION ALL

    -- A returning installation has an observed successful start before this
    -- day's successful start. The raw 90-day boundary is intentionally made
    -- explicit in dashboard/policy copy as an observed measure.
    SELECT
        ActiveDate AS MetricDate,
        'returning_installations' AS MetricName,
        '' AS Dimension1,
        '' AS Dimension2,
        '' AS Dimension3,
        toFloat64(uniqExact(InstallationId)) AS Value,
        uniqExact(InstallationId) AS SampleSize
    FROM
    (
        SELECT
            InstallationId,
            min(toDate(Timestamp)) AS FirstDate,
            groupUniqArray(toDate(Timestamp)) AS ActiveDates
        FROM beutl_analytics.product_spans FINAL
        WHERE SpanName = 'app.session.start'
          AND Outcome = 'success'
          AND Timestamp >= now('UTC') - INTERVAL 90 DAY
          AND InstallationId != ''
        GROUP BY InstallationId
    )
    ARRAY JOIN ActiveDates AS ActiveDate
    WHERE ActiveDate >= daily_window_start
      AND ActiveDate < window_end
      AND FirstDate < ActiveDate
    GROUP BY MetricDate

    UNION ALL

    SELECT
        CalendarDate AS MetricDate,
        'active_installations_7d' AS MetricName,
        '' AS Dimension1,
        '' AS Dimension2,
        '' AS Dimension3,
        toFloat64(uniqExact(InstallationId)) AS Value,
        uniqExact(InstallationId) AS SampleSize
    FROM
    (
        -- Expand each observed active day into its following seven UTC
        -- calendar days. This avoids a non-equi table join and still permits
        -- a distinct installation count for each WAU day.
        SELECT
            InstallationId,
            addDays(toDate(Timestamp), arrayJoin(range(7))) AS CalendarDate
        FROM beutl_analytics.product_spans FINAL
        WHERE SpanName = 'app.session.start'
          AND Outcome = 'success'
          AND toDate(Timestamp) >= subtractDays(daily_window_start, 6)
          AND toDate(Timestamp) < window_end
    ) AS spans
    WHERE CalendarDate >= daily_window_start
      AND CalendarDate < window_end
    GROUP BY MetricDate

    UNION ALL

    SELECT
        CalendarDate AS MetricDate,
        'active_installations_30d' AS MetricName,
        '' AS Dimension1,
        '' AS Dimension2,
        '' AS Dimension3,
        toFloat64(uniqExact(InstallationId)) AS Value,
        uniqExact(InstallationId) AS SampleSize
    FROM
    (
        -- MAU uses the same bounded expansion over the prior thirty UTC days.
        SELECT
            InstallationId,
            addDays(toDate(Timestamp), arrayJoin(range(30))) AS CalendarDate
        FROM beutl_analytics.product_spans FINAL
        WHERE SpanName = 'app.session.start'
          AND Outcome = 'success'
          AND toDate(Timestamp) >= subtractDays(daily_window_start, 29)
          AND toDate(Timestamp) < window_end
    ) AS spans
    WHERE CalendarDate >= daily_window_start
      AND CalendarDate < window_end
    GROUP BY MetricDate

    UNION ALL

    -- Version/OS/channel are low-cardinality product resource dimensions.
    SELECT
        toDate(Timestamp) AS MetricDate,
        'installation_distribution' AS MetricName,
        if(empty(AppVersion), 'unknown', AppVersion) AS Dimension1,
        if(empty(OsFamily), 'unknown', OsFamily) AS Dimension2,
        if(empty(ReleaseChannel), 'unknown', ReleaseChannel) AS Dimension3,
        toFloat64(uniqExact(InstallationId)) AS Value,
        uniqExact(InstallationId) AS SampleSize
    FROM beutl_analytics.product_spans FINAL
    WHERE SpanName = 'app.session.start'
      AND Outcome = 'success'
      AND toDate(Timestamp) >= daily_window_start
      AND toDate(Timestamp) < window_end
    GROUP BY MetricDate, Dimension1, Dimension2, Dimension3

    UNION ALL

    -- Per-session ordered journey funnel.
    SELECT
        MetricDate,
        'journey_same_session_funnel' AS MetricName,
        Stage AS Dimension1,
        '' AS Dimension2,
        '' AS Dimension3,
        toFloat64(count()) AS Value,
        uniqExact(InstallationId) AS SampleSize
    FROM
    (
        SELECT
            InstallationId,
            MetricDate,
            arrayJoin(arraySlice(
                ['startup', 'project', 'asset', 'first_edit', 'preview', 'save', 'export'],
                1,
                toUInt64(HighestStage))) AS Stage
        FROM
        (
            SELECT
                InstallationId,
                toDate(minIf(Timestamp, SpanName = 'app.session.start' AND Outcome = 'success')) AS MetricDate,
                windowFunnel(604800)(
                    toDateTime(Timestamp),
                    SpanName = 'app.session.start' AND Outcome = 'success',
                    (SpanName = 'project.open' OR SpanName = 'project.create') AND Outcome = 'success',
                    SpanName = 'asset.add' AND Outcome = 'success',
                    SpanName = 'editor.first_edit' AND Outcome = 'success',
                    SpanName = 'preview.first_frame' AND Outcome = 'success',
                    SpanName = 'project.save' AND Outcome = 'success',
                    SpanName = 'media.export' AND Outcome = 'success') AS HighestStage
            FROM beutl_analytics.product_spans FINAL
            WHERE toDate(Timestamp) >= daily_window_start
              AND toDate(Timestamp) < window_end
              AND SessionId != ''
            GROUP BY SessionId, InstallationId
        )
    )
    WHERE MetricDate >= daily_window_start
      AND MetricDate < window_end
    GROUP BY MetricDate, Stage

    UNION ALL

    -- First-seven-day activation funnel, grouped by the installation's first
    -- observed successful startup in UTC. It is intentionally observational;
    -- use the policy/dashboard copy rather than treating this as all usage.
    SELECT
        MetricDate,
        'activation_7d_funnel' AS MetricName,
        Stage AS Dimension1,
        '' AS Dimension2,
        '' AS Dimension3,
        toFloat64(count()) AS Value,
        toUInt64(count()) AS SampleSize
    FROM
    (
        SELECT
            MetricDate,
            arrayJoin(arraySlice(
                ['startup', 'project', 'asset', 'first_edit', 'preview', 'save', 'export'],
                1,
                toUInt64(HighestStage))) AS Stage
        FROM
        (
            SELECT
                toDate(FirstStart) AS MetricDate,
                windowFunnel(604800)(
                    toDateTime(Timestamp),
                    SpanName = 'app.session.start' AND Outcome = 'success',
                    (SpanName = 'project.open' OR SpanName = 'project.create') AND Outcome = 'success',
                    SpanName = 'asset.add' AND Outcome = 'success',
                    SpanName = 'editor.first_edit' AND Outcome = 'success',
                    SpanName = 'preview.first_frame' AND Outcome = 'success',
                    SpanName = 'project.save' AND Outcome = 'success',
                    SpanName = 'media.export' AND Outcome = 'success') AS HighestStage
            FROM
            (
                SELECT
                    InstallationId,
                    minIf(Timestamp, SpanName = 'app.session.start' AND Outcome = 'success') AS FirstStart
                FROM beutl_analytics.product_spans FINAL
                WHERE Timestamp >= now('UTC') - INTERVAL 90 DAY
                  AND InstallationId != ''
                GROUP BY InstallationId
            ) AS cohorts
            INNER JOIN beutl_analytics.product_spans AS events FINAL USING (InstallationId)
            WHERE events.Timestamp >= cohorts.FirstStart
              AND events.Timestamp <= cohorts.FirstStart + INTERVAL 7 DAY
            GROUP BY InstallationId, FirstStart
        )
    )
    WHERE MetricDate >= daily_window_start
      AND MetricDate < window_end
    GROUP BY MetricDate, Stage

    UNION ALL

    -- Feature IDs arrive only from the trusted feature catalogue or fixed
    -- generic/overflow categories enforced by the desktop application.
    SELECT
        toDate(Timestamp) AS MetricDate,
        'feature_adoption' AS MetricName,
        if(empty(FeatureId), 'generic', FeatureId) AS Dimension1,
        '' AS Dimension2,
        '' AS Dimension3,
        toFloat64(uniqExact(InstallationId)) AS Value,
        uniqExact(InstallationId) AS SampleSize
    FROM beutl_analytics.product_spans FINAL
    WHERE FeatureId != ''
      AND toDate(Timestamp) >= daily_window_start
      AND toDate(Timestamp) < window_end
    GROUP BY MetricDate, Dimension1

    UNION ALL

    -- Daily event outcome counts retain cancellation as a first-class result.
    SELECT
        toDate(Timestamp) AS MetricDate,
        'event_outcomes' AS MetricName,
        SpanName AS Dimension1,
        Outcome AS Dimension2,
        '' AS Dimension3,
        toFloat64(count()) AS Value,
        uniqExact(InstallationId) AS SampleSize
    FROM beutl_analytics.product_spans FINAL
    WHERE toDate(Timestamp) >= daily_window_start
      AND toDate(Timestamp) < window_end
    GROUP BY MetricDate, Dimension1, Dimension2

    UNION ALL

    -- Ingestion health is calculated from server arrival time. This source is
    -- intentionally not FINAL: the daily run observes duplicate deliveries
    -- before ReplacingMergeTree compaction folds them to their event-id winner.
    SELECT
        toDate(IngestedAt) AS MetricDate,
        'pipeline_duplicate_events' AS MetricName,
        '' AS Dimension1,
        '' AS Dimension2,
        '' AS Dimension3,
        toFloat64(count() - uniqExact(EventId)) AS Value,
        toUInt64(count()) AS SampleSize
    FROM beutl_analytics.product_spans
    WHERE EventId != ''
      AND toDate(IngestedAt) >= daily_window_start
      AND toDate(IngestedAt) < window_end
    GROUP BY MetricDate

    UNION ALL

    SELECT
        toDate(IngestedAt) AS MetricDate,
        'pipeline_late_events' AS MetricName,
        '' AS Dimension1,
        '' AS Dimension2,
        '' AS Dimension3,
        toFloat64(countIf(dateDiff('hour', Timestamp, IngestedAt) > 1)) AS Value,
        toUInt64(count()) AS SampleSize
    FROM beutl_analytics.product_spans FINAL
    WHERE toDate(IngestedAt) >= daily_window_start
      AND toDate(IngestedAt) < window_end
    GROUP BY MetricDate

    UNION ALL

    SELECT
        toDate(IngestedAt) AS MetricDate,
        'pipeline_ingest_lag_p95_minutes' AS MetricName,
        '' AS Dimension1,
        '' AS Dimension2,
        '' AS Dimension3,
        quantileTDigest(0.95)(if(IngestedAt > Timestamp, dateDiff('second', Timestamp, IngestedAt), 0) / 60.0) AS Value,
        toUInt64(count()) AS SampleSize
    FROM beutl_analytics.product_spans FINAL
    WHERE toDate(IngestedAt) >= daily_window_start
      AND toDate(IngestedAt) < window_end
    GROUP BY MetricDate

    UNION ALL

    SELECT
        toDate(IngestedAt) AS MetricDate,
        'pipeline_feature_cardinality' AS MetricName,
        '' AS Dimension1,
        '' AS Dimension2,
        '' AS Dimension3,
        toFloat64(uniqExactIf(FeatureId, FeatureId != '')) AS Value,
        toUInt64(count()) AS SampleSize
    FROM beutl_analytics.product_spans FINAL
    WHERE toDate(IngestedAt) >= daily_window_start
      AND toDate(IngestedAt) < window_end
    GROUP BY MetricDate

    UNION ALL

    -- p50/p95/p99 latency by fixed event name. Use the independently validated
    -- bounded semantic attribute rather than exporter-native span end time.
    SELECT
        MetricDate,
        'quality_latency_ms' AS MetricName,
        SpanName AS Dimension1,
        Percentile AS Dimension2,
        '' AS Dimension3,
        Value,
        SampleSize
    FROM
    (
        SELECT
            toDate(Timestamp) AS MetricDate,
            SpanName,
            arrayJoin(['p50', 'p95', 'p99']) AS Percentile,
            multiIf(
                Percentile = 'p50', quantileTDigest(0.50)(assumeNotNull(toFloat64OrNull(SpanAttributes['beutl.duration_ms']))),
                Percentile = 'p95', quantileTDigest(0.95)(assumeNotNull(toFloat64OrNull(SpanAttributes['beutl.duration_ms']))),
                quantileTDigest(0.99)(assumeNotNull(toFloat64OrNull(SpanAttributes['beutl.duration_ms'])))) AS Value,
            uniqExact(InstallationId) AS SampleSize
        FROM beutl_analytics.product_spans FINAL
        WHERE SpanName IN ('app.session.start', 'project.open', 'preview.first_frame', 'preview.playback_summary', 'media.export')
          AND Outcome IN ('success', 'partial', 'failed', 'cancelled')
          AND toFloat64OrNull(SpanAttributes['beutl.duration_ms']) BETWEEN 0 AND 86400000
          AND toDate(Timestamp) >= daily_window_start
          AND toDate(Timestamp) < window_end
        GROUP BY MetricDate, SpanName
    )

    UNION ALL

    -- UTC cohorts and D1/D7/D30 continuation. The raw 90-day window is long
    -- enough to recalculate D30 while results retain no cohort members.
    SELECT
        CohortDate AS MetricDate,
        'retention_rate' AS MetricName,
        Period AS Dimension1,
        '' AS Dimension2,
        '' AS Dimension3,
        if(CohortSize = 0, 0.0, toFloat64(Returned) / CohortSize) AS Value,
        CohortSize AS SampleSize
    FROM
    (
        SELECT
            CohortDate,
            Period,
            countIf(has(ActiveDates, addDays(CohortDate, toUInt16(replaceOne(Period, 'D', ''))))) AS Returned,
            toUInt64(count()) AS CohortSize
        FROM
        (
            SELECT
                InstallationId,
                toDate(minIf(Timestamp, SpanName = 'app.session.start' AND Outcome = 'success')) AS CohortDate,
                groupUniqArray(toDate(Timestamp)) AS ActiveDates
            FROM beutl_analytics.product_spans FINAL
            WHERE SpanName = 'app.session.start'
              AND Outcome = 'success'
              AND Timestamp >= now('UTC') - INTERVAL 90 DAY
              AND InstallationId != ''
            GROUP BY InstallationId
        )
        ARRAY JOIN ['D1', 'D7', 'D30'] AS Period
        WHERE CohortDate >= daily_window_start
          AND CohortDate < window_end
        GROUP BY CohortDate, Period
    )

    UNION ALL

    -- A monthly cohort is calculated before small-cell suppression so that
    -- Grafana can show a stable monthly view without retaining cohort members.
    SELECT
        toStartOfMonth(CohortDate) AS MetricDate,
        'retention_monthly_cohort' AS MetricName,
        Period AS Dimension1,
        '' AS Dimension2,
        '' AS Dimension3,
        if(sum(CohortSize) = 0, 0.0, toFloat64(sum(Returned)) / sum(CohortSize)) AS Value,
        toUInt64(sum(CohortSize)) AS SampleSize
    FROM
    (
        SELECT
            CohortDate,
            Period,
            countIf(has(ActiveDates, addDays(CohortDate, toUInt16(replaceOne(Period, 'D', ''))))) AS Returned,
            toUInt64(count()) AS CohortSize
        FROM
        (
            SELECT
                InstallationId,
                toDate(minIf(Timestamp, SpanName = 'app.session.start' AND Outcome = 'success')) AS CohortDate,
                groupUniqArray(toDate(Timestamp)) AS ActiveDates
            FROM beutl_analytics.product_spans FINAL
            WHERE SpanName = 'app.session.start'
              AND Outcome = 'success'
              AND Timestamp >= now('UTC') - INTERVAL 90 DAY
              AND InstallationId != ''
            GROUP BY InstallationId
        )
        ARRAY JOIN ['D1', 'D7', 'D30'] AS Period
        WHERE CohortDate >= monthly_window_start
          AND CohortDate < window_end
        GROUP BY CohortDate, Period
    )
    GROUP BY MetricDate, Period
)
WHERE
    (
        MetricName = 'retention_monthly_cohort'
        AND MetricDate >= monthly_window_start
        AND MetricDate < window_end
    )
    OR
    (
        MetricName != 'retention_monthly_cohort'
        AND MetricDate >= daily_window_start
        AND MetricDate < window_end
    );

-- Test-only failure injection exercises the publication boundary. A failed run
-- may leave unreferenced staging rows, but it cannot replace a visible snapshot.
SELECT throwIf({fail_after_stage:UInt8} = 1, 'intentional rollup publication failure');

INSERT INTO beutl_analytics.analytics_rollup_publications
    (DefinitionVersion, MetricDate, MetricFamily, RunId, CompletedAt)
WITH
    'v1' AS definition_version,
    {run_id:String} AS run_id,
    now64(3, 'UTC') AS completed_at,
    toDate(now('UTC') - INTERVAL 40 DAY) AS daily_publication_start,
    toStartOfMonth(daily_publication_start) AS monthly_publication_start,
    toDate(now('UTC') + INTERVAL 1 DAY) AS publication_end
SELECT
    definition_version,
    MetricDate,
    MetricFamily,
    run_id,
    completed_at
FROM
(
    -- Publish every daily target, including empty dates, so disappeared daily
    -- dimensions become invisible without replacing older retained dates.
    SELECT addDays(
        daily_publication_start,
        arrayJoin(range(toUInt64(dateDiff('day', daily_publication_start, publication_end))))) AS MetricDate,
        'daily' AS MetricFamily

    UNION ALL

    -- Monthly rows use month-start dates outside the daily target when the
    -- forty-day boundary crosses a month. Publish only those month keys rather
    -- than the intervening older daily dates, whose 13-month snapshots remain.
    SELECT addMonths(
        monthly_publication_start,
        arrayJoin(range(toUInt64(dateDiff('month', monthly_publication_start, toStartOfMonth(publication_end)) + 1)))) AS MetricDate,
        'monthly' AS MetricFamily
);
