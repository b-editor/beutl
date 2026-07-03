# Low-Effort Brief Pipeline — Brief Expansion, Reference Direction, Quality Convergence Loop

- **Date**: 2026-07-03
- **Status**: Approved direction (user selected B/D/E; A/C/F explicitly rejected)
- **Depends on**: `2026-07-02-visual-quality-design.md`, `2026-07-03-video-type-workflows.md`, `2026-07-03-asset-sourcing.md`

## Problem

Output quality currently correlates with how carefully the incoming brief is written. The
2026-07-03 baseline run (`docs/benchmarks/visual-quality/baselines/2026-07-03-baseline-t2t8.md`)
used deliberately terse briefs (prompt + duration + mood only) and its weakest axes were
`layerDensityDepth` (2.5) and `backgroundRichness` (2.7) — exactly the qualities that
under-specified prompts fail to ask for. The goal: a one-line prompt such as
"かっこいいロゴイントロ作って" should still produce a strong video.

Three families of fixes were considered. Preset/style-pack libraries, composition
template scaffolds, and creative-memory defaulting were **rejected by the user**: past
attempts at those converged every run onto the same look and reduced originality. The
accepted directions are:

- **B — Brief expansion**: expand a terse prompt into a full production brief before planning.
- **D — Quality convergence loop**: iterate build → render → critique → revise until per-axis floors are met.
- **E — Reference-based direction**: derive the direction from user-supplied reference images/video/URLs instead of prose.

## Design principles

1. **Expand, don't select.** Every derived field must come from the prompt's (or
   reference's) own semantics with a recorded derivation reason. No fixed lookup table of
   default styles may exist anywhere in the pipeline. The existing divergence machinery
   (`list_creative_directions` / `recentToAvoid` / `record_creative_direction` /
   `derive_palette` structural signatures) is the anti-monotony backbone; expansion plugs
   into it rather than bypassing it.
2. **Judge against the piece's own brief, not a house style.** The convergence loop
   scores and revises toward the expanded brief's stated concept. A revision directive
   that would replace the authored concept with generic rubric-pleasing elements is a
   defect of the loop, not a fix.
3. **User-stated constraints stay literal.** Expansion and reference extraction only fill
   gaps; anything the user actually wrote wins over anything derived.

## B — Brief expansion (new skill `beutl-agent-brief-expansion`)

**Trigger**: the incoming request is *terse* — missing two or more of: subject specifics,
target duration, mood/audience, explicit style or palette constraints, an asset
inventory. Rich briefs skip expansion entirely.

**Mechanism** (skill-layer, agent-authored):

1. Record the user's literal constraints verbatim (`givenConstraints`).
2. Sketch **three structurally divergent concept candidates** (different motion verbs,
   layout grammar, palette family, type treatment) derived from the prompt's subject,
   audience, and register; check each against `recentToAvoid`; pick one with a recorded
   reason. Internal divergence plus cross-session fingerprints is what prevents the
   preset-convergence failure mode.
3. Emit an **Expanded Brief** block in notes: subject, one-sentence promise, `videoType`,
   duration, audience, mood vocabulary, `paletteDirection` (hue family / tonal seed /
   harmony scheme — *inputs for `derive_palette`, never literal hex*), type vibe, motion
   verbs, density target, audio plan, asset needs, outputs.
4. Hand off to `beutl-agent-timeline-from-shotlist` Phase -1 with the expanded brief as
   the brief. Phase 0's `derive_palette` / background grammar / plan sheets stay
   authoritative — expansion feeds them, it does not replace them.

**Duration defaults** when unstated: `logo-intro` 6 s, other types 15–30 s scaled to the
message; recorded as derived, not user-given.

## E — Reference-based direction (same skill, second intake path)

**Trigger**: the user supplies reference media (image/video files or URLs) describing the
intended look.

**Mechanism**:

1. Fetch user-supplied URLs with the agent's own web tools (asset-sourcing precedent);
   store under `references/` in the output directory with `references/manifest.json`
   (source, retrieval date, `use: "direction-only"`). References are **not assets**: they
   are never placed in the timeline, never traced, never re-rendered into the output. A
   reference the user wants *in* the video routes through `beutl-agent-asset-sourcing`
   and its license contract instead.
2. Extract **abstract attributes only** via vision: dominant hue family (approximate
   degrees), tonal seed, saturation discipline, contrast character, layer-density
   profile, background material class, type vibe, layout grammar, and — for video —
   tempo/easing/transition vocabulary.
3. **Prohibited**: reproducing logos, marks, characters, distinctive illustrations, or
   copy text from the reference; reconstructing its composition wholesale. Extract
   relationships, not the picture.
4. Map extraction into the same Expanded Brief fields; hue/tonal seed feed
   `derive_palette` as `baseHueDegrees`/`tonalSeed` so contrast checks still run.
5. Multiple references: record what each contributes; conflicts resolve as explicit user
   text > later reference > earlier reference.

## D — Quality convergence loop (extension of `beutl-agent-visual-review`)

Today the visual-review skill is advisory and stops after 2 revision passes. Add an
opt-in **convergence loop mode**, used when the run started from an expanded brief
(low-effort mode) or when the caller explicitly requests convergence:

- **Loop**: score six axes → concrete directives → smallest coherent revision pass →
  re-render → `compare_revisions` → rescore.
- **Exit criteria**: every axis ≥ 3 (floor), or `maxPasses` (default 3) exhausted →
  unconverged runs hand off to the human with the delta ledger, exactly as today.
- **Anti-genericization**: directives must be phrased in the piece's own concept
  vocabulary (from the expanded brief / `directionContract`). Adding stock
  particles/glow/grain purely to raise a score is forbidden; a directive that changes the
  concept escalates to `human_advisory` instead of being applied.
- **Anti-oscillation**: an axis already at ≥ 4 must not be edited by later passes except
  to repair a `compare_revisions` regression; keep a per-pass delta ledger.
- Deterministic gates (`evaluate_edit_quality`, `final_preflight`) remain the hard gate
  family; the loop only governs the advisory visual layer.

## Server-side changes

**None in v1.** Expansion and vision critique are inherently LLM tasks; palette exactness
already routes through `derive_palette`; skipping new tools avoids the DI-symmetry
burden across the two MCP hosts. The only C# change is **registration**: bundle the new
skill (`Beutl.AgentToolkit.csproj` EmbeddedResource + `BundledAgentToolkitAssets`
descriptor) and recommend it (`QueryTools.CreateRecommendedSkills`), with the existing
sync tests extended. A deterministic reference-palette-extraction tool was considered and
dropped: the goal is mood transfer, not colorimetric fidelity, and agent vision plus
`derive_palette` validation covers it.

## Acceptance criteria

1. Re-run the 10-brief benchmark (`docs/benchmarks/visual-quality/`) with the expansion
   pipeline active — the briefs are already terse by design, so they exercise B/D
   directly. Targets vs the 2026-07-03 baseline: `layerDensityDepth` ≥ 3.0 and
   `backgroundRichness` ≥ 3.0 average; no other axis regresses by more than 0.2.
2. Diversity check: run one brief twice in fresh sessions; the two runs' recorded
   creative-direction fingerprints must differ structurally (motion verbs, layout,
   palette family, type treatment).
3. Reference check: one run driven by a reference image produces a palette/density/type
   direction traceable to the recorded extraction, with no prohibited content reproduced.

## Deliverables

1. `.claude/skills/beutl-agent-brief-expansion/SKILL.md` (+ byte-identical `.agents/skills` mirror).
2. Phase -1 hook in `beutl-agent-timeline-from-shotlist` (terse/reference intake) and a
   Phase 4 pointer to convergence loop mode for low-effort runs.
3. Convergence loop mode section in `beutl-agent-visual-review`.
4. Registration: csproj EmbeddedResource, `BundledAgentToolkitAssets`,
   `QueryTools.CreateRecommendedSkills`, updated `GetStartedSkillPointerTests` /
   `AgentToolkitInstallerTests`.
