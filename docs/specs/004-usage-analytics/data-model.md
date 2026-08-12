# Data Model

## Product event schema

[Product Event Contract v1](contracts/product-events.md) is the normative source for
product resources, span attributes, scopes, and bounded high-frequency summaries.

## Trusted manifest v1

[Analytics Features v1](contracts/analytics-features-v1.md) is the normative source for
the manifest format, validation limits, and trusted loading rules.

## Package provenance

`Unknown`, `LocalSource`, `MarketplaceCandidate`, and `VerifiedMarketplace` distinguish whether a package can use exact feature IDs. A verified record carries the canonical public Marketplace package ID, package SHA-256, and approved manifest SHA-256. Before loading a trusted package, the client snapshots at most 64 MiB of persisted Marketplace `.nupkg` bytes, validates both archive and installed manifests, captures at most 64 MiB of managed assemblies, compares each extracted DLL with its archive entry, and loads only the captured archive bytes. Exact packages have an empty `Assembly.Location`; package-root path discovery is not available under this opt-in mode. A package containing `runtimes/**/native/**` is deliberately generic because native code cannot be loaded from the immutable managed snapshot. Missing, unreadable, mismatching, or replaced evidence resolves to generic attribution.

## Quality metrics

The aggregate-only `Beutl.Quality` meter at version `v1` exports Delta temporality.
`beutl.quality.operation.duration` is a `double` histogram in `ms`, and
`beutl.quality.operation.total` is a `long` counter in `{operation}`. Both carry exactly
`beutl.operation` and `beutl.outcome`; operations are limited to `app.session.start`,
`project.open`, `preview.first_frame`, `preview.playback_summary`, and `media.export`.
`beutl.quality.unclean_session.total` is a tag-free `long` counter in `{session}`.
The duration instrument uses the OpenTelemetry SDK standard explicit bucket boundaries
and records minimum and maximum values; exponential histograms are not produced.
No installation, session, error, feature, path, or free-text value is a metric dimension.
Delta points from desktop and PackageTools processes merge into the same low-cardinality series.
The product recorded/rejected counters remain local implementation diagnostics and are
rejected by the final quality exporter; backend dashboards do not depend on them.
