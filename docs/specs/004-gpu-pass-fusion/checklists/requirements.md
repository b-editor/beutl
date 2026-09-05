# Specification Quality Checklist: Renderer-Wide GPU Pass Fusion

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-07-19
**Feature**: [spec.md](../spec.md)
**Validated against**: `spec.md` at `95766e7d3`, then re-run against the working tree at `6c4547def` after the SC-007 / SC-008 evidence work. Every box below reflects the re-run; the amendments in Notes record what each box originally certified and what has changed since.

## Content Quality

- [x] Implementation detail is limited to the user-mandated public API outcomes needed to make the contract testable
- [x] Focused on user value and business needs
- [x] Written for technical product, engine, and plugin stakeholders
- [x] All mandatory sections completed

## Requirement Completeness

- [x] No [NEEDS CLARIFICATION] markers remain
- [x] Requirements are testable and unambiguous
- [x] Success criteria are measurable
- [x] Success criteria are measurable and name implementation surfaces only where the requested public contract requires them
- [x] All acceptance scenarios are defined
- [x] Edge cases are identified
- [x] Scope is clearly bounded
- [x] Dependencies and assumptions identified

## Feature Readiness

- [x] All functional requirements have clear acceptance criteria
- [x] User scenarios cover primary flows
- [~] Feature meets measurable outcomes defined in Success Criteria — every criterion is stated measurably, and each is now evidenced by a committed script and a fingerprinted manifest under `evidence/`. Two clauses remain undemonstrated rather than unmeasured: SC-007's comparison against provenance-verified current-main references, and SC-008's pre-feature performance baseline. See the notes below.
- [x] Named APIs in the specification correspond to deliberate public extensibility outcomes, with internal type shapes deferred to planning

## Notes

- Validation completed in three refinement iterations; no `[NEEDS CLARIFICATION]` markers remain.
- The exact `void RenderNode.Process(RenderNodeContext)` direction and existing public API names appear because the user fixed those public extensibility outcomes. Helper overloads and internal operation, compiler, executor, cache, and resource type shapes remain planning decisions.
- Independent source inventory, donor-evidence, and public-design reviews passed after covering every existing render-node graph shape, breaking migration, recording purity, nested-request continuity, cache behavior, Shader and Geometry semantics, 3D boundaries, lifetime, and measurable outcomes.
- *Amended.* "All functional requirements have clear acceptance criteria" certified FR-043 with its committed paired visual-evidence apparatus intact. That half of FR-043 — the pinned starting-SHA baseline, the fingerprinted RGBA16F references and manifests, and the committed paired runner — was withdrawn after this gate with the evidence tree (tasks T005–T007, T016, T019, T020, T114, T115, T123); see the FR-043 note in `spec.md` and T123 in `tasks.md`. The box stays ticked: the criteria were clear when the gate ran.
- *Amended.* "Feature meets measurable outcomes defined in Success Criteria" predates the FR-043, SC-007 and SC-008 amendments in `spec.md`, so it is no longer a plain tick. Two criteria were stated measurably but not demonstrated from what this branch commits: SC-007's comparison against provenance-verified current-main references, whose fingerprinted manifests went with the evidence tree, and SC-008's performance confidence interval, which was measurable on demand via `tests/Beutl.Benchmarks/Rendering/RenderPipelineBenchmarks.cs` and is not a merge gate. Phase 2's checkpoint status in `tasks.md` records the same limitation, and FR-043 was narrowed to match rather than left as an unmet MUST. Everything else in the Success Criteria is demonstrated by the committed suites.

- *Re-run at `6c4547def`.* The box above stays `[~]`, but for a narrower reason than before. The measurement apparatus is no longer missing: `evidence/run-parity-evidence.sh` writes a fingerprinted SC-007 parity manifest, `evidence/run-paired-benchmarks.sh` plus `PairedBenchmarkAnalyzer` compute and write the SC-008 bootstrap confidence interval, both refuse a result they cannot show came from one machine, and `tests/Beutl.UnitTests/Engine/Graphics/Rendering/Evidence/` pins the rules without a GPU. What remains undemonstrated is one clause of each criterion, and both fail for the same structural reason rather than for want of tooling: a comparison against the *pre-feature* engine needs a corpus exporter and a benchmark harness compiled against the starting commit's own API, and this branch's workloads are written against its own `void RenderNode.Process` recording contract. Until such a harness exists, SC-007's parity and SC-008's ratio are evidenced against a fusion-disabled baseline of this renderer, which the manifests record as the weaker comparison it is. The other three re-checked boxes are unchanged: no `[NEEDS CLARIFICATION]` marker remains, every mandatory section is present, and FR-030 still names the `MapInputSupply<TState>` shape the amendment below already records as never shipped.
- *Amended.* "Named APIs in the specification correspond to deliberate public extensibility outcomes" certified spec text that still names `RenderScaleContract.MapInputSupply<TState>(TState state, Func<TState, EffectiveScale, EffectiveScale> map, structuralKey)`, a state-first shape that never shipped; the delivered pair is the bidirectional `MapInputSupply(map, mapOutputDemandToInput)` and the forward-only `MapInputSupplyPreservingDemand(map)`. See the FR-030 note in `spec.md`, with `contracts/public-api.md` and `contracts/breaking-changes.md` as the normative record. This box certified the 2026-07-19 spec text, not the delivered signatures.
