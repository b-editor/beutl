FROM busybox:1.37.0-musl AS health-probe
FROM otel/opentelemetry-collector-contrib:0.120.0

# The upstream collector image is distroless. Copy one pinned static binary so
# Docker can probe the live health_check endpoint rather than merely parsing
# the mounted configuration.
COPY --from=health-probe /bin/busybox /busybox
