# Specification Quality Checklist: Proxy Media Workflow

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-05-20
**Feature**: [spec.md](../spec.md)

## Content Quality

- [x] No implementation details (languages, frameworks, APIs)
- [x] Focused on user value and business needs
- [x] Written for non-technical stakeholders
- [x] All mandatory sections completed

## Requirement Completeness

- [x] No [NEEDS CLARIFICATION] markers remain
- [x] Requirements are testable and unambiguous
- [x] Success criteria are measurable
- [x] Success criteria are technology-agnostic (no implementation details)
- [x] All acceptance scenarios are defined
- [x] Edge cases are identified
- [x] Scope is clearly bounded
- [x] Dependencies and assumptions identified

## Feature Readiness

- [x] All functional requirements have clear acceptance criteria
- [x] User scenarios cover primary flows
- [x] Feature meets measurable outcomes defined in Success Criteria
- [x] No implementation details leak into specification

## Notes

- Spec passes validation on first iteration. No [NEEDS CLARIFICATION] markers were inserted: ambiguous areas (auto-on-import vs. manual generation, default proxy storage location, proxy identity key, audio handling) were resolved with documented assumptions in `spec.md` under **Assumptions**, with reasoning. If any assumption is rejected by the user, revisit FR-019 / FR-020 / FR-011 / FR-016 before `/speckit-plan`.
- `/speckit-clarify` session 2026-05-20 resolved five additional ambiguities (see `## Clarifications` in `spec.md`): proxy preset shape (2–3 fixed H.264 presets), staleness fingerprint (`path + size + mtime`), stale handling (manual regenerate at MVP), concurrency (serial, 1 job), and cache cap (global LRU with configurable default).
- One spec-level assumption is structurally load-bearing: **preview path and export path are distinct in `Beutl.Engine`**. If `/speckit-plan` finds them merged in a single media-resolution layer, that is a planning-phase finding, not a spec defect — call it out and adjust the plan accordingly. *(Post-003: 003 confirmed export routes through `OutputViewModel`; the remaining risks are the `PreferProxy` render-context seam and the logical-size seam below.)*
- **Post-003 re-validation (2026-06-27)**: the spec was re-checked against the now-implemented `003-resolution-independent-pipeline`. Two new structurally load-bearing requirements were added — **FR-021** (a proxied video clip's logical footprint is preserved; the stable logical-size channel 003 deferred) and **FR-022** (a proxied video source reports supply density via `EffectiveScale.At(supplyDensity)`, not the hard-coded `At(1)`), plus **FR-023** (cache invalidation on supply-density change). Without FR-021/FR-022, opening a smaller proxy file would shrink the clip on the canvas under 003. Supporting updates landed in `spec.md` (Assumptions, FR-002/FR-017 rationale, SC-003), `data-model.md`, `contracts/IProxyResolver.md` (`ProxyResolution` now carries sizes), `tasks.md` (T062–T065 + 003-aware T004/T024/T029/T030/T031/T040/T041), `research.md` (R-11), `quickstart.md` (steps 4a and 8), and `plan.md` (Summary, Risk table, Project Structure). Still images were explicitly scoped out of MVP proxying because they bypass `DecoderRegistry.OpenMediaFile`.
- Items marked incomplete require spec updates before `/speckit-clarify` or `/speckit-plan`.
