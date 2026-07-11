---
name: beutl-agent-visual-review
description: Review Beutl Agent Editing Toolkit rendered stills and storyboard contact sheets with a six-axis visual-quality rubric, then translate every finding into concrete edit directives.
---

# Beutl Agent Visual Review

Use this skill after a Beutl Agent Editing Toolkit scene has rendered stills or a storyboard contact sheet and the coordinator wants aesthetic review. This is a visual feedback layer, not a replacement for deterministic toolkit gates.

## Inputs

- Rendered still PNGs from `render_still`, preferably with `returnImageContent=true` when the MCP client supports image blocks.
- A storyboard contact sheet from `render_storyboard`, preferably with `returnImageContent=true` on a synchronous call. For motion-phase review, prefer a subdivided storyboard (`subdivisionLevel:1`, or `2` for suspicious gaps) so in-between frames expose cut continuity.
- The user brief, target duration, mood, and any accepted creative constraints.
- Existing deterministic results from `preview_quality_risks`, `evaluate_motion_variation`, `evaluate_edit_quality`, or `final_preflight` when available.
- `compare_revisions` results after revision passes when a cached rendered quality baseline exists.

If image content blocks are unavailable, read the PNG files from the returned paths. Do not score from JSON alone.

## Rubric

Score each axis from 1 to 5:

- 1: severe failure; the issue dominates the output.
- 2: weak; the issue is obvious and needs revision.
- 3: acceptable; the axis works but feels generic or uneven.
- 4: strong; the axis supports the brief with only minor issues.
- 5: excellent; the axis is polished, intentional, and brief-specific.

Axes:

- `paletteHarmony`: color relationships, contrast, saturation control, and whether the look avoids crude default palettes.
- `typographicHierarchy`: readable type, clear role separation, scale, weight, spacing, and hierarchy matched to the message.
- `compositionWhitespace`: alignment, safe areas, focal point clarity, balance, cropping, and purposeful negative space.
- `layerDensityDepth`: visible background, midground, foreground, accents, texture, and depth without clutter.
- `backgroundRichness`: backdrop material, gradients, shaders, texture, and detail that avoid a flat low-effort surface.
- `motionArc`: opening, development, transition energy, easing feel, staging, shot-to-shot continuity, eye-trace continuity, camera work, and final resolve as inferred from the contact sheet or video. When subdivided storyboard frames are available, score shot-to-shot continuity from `kind: "inbetween"` frames, not just anchor shots. When `render_storyboard` returns `cutEyeTrace`, include its displacement ratios in the motionArc evidence and treat exceeded eye-trace budgets as continuity findings unless a planned bridge or deliberate hard-cut reason is recorded. Explicitly check for the slide-deck failure: if every shot's framing is identical across its own in-between frames (no push-in, pan, parallax, or whip-pan — only element-level animation inside a locked viewpoint) and cuts are hard swaps, cap `motionArc` at 2 for `motion-graphics` unless a locked-off look is a recorded brief constraint.

Per-type emphasis shifts the weight, not the axes: `slideshow` emphasizes transition consistency and read time; `footage-cut` emphasizes cut rhythm, visible coverage, and overlay restraint; `lyric-captions` emphasizes readability, contrast, and sync feel; `logo-intro` emphasizes motion arc, easing, final hold, and detail finish; `motion-graphics` uses the default balance across all six axes.

## Finding Rule

Every finding must become a concrete edit directive. Do not write vague taste notes.

Good:

- "Background is a single two-stop gradient layer. Add one midground texture layer, add a subtle grain/SKSL surface, and rotate the accent hue about +30 degrees while preserving text contrast."
- "Hero and caption use similar size and weight. Reduce caption size by about 35%, lower caption opacity, and keep the hero as the only high-contrast type role in this beat."
- "Three contact-sheet frames stay in the lower-right quadrant. Move the secondary accent path to the opposite diagonal and add one foreground sweep during the transition."
- "Shots 2-5 hold an identical framing and cut with hard swaps, so the piece reads as slides. Wrap each shot's content in a `[role:camera-rig]` DrawableGroup, add a 5% eased push-in on shots 2 and 4, and bridge the shot 3 to 4 cut with a whip-pan translate on the rig."

Bad:

- "Make it more premium."
- "Improve the colors."
- "The motion feels bad."

## Workflow

1. Confirm deterministic hard blockers first. `typographyReadTime`, `elementStructure`, `motionContinuity`, supplied-plan `layerDensity` violations, still visibility warnings, and missing explicit animation for motion graphics remain the only hard-blocking gate family unless the coordinator has documented an intentional exception. A `layerDensity` issue is hard-blocking only when authored motion-graphics foreground density is below half of the supplied `quantitativePlanSheet` target; minimal/negative-space briefs must use `allowMinimalDensity` or an equivalent role tag to keep it advisory.
2. Inspect the rendered images directly. Use the image content block when present; otherwise read the PNG paths.
3. Score all six axes. Include a one-sentence evidence note per score tied to a visible frame or contact-sheet region.
4. Produce concrete edit directives for scores 1-3. A score of 4 may include optional polish. A score of 5 should not request edits. For `motionArc`, convert weak in-between frames or exceeded `cutEyeTrace` displacement into bridge-animation directives: carry an element across the cut, add a camera-rig move (an eased push-in inside the shot, or a whip-pan translate across the cut), add a sweep or wipe, preserve shared background motion, realign the focal point, or overlap transform/opacity ramps. If in-betweens look identical to anchors except for a hard swap, call out the slideshow cut and name the adjacent shot pair; when the cause is a locked viewpoint rather than a missing element bridge, direct a `[role:camera-rig]` `DrawableGroup` transform animation rather than more element-level accents.
5. Group directives into the smallest coherent revision pass. Prefer edits that can be made through `apply_edit`, `duplicate_object`, role tags, effect recipes, or timing changes.
6. Re-render the affected stills or storyboard after a revision and repeat the rubric. After a revision pass, run `compare_revisions` when a cached rendered quality baseline exists; a fix that regresses another axis by more than one severity step is itself a rework finding, so include the introduced issue and paired still evidence in the next directive set.
7. Stop after at most 2 revise passes in advisory mode. If the third review would still request aesthetic changes, hand off to the human with the advisory, the latest contact sheet path, and the remaining concrete directives. Convergence loop mode (below) overrides this budget.

## Convergence Loop Mode

Use this mode when the run started from `beutl-agent-brief-expansion` (low-effort mode) or when the coordinator explicitly requests convergence. It turns the advisory pass into a bounded improvement loop:

- **Loop**: score all six axes → concrete directives → smallest coherent revision pass via `apply_edit` → re-render the affected stills/storyboard → `compare_revisions` → rescore. Continue while any axis scores below 3 and passes remain.
- **Pass budget**: `maxPasses` defaults to 3 revision passes. The coordinator may raise it explicitly; never raise it yourself.
- **Exit**: every axis ≥ 3 → `export_allowed_by_visual_review`. Budget exhausted with an axis still below 3 → `human_advisory` with the full delta ledger; never export silently from an unconverged loop.
- **Anti-genericization**: every directive must be phrased in the piece's own concept vocabulary from the expanded brief / `directionContract` — name which brief field the edit serves. Adding stock particles, glow, grain, or extra layers purely to raise a score is forbidden; if the only fix you can articulate would change the authored concept, escalate that axis to `human_advisory` instead of applying it.
- **Anti-oscillation**: an axis already at ≥ 4 must not be edited by later passes except to repair a regression that `compare_revisions` attributes to your own revision. Keep a per-pass delta ledger: axis scores before/after, issues resolved, issues introduced, `regression` flag.
- Deterministic gates remain the hard gate family. The loop governs only the advisory visual layer; a deterministic blocker discovered mid-loop is fixed under the existing quality-gate policy before the loop continues.

## Blocking Policy

Visual review is advisory unless the calling agent or user makes it a policy gate. Do not turn low visual scores into server-side blockers. Deterministic gate failures are still handled by the existing quality-review policy.

## Output Format

Return:

- `imagesReviewed`: still paths, contact-sheet path, or image-content note.
- `scores`: the six axis scores with brief visual evidence.
- `hardBlockers`: deterministic blockers only, or an empty list.
- `advisoryFindings`: visual findings, each with axis, score, evidence, and concrete edit directive.
- `revisionDelta`: `compare_revisions` resolved/introduced issue summary when available.
- `revisionPass`: 0, 1, or 2 in advisory mode; 0..`maxPasses` in convergence loop mode.
- `convergence`: loop mode only — per-pass axis-score trajectory and whether the exit criterion (all axes ≥ 3) was met.
- `nextAction`: one of `apply_concrete_edits`, `rerender_and_review`, `human_advisory`, or `export_allowed_by_visual_review`.

Use `human_advisory` after the second revision pass in advisory mode, or after the loop budget is exhausted in convergence loop mode, if visual problems remain.
