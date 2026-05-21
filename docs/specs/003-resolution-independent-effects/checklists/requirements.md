# Specification Quality Checklist: Per-Clip Proxy via RenderNodeOperation CorrectionScale

**Purpose**: Validate specification completeness and quality before proceeding to implementation
**Created**: 2026-05-20
**Last rewritten**: 2026-05-22
**Feature**: [spec.md](../spec.md)

## Content Quality

- [x] No implementation details leak into requirements (FRs are observable from outside the engine; the implementation details live in `data-model.md` / `contracts/` / `tasks.md`)
- [x] Focused on user value and business needs (proxy preview is the user-visible win)
- [x] Written for non-technical stakeholders (User Story 1 reads as a workflow narrative; RenderNode terminology is fenced into Key Entities / Edge Cases)
- [x] All mandatory sections completed

## Requirement Completeness

- [x] No [NEEDS CLARIFICATION] markers remain
- [x] Requirements are testable and unambiguous (every FR maps to one or more SC or task)
- [x] Success criteria are measurable (SSIM ≥ 0.97, JSON byte equality, etc.)
- [x] Success criteria are technology-agnostic (SC-001..SC-005 describe outcomes, not Skia / RenderNode mechanics)
- [x] All acceptance scenarios are defined
- [x] Edge cases are identified
- [x] Scope is clearly bounded (per-clip proxy ENGINE mechanism; UX / persistence / proxy media generation are out of scope)
- [x] Dependencies and assumptions identified

## Feature Readiness

- [x] All functional requirements have clear acceptance criteria
- [x] User scenarios cover primary flows (heavy source proxied; lightweight overlays full quality; legacy projects unchanged)
- [x] Feature meets measurable outcomes defined in Success Criteria
- [x] No implementation details leak into specification

## Notes

- **Full rewrite (2026-05-22)**: The spec was rewritten after the user clarified during design review that the actual proxy workflow is **per-clip** (heavy 4K video at 1/4 while text stays at full), **not** scene-wide. All prior helper-internal scaling work (`PixelLength` wrappers / `FilterEffectContext` scaled helpers / `GraphicsContext2D` Rect scaling / `PenHelper.GetScaled*` / `Transform.CreateMatrix` scaling / `ImmediateCanvas.PushTransform` scaling / `*Raw` opt-out family) was abandoned. The current design centres on `RenderNodeOperation.CorrectionScale` with bottom-up propagation through the render-node graph. See `spec.md` § "Design history", `research.md` § "Historical pivots", and the 8 commits between `63dd67191` and `d4728ede9` for the full pivot record.
- The current design has a much smaller change surface than prior drafts: `RenderScale` value type + 1 virtual property on `RenderNodeOperation` + per-RenderNode-subclass adjustments. Plugin / extension authors (`Drawable` / `FilterEffect` / `Shape` / `Pen` / `Transform`) do **not** change their code.
- Per-clip proxy **UX and persistence** is explicitly a follow-up feature. This PR is the engine-side mechanism only. Test exercise is via a test-only `ProxyTestHarness` that injects `CorrectionScale` values into source nodes.
- The 13 in-scope effects (Blur, DropShadow, InnerShadow, StrokeEffect, Erode, Dilate, FlatShadow, ColorShift, DisplacementMapTransform × 3, MosaicEffect, ShakeEffect, SplitEffect, Clipping) from the T001 audit are still in scope — but the mechanism that scales their parameters now lives in `FilterEffectRenderNode.Process` instead of in `FilterEffectContext` helpers.
- Geometry / TextBlock / Brush internal rects — previously listed as deferred follow-ups — are no longer deferred. They participate automatically via the `SKCanvas.Scale` matrix applied inside Type B source render passes (see `contracts/source-node-proxy.md`).
- `Pen.Thickness` — previously requiring `PenHelper.GetScaledThickness` opt-in — is now automatic. The Pen's stroke width is in canvas units; Skia scales it with the surrounding `SKCanvas` matrix. The previously-introduced `PenHelper.GetScaled*` family is **not added** in this PR.
