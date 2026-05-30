# Specification Quality Checklist: Resolution-Independent Rendering Pipeline

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-05-30
**Feature**: [spec.md](../spec.md)

## Content Quality

- [x] No implementation details (languages, frameworks, APIs)
- [x] Focused on user value and business needs
- [x] Written for non-technical stakeholders
- [x] All mandatory sections completed

> Note: this is an engine-internal architectural feature, so requirements unavoidably reference rendering concepts (render scale, compositing, effects). They are kept at the *behavior/contract* level; concrete types, file paths, and code-level "how" are confined to `notes/rendering-analysis.md`, which is supporting research, not the spec itself.

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

- The four scope/behavior decisions that materially shape this spec were confirmed with the maintainer before writing:
  1. **Scope** = render-scale plumbing only (decoder-level proxy deferred; `MediaOptions` kept extensible).
  2. **Resolution-sensitive effects** = parameter scaling only ("プロパティにスケールを乗算する"); reduced-scale preview is best-effort, full-fidelity at export.
  3. **Mixed-scale** = composite at `max` child scale capped at output scale (Mitchell resample).
  4. **Logical unit** = `1 unit = 1 px at FrameSize`; no file migration; scale 1.0 byte-identical.
- Remaining decisions are plan-level and tracked under **Open Questions for Planning** in the spec; they do not block `/speckit-plan` but should be resolved there (a subset may warrant `/speckit-clarify`).
- The acceptance tolerance numbers (SC-004) are intentionally left as "a fixed threshold" pending the plan-phase decision on exact SSIM/PSNR values and the byte-identical gate.
- **`/speckit-clarify` session 2026-05-30 applied.** Four spec-level clarifications were resolved and folded in (see the spec's `## Clarifications` section): **export supersampling AA is in scope** (new FR-034, SC-009; supersampling moved out of Open Questions), the **preview render scale is a fixed enum** Full/Half/Quarter/Fit-to-previewer held as **per-edit-view, non-persisted** session state (new FR-035), and the **acceptance metric** is an exact byte-equality gate at scale 1.0 plus an SSIM threshold for reduced-scale "exact" effects (SC-004 committed to SSIM). Open Questions trimmed to 6 plan-level items.
- **Supply-driven scale-model refinement (2026-05-30, post-plan, maintainer-driven).** The original top-down D1 (every effect runs at the requested scale) was replaced with a **supply-driven** model after the maintainer required: (R1) a low-res proxy input is NOT upsampled by an intermediate effect, and (R2) a higher-than-output input (e.g. 4K on a 1080 timeline) flows into intermediate effects at its full density. Three scales now exist — **output scale `s_out`** (final target only), per-op **`EffectiveScale`** (supply density; vector = `Unbounded`, replacing the dropped `LosslessReRasterizable` bool), and computed **working scale `w`** via `ResolveWorkingScale` + a per-effect **`ResolutionPolicy`** (FR-036) under a **global ceiling** (FR-037). Maintainer choices: default `Inherit` for all effects (opt-out via `ClampToOutput`) + global memory ceiling ON. Validated by a design + adversarial-verification workflow. Edits applied across spec (FR-008/009/013/016/017/018/019 + new FR-036/037, Key Entities), research (D1 rewrite + new D7 + D6 cache), data-model (new `EffectiveScale`/`ResolutionPolicy` value types; `OutputScale`/`WorkingScale` renames), and contracts. A **fresh Codex review** then verified the supply-driven model against code and found only top-down leftovers + one byte-identity hazard; all six were fixed (spec Assumptions cap deferral; shader uniforms `s`→`w`; quickstart/plan Slice-2 `×s`→`×w`; `ResolutionPolicy.Inherit` vector-only fallback; FR-007 filter-sink rounding preserved at `w=1.0`; `MaxWorkingScale` preview-only / export-uncapped; `PreserveSource` floor carrier; cache-creation path; D1–D7 label).
- **`/speckit-plan` Codex review (2026-05-30) — two spec contradictions corrected.** An independent code-grounded review of the plan caught: (1) **FR-007** said origins "floor" but the code (`PixelPoint.FromPoint` `(int)` cast) truncates **toward zero** — they differ for negative origins and byte-identity depends on it; corrected FR-007 + the two edge-case lines. (2) **FR-011** required render scale in the stroke cache key, contradicting decision D3 (logical-space outlines are scale-invariant); reworded FR-011 to be outcome-based (cache must stay correct, no stale-scale reuse) and note the chosen logical-space approach. The `GraphicsContext2D` consumer inventory in `contracts/public-api.md` was completed. (The *first* Codex attempt confabulated nonexistent files and was discarded; the re-run with forced file reads produced these valid findings.)
- **Independent code-verification review (Codex) applied.** A second pass verified the dossier's claims against the actual code and critiqued the spec. Corrections were folded in: factual fixes (filter sinks use `(int)` cast not `PixelRect.FromRect`; `FromRect` rounds asymmetrically, not truncates; proxy-decode/IPC kept out of 003 scope), and four newly-found coupling sites became first-class requirements — **FR-029** (particles), **FR-030** (audio visualizers), **FR-031** (render-dispatcher atomicity), **FR-032** (scale invalidation key + source-generator impact), **FR-033** (3D as a mixed-scale op). FR/SC wording was tightened (raw-frame vs encoded byte-identical, root-only FR-003, capability-flag FR-018, scoped SC-008, benchmark/manifest caveats in SC-003/SC-004). The dossier carries the corrections in its **§12** addendum.
