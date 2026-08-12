-- Golden data covers a duplicate event ID, an out-of-order session, a delayed
-- event timestamp, a single-event installation, a trusted feature ID, and a
-- deliberately huge duration. It never uses user content or real IDs.
INSERT INTO beutl_analytics.product_spans
(
    Timestamp, TraceId, SpanId, ParentSpanId, TraceState, SpanName, SpanKind,
    ServiceName, ResourceAttributes, ScopeName, ScopeVersion, SpanAttributes,
    Duration, StatusCode, StatusMessage
)
VALUES
(
    now64(9, 'UTC') - INTERVAL 2 DAY, 'golden-trace-1', 'golden-span-1', '', '',
    'app.session.start', 'Internal', 'Beutl',
    map('service.version', 'golden', 'beutl.telemetry.stream', 'product', 'beutl.analytics.schema', 'v1', 'beutl.installation.id', 'golden-installation-a', 'beutl.session.id', 'golden-session-a', 'beutl.first_seen_month', '2026-08', 'beutl.release.channel', 'golden', 'os.type', 'windows', 'process.architecture', 'x64', 'beutl.renderer', 'golden'),
    'golden', 'v1', map('beutl.event.id', 'golden-duplicate-event', 'beutl.outcome', 'success'), 1000000, 'Ok', ''
),
(
    now64(9, 'UTC') - INTERVAL 1 DAY, 'golden-trace-1', 'golden-span-1b', '', '',
    'app.session.start', 'Internal', 'Beutl',
    map('service.version', 'golden', 'beutl.telemetry.stream', 'product', 'beutl.analytics.schema', 'v1', 'beutl.installation.id', 'golden-installation-a', 'beutl.session.id', 'golden-session-a', 'beutl.first_seen_month', '2026-08', 'beutl.release.channel', 'golden', 'os.type', 'windows', 'process.architecture', 'x64', 'beutl.renderer', 'golden'),
    'golden', 'v1', map('beutl.event.id', 'golden-duplicate-event', 'beutl.outcome', 'success'), 2000000, 'Ok', ''
),
(
    now64(9, 'UTC') - INTERVAL 1 DAY, 'golden-trace-2', 'golden-span-2', '', '',
    'project.save', 'Internal', 'Beutl',
    map('service.version', 'golden', 'beutl.telemetry.stream', 'product', 'beutl.analytics.schema', 'v1', 'beutl.installation.id', 'golden-installation-a', 'beutl.session.id', 'golden-session-a', 'beutl.first_seen_month', '2026-08', 'beutl.release.channel', 'golden', 'os.type', 'windows', 'process.architecture', 'x64', 'beutl.renderer', 'golden'),
    'golden', 'v1', map('beutl.event.id', 'golden-out-of-order-save', 'beutl.outcome', 'success'), 3000000, 'Ok', ''
),
(
    now64(9, 'UTC') - INTERVAL 6 DAY, 'golden-trace-3', 'golden-span-3', '', '',
    'project.open', 'Internal', 'Beutl',
    map('service.version', 'golden', 'beutl.telemetry.stream', 'product', 'beutl.analytics.schema', 'v1', 'beutl.installation.id', 'golden-installation-a', 'beutl.session.id', 'golden-session-a', 'beutl.first_seen_month', '2026-08', 'beutl.release.channel', 'golden', 'os.type', 'windows', 'process.architecture', 'x64', 'beutl.renderer', 'golden'),
    'golden', 'v1', map('beutl.event.id', 'golden-late-open', 'beutl.outcome', 'success'), 4000000, 'Ok', ''
),
(
    now64(9, 'UTC'), 'golden-trace-4', 'golden-span-4', '', '',
    'editor.first_edit', 'Internal', 'Beutl',
    map('service.version', 'golden', 'beutl.telemetry.stream', 'product', 'beutl.analytics.schema', 'v1', 'beutl.installation.id', 'golden-installation-b', 'beutl.session.id', 'golden-session-b', 'beutl.first_seen_month', '2026-08', 'beutl.release.channel', 'golden', 'os.type', 'linux', 'process.architecture', 'arm64', 'beutl.renderer', 'golden'),
    'golden', 'v1', map('beutl.event.id', 'golden-single-event', 'beutl.outcome', 'success', 'beutl.feature.id', 'builtin/editor/small'), 5000000, 'Ok', ''
),
(
    now64(9, 'UTC'), 'golden-trace-5', 'golden-span-5', '', '',
    'media.export', 'Internal', 'Beutl',
    map('service.version', 'golden', 'beutl.telemetry.stream', 'product', 'beutl.analytics.schema', 'v1', 'beutl.installation.id', 'golden-installation-c', 'beutl.session.id', 'golden-session-c', 'beutl.first_seen_month', '2026-08', 'beutl.release.channel', 'golden', 'os.type', 'macos', 'process.architecture', 'arm64', 'beutl.renderer', 'golden'),
    'golden', 'v1', map('beutl.event.id', 'golden-huge-duration', 'beutl.outcome', 'failed'), 18446744073709551600, 'Error', ''
),
(
    now64(9, 'UTC') - INTERVAL 1 DAY, 'golden-trace-6', 'golden-span-6', '', '',
    'extension.load', 'Internal', 'Beutl',
    map('service.version', 'golden', 'beutl.telemetry.stream', 'product', 'beutl.analytics.schema', 'v1', 'beutl.installation.id', 'golden-installation-dimension', 'beutl.session.id', 'golden-session-dimension', 'beutl.first_seen_month', '2026-08', 'beutl.release.channel', 'golden', 'os.type', 'windows', 'process.architecture', 'x64', 'beutl.renderer', 'golden'),
    'golden', 'v1', map('beutl.event.id', 'golden-dimension-event', 'beutl.outcome', 'success', 'beutl.feature.id', 'extension/com.example.editor/editor/old'), 1000000, 'Ok', ''
),
(
    now64(9, 'UTC') - INTERVAL 37 DAY, 'golden-trace-7', 'golden-span-7', '', '',
    'app.session.start', 'Internal', 'Beutl',
    map('service.version', 'golden', 'beutl.telemetry.stream', 'product', 'beutl.analytics.schema', 'v1', 'beutl.installation.id', 'golden-installation-d30', 'beutl.session.id', 'golden-session-d30-cohort', 'beutl.first_seen_month', '2026-07', 'beutl.release.channel', 'golden', 'os.type', 'linux', 'process.architecture', 'x64', 'beutl.renderer', 'golden'),
    'golden', 'v1', map('beutl.event.id', 'golden-d30-cohort', 'beutl.outcome', 'success'), 1000000, 'Ok', ''
),
(
    now64(9, 'UTC') - INTERVAL 3 DAY - INTERVAL 1 MINUTE, 'golden-trace-funnel', 'golden-span-funnel-0', '', '',
    'project.open', 'Internal', 'Beutl',
    map('service.version', 'golden', 'beutl.telemetry.stream', 'product', 'beutl.analytics.schema', 'v1', 'beutl.installation.id', 'golden-installation-funnel', 'beutl.session.id', 'golden-session-funnel', 'beutl.first_seen_month', '2026-08', 'beutl.release.channel', 'golden', 'os.type', 'windows', 'process.architecture', 'x64', 'beutl.renderer', 'golden'),
    'golden', 'v1', map('beutl.event.id', 'golden-funnel-early-project', 'beutl.outcome', 'success'), 1000000, 'Ok', ''
),
(
    now64(9, 'UTC') - INTERVAL 3 DAY, 'golden-trace-funnel', 'golden-span-funnel-1', '', '',
    'app.session.start', 'Internal', 'Beutl',
    map('service.version', 'golden', 'beutl.telemetry.stream', 'product', 'beutl.analytics.schema', 'v1', 'beutl.installation.id', 'golden-installation-funnel', 'beutl.session.id', 'golden-session-funnel', 'beutl.first_seen_month', '2026-08', 'beutl.release.channel', 'golden', 'os.type', 'windows', 'process.architecture', 'x64', 'beutl.renderer', 'golden'),
    'golden', 'v1', map('beutl.event.id', 'golden-funnel-start', 'beutl.outcome', 'success'), 1000000, 'Ok', ''
),
(
    now64(9, 'UTC') - INTERVAL 3 DAY + INTERVAL 1 MINUTE, 'golden-trace-funnel', 'golden-span-funnel-2', '', '',
    'project.open', 'Internal', 'Beutl',
    map('service.version', 'golden', 'beutl.telemetry.stream', 'product', 'beutl.analytics.schema', 'v1', 'beutl.installation.id', 'golden-installation-funnel', 'beutl.session.id', 'golden-session-funnel', 'beutl.first_seen_month', '2026-08', 'beutl.release.channel', 'golden', 'os.type', 'windows', 'process.architecture', 'x64', 'beutl.renderer', 'golden'),
    'golden', 'v1', map('beutl.event.id', 'golden-funnel-project', 'beutl.outcome', 'success'), 1000000, 'Ok', ''
),
(
    now64(9, 'UTC') - INTERVAL 3 DAY + INTERVAL 2 MINUTE, 'golden-trace-funnel', 'golden-span-funnel-3', '', '',
    'asset.add', 'Internal', 'Beutl',
    map('service.version', 'golden', 'beutl.telemetry.stream', 'product', 'beutl.analytics.schema', 'v1', 'beutl.installation.id', 'golden-installation-funnel', 'beutl.session.id', 'golden-session-funnel', 'beutl.first_seen_month', '2026-08', 'beutl.release.channel', 'golden', 'os.type', 'windows', 'process.architecture', 'x64', 'beutl.renderer', 'golden'),
    'golden', 'v1', map('beutl.event.id', 'golden-funnel-asset', 'beutl.outcome', 'success'), 1000000, 'Ok', ''
),
(
    now64(9, 'UTC') - INTERVAL 3 DAY + INTERVAL 3 MINUTE, 'golden-trace-funnel', 'golden-span-funnel-4', '', '',
    'editor.first_edit', 'Internal', 'Beutl',
    map('service.version', 'golden', 'beutl.telemetry.stream', 'product', 'beutl.analytics.schema', 'v1', 'beutl.installation.id', 'golden-installation-funnel', 'beutl.session.id', 'golden-session-funnel', 'beutl.first_seen_month', '2026-08', 'beutl.release.channel', 'golden', 'os.type', 'windows', 'process.architecture', 'x64', 'beutl.renderer', 'golden'),
    'golden', 'v1', map('beutl.event.id', 'golden-funnel-edit', 'beutl.outcome', 'success'), 1000000, 'Ok', ''
),
(
    now64(9, 'UTC') - INTERVAL 3 DAY + INTERVAL 4 MINUTE, 'golden-trace-funnel', 'golden-span-funnel-5', '', '',
    'preview.first_frame', 'Internal', 'Beutl',
    map('service.version', 'golden', 'beutl.telemetry.stream', 'product', 'beutl.analytics.schema', 'v1', 'beutl.installation.id', 'golden-installation-funnel', 'beutl.session.id', 'golden-session-funnel', 'beutl.first_seen_month', '2026-08', 'beutl.release.channel', 'golden', 'os.type', 'windows', 'process.architecture', 'x64', 'beutl.renderer', 'golden'),
    'golden', 'v1', map('beutl.event.id', 'golden-funnel-preview', 'beutl.outcome', 'success'), 1000000, 'Ok', ''
),
(
    now64(9, 'UTC') - INTERVAL 3 DAY + INTERVAL 5 MINUTE, 'golden-trace-funnel', 'golden-span-funnel-6', '', '',
    'project.save', 'Internal', 'Beutl',
    map('service.version', 'golden', 'beutl.telemetry.stream', 'product', 'beutl.analytics.schema', 'v1', 'beutl.installation.id', 'golden-installation-funnel', 'beutl.session.id', 'golden-session-funnel', 'beutl.first_seen_month', '2026-08', 'beutl.release.channel', 'golden', 'os.type', 'windows', 'process.architecture', 'x64', 'beutl.renderer', 'golden'),
    'golden', 'v1', map('beutl.event.id', 'golden-funnel-save', 'beutl.outcome', 'success'), 1000000, 'Ok', ''
),
(
    now64(9, 'UTC') - INTERVAL 3 DAY + INTERVAL 6 MINUTE, 'golden-trace-funnel', 'golden-span-funnel-7', '', '',
    'media.export', 'Internal', 'Beutl',
    map('service.version', 'golden', 'beutl.telemetry.stream', 'product', 'beutl.analytics.schema', 'v1', 'beutl.installation.id', 'golden-installation-funnel', 'beutl.session.id', 'golden-session-funnel', 'beutl.first_seen_month', '2026-08', 'beutl.release.channel', 'golden', 'os.type', 'windows', 'process.architecture', 'x64', 'beutl.renderer', 'golden'),
    'golden', 'v1', map('beutl.event.id', 'golden-funnel-export', 'beutl.outcome', 'success'), 1000000, 'Ok', ''
);

-- Forged future event timestamps cannot affect retention. The two arrivals
-- straddle the precise server-ingest + 90-day boundary; the first must expire
-- while the second remains even though both share the hostile event clock.
INSERT INTO beutl_analytics.product_spans
(
    Timestamp, IngestedAt, TraceId, SpanId, ParentSpanId, TraceState, SpanName,
    SpanKind, ServiceName, ResourceAttributes, ScopeName, ScopeVersion,
    SpanAttributes, Duration, StatusCode, StatusMessage
)
VALUES
(
    now64(9, 'UTC') + INTERVAL 365 DAY,
    now64(3, 'UTC') - INTERVAL 90 DAY - INTERVAL 1 MINUTE,
    'golden-trace-retention', 'golden-span-retention', '', '',
    'app.session.start', 'Internal', 'Beutl',
    map('service.version', 'golden', 'beutl.telemetry.stream', 'product', 'beutl.analytics.schema', 'v1', 'beutl.installation.id', 'golden-installation-retention', 'beutl.session.id', 'golden-session-retention', 'beutl.first_seen_month', '2026-08', 'beutl.release.channel', 'golden', 'os.type', 'windows', 'process.architecture', 'x64', 'beutl.renderer', 'golden'),
    'golden', 'v1', map('beutl.event.id', 'golden-retention-future', 'beutl.outcome', 'success'),
    1000000, 'Ok', ''
),
(
    now64(9, 'UTC') + INTERVAL 365 DAY,
    now64(3, 'UTC') - INTERVAL 90 DAY + INTERVAL 10 MINUTE,
    'golden-trace-retention-inside', 'golden-span-retention-inside', '', '',
    'app.session.start', 'Internal', 'Beutl',
    map('service.version', 'golden', 'beutl.telemetry.stream', 'product', 'beutl.analytics.schema', 'v1', 'beutl.installation.id', 'golden-installation-retention-inside', 'beutl.session.id', 'golden-session-retention-inside', 'beutl.first_seen_month', '2026-08', 'beutl.release.channel', 'golden', 'os.type', 'windows', 'process.architecture', 'x64', 'beutl.renderer', 'golden'),
    'golden', 'v1', map('beutl.event.id', 'golden-retention-inside', 'beutl.outcome', 'success'),
    1000000, 'Ok', ''
);
