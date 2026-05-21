# Specification Quality Checklist: Resolution-Independent Pixel-Absolute Effects

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

- Items marked incomplete require spec updates before `/speckit-clarify` or `/speckit-plan`.
- `/speckit-clarify` session on 2026-05-20 pinned: unit semantic (pixels-at-export), nested-frame rule (innermost scene), plugin contract, tolerance metric (SSIM ≥ 0.97 after bicubic upscale), and proxy-uniformity rule (uniform scale enforced). See `spec.md` → Clarifications.
- **Design pivot (post-T001 audit, 2026-05-20)**: Wrapper-type approach replaced by helper-internal scaling inside `FilterEffectContext` + `*Raw` opt-out twins. No new wrapper types. See `research.md` § R2.
- **Scope expansion (2026-05-21)**: Scope broadened from FilterEffects-only to the **enumerated raster-space rendering surfaces** — `GraphicsContext2D` direct Rect helpers, `Pen` consumption via `PenHelper.GetScaled*`, and Transform via render-node application (`ImmediateCanvas.PushTransform` + `TransformRenderNode.IsRaw`), with Shape benefiting automatically. See `research.md` § R8 / R9 / R10 (R10 revised after Codex design review) and the four contract docs under `contracts/`. **Deferred to separate follow-up features**: `Geometry` path coordinates, `TextBlock.Size / Spacing`, `Brush` pixel rectangles.
- **Post-review revision (2026-05-21)**: Codex design review surfaced (i) an unclosed `CompositionContext.RenderScale` propagation chain, (ii) divergence risk between Pen render-time scaling and project-space bounds, (iii) plugin-migration contract inconsistencies, (iv) over-strong "byte-identical" framing, (v) Block C / Block E ordering, (vi) summary that overpromised end-to-end parity. All six addressed: Transform scaling moved to `ImmediateCanvas` (no CompositionContext change); `PenHelper.GetScaledBounds` + render-space vs project-space classification added; plugin contract consolidated into a single 4+ row authoritative table in `contracts/effect-helper-scaling.md`; plan summary clarified to "visually equivalent (SSIM ≥ 0.97)" and "enumerated raster-space surfaces"; tasks.md flags the `T011a / T011b → T015` cross-block dependency.
- The exact in-scope surfaces are enumerated in `data-model.md` § "In-scope built-in effects" + § "In-scope non-effect surfaces".
