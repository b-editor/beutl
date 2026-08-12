#!/bin/sh
set -eu

test -f /tmp/beutl-rollup-ready
max_age_seconds="${ROLLUP_MAX_AGE_SECONDS:-90000}"

clickhouse-client \
  --host clickhouse \
  --user beutl_rollup \
  --database beutl_analytics \
  --format TSVRaw \
  --query "
    SELECT if(
      count() > 0
      AND dateDiff('second', max(CompletedAt), now64(3, 'UTC')) <= ${max_age_seconds},
      1,
      0)
    FROM analytics_rollup_publications FINAL
    WHERE DefinitionVersion = 'v1'
  " \
  | grep -qx 1
