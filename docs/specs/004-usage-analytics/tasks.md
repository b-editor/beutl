# Tasks

- [x] Specify consent, privacy, event, and trusted extension requirements.
- [x] Add shared telemetry lifecycle, identity, validation, and safe diagnostic components.
- [x] Migrate desktop and PackageTools hosts away from linked telemetry source.
- [x] Add configuration/UI migration and immediate consent behavior.
- [x] Add semantic journey instrumentation and bounded summaries.
- [x] Add static SDK manifest packing and package provenance validation.
- [x] Add Marketplace release client contract support.
- [x] Add NUnit/headless test coverage and execute verification.

## Verification record

- `dotnet build Beutl.slnx --no-restore` completed with zero warnings and zero errors.
- Focused product, final export-gate, quality-metric, identity, manifest, and provenance
  NUnit coverage passed (52 tests).
- Focused telemetry consent/reset/migration, built-in catalog, first-edit, and PackageTools
  session headless coverage passed (26 tests). The full headless project passed all 167 tests;
  its host then hit the existing XAudio2 finalizer crash after the assertions completed.
- The SDK package and an isolated package consumer both packed successfully; the consumer
  archive contained `beutl/analytics-features.v1.json` at the canonical path.
- The complete solution test command still has four pre-existing Unit failures in
  `AutoSaveServiceTests` / `ProxyResolverTests`. After test completion, an XAudio2
  finalizer crash can still abort a host; neither failure touches this feature's files or
  its focused tests.
