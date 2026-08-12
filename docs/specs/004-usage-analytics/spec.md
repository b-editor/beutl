# Feature Specification: Privacy-preserving usage analytics

**Feature Branch**: `004-usage-analytics`

**Created**: 2026-08-11

**Status**: Complete

**Input**: Collect detailed, privacy-preserving product usage and reliability data and visualize it for Beutl developers and operators.

## User Scenarios & Testing

### User Story 1 - Consent-controlled product analytics (Priority: P1)

An existing or new desktop user can independently allow or refuse usage analytics. The application must immediately stop creating or exporting product events when consent is off, without requiring a restart.

**Why this priority**: No analytics value justifies collecting data without a reliable consent boundary.

**Independent Test**: An in-memory exporter observes no spans while disabled, spans after enabling, and no queued span after revocation.

**Acceptance Scenarios**:

1. **Given** an existing configured telemetry preference without the new choice, **When** configuration is restored, **Then** `UsageAnalytics` inherits the historical Application choice once.
2. **Given** usage analytics is enabled, **When** the user turns it off, **Then** future product spans and metrics are not exported immediately.
3. **Given** usage analytics is disabled, **When** the user resets the identifier, **Then** no installation identifier remains on disk.

### User Story 2 - Useful semantic journeys (Priority: P1)

An operator can understand startup, project, preview, export, package, and agent journeys without receiving file paths, project content, text input, or raw click streams.

**Why this priority**: The feature exists to make product and reliability decisions, not to collect arbitrary diagnostic data.

**Independent Test**: Contract tests assert fixed event names and attribute allowlists; PII/path canaries cannot reach exported data.

**Acceptance Scenarios**:

1. **Given** a user opens a project and exports media, **When** operations complete, **Then** the system produces fixed semantic spans with a fixed outcome vocabulary.
2. **Given** a high-frequency edit or tool operation, **When** repeated actions occur, **Then** the system emits one bounded summary every five minutes (and flushes it at normal shutdown) rather than one span per action. At most 256 exact feature IDs are retained per period; further IDs are coalesced as `overflow`.

### User Story 3 - Trusted extension feature attribution (Priority: P2)

An operator can identify adoption of approved Marketplace extension features while untrusted, sideloaded, legacy, and malformed packages remain generic.

**Why this priority**: Exact type names and arbitrary plugin supplied strings must never become telemetry identifiers.

**Independent Test**: Manifest validation and provenance tests verify exact IDs only after package and manifest hashes match trusted release metadata.

**Acceptance Scenarios**:

1. **Given** a verified Marketplace package and approved manifest, **When** an exact registered type is used, **Then** the emitted feature ID is `extension/<canonical-marketplace-package-id>/<kind>/<key>` and is bounded.
2. **Given** any validation or provenance mismatch, **When** that type is used, **Then** only `generic` is emitted.

## Requirements

### Functional Requirements

- **FR-001**: The application MUST use a random, resettable installation identifier that is never derived from a machine or account identifier.
- **FR-002**: Product events MUST use a fixed schema, name set, attribute allowlist, and outcome vocabulary.
- **FR-003**: Product analytics, quality metrics, operational traces, and safe diagnostics MUST be independently configured and routed.
- **FR-004**: The application MUST preserve full local logs but MUST NOT forward broad application logs remotely.
- **FR-005**: The extension manifest MUST be static package content; there MUST be no plugin runtime telemetry registration API.
- **FR-006**: The SDK MUST pack a user supplied manifest at `beutl/analytics-features.v1.json`.
- **FR-007**: Existing package state MUST persist provenance and only attribute an extension exactly after release verification.
- **FR-008**: New logic MUST have NUnit or headless UI coverage.
- **FR-009**: The final desktop export boundary MUST reject unknown product names, scopes, resources, tags, values, and PII canaries even when a friend assembly creates an Activity directly.
- **FR-010**: Exact Marketplace attribution MUST rehash the persisted `.nupkg`, validate the archive and installed manifests, compare extracted assemblies with archive entries before load, and load the immutable verified bytes that are later eligible for registration. Snapshot capture is bounded to 64 MiB for the artifact and 64 MiB for managed assembly bytes. A package with in-package native runtime assets MUST remain generic unless those assets can be loaded from equivalent immutable verified bytes. Exact-attribution packages MUST NOT rely on `Assembly.Location`, which is empty for their immutable stream-loaded modules.

### Key Entities

- **Telemetry identity**: Random installation ID and process-scoped session ID.
- **Product event**: One bounded semantic operation represented as an Activity with fixed schema attributes.
- **Trusted feature manifest**: Static, validated mapping from a Marketplace package's concrete type to a feature key.
- **Package provenance**: Persisted source and release/hash evidence used to decide whether exact feature attribution is permitted.

## Success Criteria

### Measurable Outcomes

- **SC-001**: A consent change prevents subsequent export before the settings operation returns.
- **SC-002**: Every product event passes a fixed schema validator and contains no disallowed canary data.
- **SC-003**: Repeated high-frequency operations export at most one summary per fixed five-minute period and feature bucket; normal shutdown flushes the current period.
- **SC-004**: A package with missing, invalid, or mismatched trust evidence cannot emit an exact feature ID.

## Assumptions

- Raw product events are retained for 90 days from server ingest. The rollup recomputation
  window is 40 UTC days: the D30 cohort window plus up to seven days of late arrival and a
  UTC-boundary safety margin. Grafana/backend provisioning is not desktop runtime state.
- The Marketplace service exposes approved manifest hashes on release responses before the desktop client release is published.
- Offline telemetry is intentionally dropped rather than persisted to a desktop queue.
