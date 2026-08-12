#!/bin/sh
set -eu

# Recompute a moving 40-day window immediately and once a day. Each pass stages
# rows under a unique run ID, then publishes the complete date range atomically.
# Removing the marker before every pass makes an in-progress or failed run
# unhealthy even when the container process itself is still alive.
while true; do
  rm -f /tmp/beutl-rollup-ready
  run_id="$(tr -d '-' < /proc/sys/kernel/random/uuid)"
  clickhouse-client \
    --host clickhouse \
    --user beutl_rollup \
    --database beutl_analytics \
    --multiquery \
    --param_run_id "$run_id" \
    --param_fail_after_stage 0 \
    < /opt/beutl/recompute.sql
  touch /tmp/beutl-rollup-ready
  sleep 86400
done
