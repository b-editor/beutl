# Specification Quality Checklist: Declarative Effect Graph with GPU Pass Fusion

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-07-05
**Feature**: [spec.md](../spec.md)

## Content Quality

- [x] No implementation details (languages, frameworks, APIs)
- [x] Focused on user value and business needs
- [x] Written for non-technical stakeholders
- [x] All mandatory sections completed

> Note: like 003, this is an engine-internal API redesign, so the spec necessarily names the public types being replaced (`FilterEffectContext`, `FilterEffectActivator`, …) and the platform constraints (Skia/Vulkan dual stack, RGBA16F intermediates) — those *are* the feature's subject, per the project item. The "how" of the new design (graph representation, fusion codegen, pool policy) is deliberately deferred to `/speckit-plan`; requirements are stated as contracts and observable outcomes.

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

- The spec was written from the Project #9 item (fullDatabaseId 199376856), which already fixed the major scope decisions: breaking API changes are allowed, the six goals (fusion / pooling / centralized sync / plan caching / ROI / node primitives), the six-step rollout order, and the four initial completion criteria. No maintainer clarification was required at spec time; zero `[NEEDS CLARIFICATION]` markers.
- Informed defaults documented in **Assumptions** rather than asked: parity gate = the established 003 golden-image thresholds (not byte-identity, since fused shaders may differ in floating-point rounding); fused-pass shader technology (SKSL vs GLSL/SPIR-V) is a plan-level decision constrained by FR-014 (Skia-only fallback must exist); `CSharpScriptEffect` migrates via a documented fusion-ineligible compatibility node.
- SC-005's numeric targets (≥ 60% pass/allocation/flush reduction, ≥ 20% frame-time improvement on the benchmark chain scene) are initial targets to be confirmed against the FR-017 baseline counters once they land (rollout step 1); adjust in `/speckit-plan` if the measured baseline shows different headroom.
- Interaction with the merged 003 resolution-independent pipeline is pinned as a hard requirement (FR-012) and an edge case (fused passes must reproduce the unfused chain's resolved working scale, including the 16 384 px per-axis buffer clamp).
- **Codex design review applied (2026-07-05)** — a code-grounded "doubt the spec" pass over the full 11-document pack raised 2 BLOCKER / 6 MAJOR / 1 MINOR findings, all disposed in place. Axis B (fusion feasibility on SkiaSharp 3.119.4 — `SKShader.WithColorFilter`, `SKRuntimeEffect` child shaders) was verified with **no findings**. Fixes: **(B1)** the compiled plan now caches only structural artifacts (schedule, programs, resource-plan shape) while bounds/ROIs/buffer sizes/working scale are re-resolved every frame — parameter-driven bounds (blur `sigma × 3`, stroke pen, split counts) never recompile (spec FR-009, C3/C5, D3, data-model, T021/T029/T034/T036); **(B2)** the C# script surface break was made unambiguous per maintainer approval — legacy scripts fail at script compile time with a migration diagnostic, never silent wrong output (spec US4-AS3/FR-013, A6, breaking-changes, T046); **(M)** legacy cost model corrected to ≥ 1 materialization + ≥ 1 pass per custom item (spec Overview, research §0, T008/US5 test); pool ownership specified as a lease returned at `RenderTarget`'s last ref-count release (D4, data-model, T013); node taxonomy normalized to "seven descriptors realizing five primitives" (D7, data-model, quickstart, T049); effect census fixed at 42 incl. the previously missing `FilterEffectPresenter` (research §0/D7, plan, T004/T046/T048); `BlendEffect` reclassified as brush-based GeometryNode with a solid-color lowering optimization (D7, T026/T043); working-scale clamp carry semantics pinned to legacy `Flush`-mutation parity with a 16 384 px-clamp parity test (D6, C3.2, data-model §6, O2, T029); **(minor)** `MatrixConvolution`/`Transform` relabeled as builder conveniences, not effect classes (D7, T041).
- **Second (cold-context) Codex review applied (2026-07-05)** — a fresh session re-reviewed the post-fix pack: 4 BLOCKER / 5 MAJOR / 3 MINOR. Axis B again clean (SkiaSharp 3.119.4 APIs confirmed). Fixes: **(B1)** topology-changing values (SplitEffect division counts, compute pass counts, branch counts) pinned as *structural* — the first review's fix had over-rotated by listing split counts as size-only (C3.6/C5, D3, data-model, spec edge case, T036/T044/T050 + O2 row); **(B2)** rollout ordering repaired — the T019 bridge wraps legacy items as an `OpaqueLegacyPass` *executed via the retained internal activator machinery*, so unmigrated custom effects run before `GeometryPass`/`ComputePass` exist (D10 step 3, data-model CompiledPass, T019/T023); **(B3)** a **dynamic-outputs** pass contract added for execution-time-resolved output counts (`PartsSplitEffect` contour discovery): executor-owned pooled allocation, counted + leak-checked, exempt from the static FR-007 bound (FR-007, C3.5, data-model, D7, T039/T044/T050); **(B4)** allocation-failure semantics restated as a deliberate *normalization* of path-dependent legacy behavior (`Flush` drop-or-throw vs `CreateTarget` empty-target + unconditional `Open` throw), with per-path replacement tests (FR-015, C7, A7, D4, breaking-changes, T011/T012/T050); **(M)** premultiplied-alpha / linear-light color contract for shader nodes with semitransparent parity scenes (A2, data-model, O4, T031); `NodeGraphFilterEffect` pinned as a render-node boundary on the `CreateRenderNode` seam (FR-013, D7, T047); color-migration count corrected to 15 classes + conveniences (Phase 5 goal, T031); 41→42 census drift fixed (US4 test line); pool lease concretized (pool-aware last-release deallocator + generation tag — D4, data-model, T011/T012); **(minor)** plan.md taxonomy wording aligned; internal-only removals footnoted in breaking-changes. **Declined**: the reviewer's suggestion to strip review-history notes from this checklist — 003's precedent keeps review dispositions in checklist notes, and the pack must record *why* its requirements changed.
