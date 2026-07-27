---
name: beutl-agent-visual-review
description: Review Beutl Agent Editing Toolkit rendered stills and storyboard contact sheets with a six-axis visual-quality rubric, then translate every finding into concrete edit directives.
---

# Beutl Agent Visual Review

Use this skill after a Beutl Agent Editing Toolkit scene has rendered stills or a storyboard contact sheet and you want an aesthetic read on it.

This is a lens, not a gate. The rubric gives you a vocabulary for saying what is actually wrong with a frame instead of "make it better" — which is the difference between a finding you can act on and a taste note you cannot. A low score is an observation about the frame, not a verdict on the piece: a deliberately spare, still, or monochrome result can score 2 on several axes and be exactly right.

## Inputs

- Rendered still PNGs from `render_still`, preferably with `returnImageContent=true` when the MCP client supports image blocks.
- A storyboard contact sheet from `render_storyboard`, preferably with `returnImageContent=true` on a synchronous call. For motion review, a subdivided storyboard (`subdivisionLevel:1`, or `2` for suspicious gaps) exposes cut continuity that anchor frames hide.
- The brief, target duration, mood, and any stated constraints.
- Existing results from `preview_quality_risks`, `evaluate_motion_variation`, `evaluate_edit_quality`, or `final_preflight` when available.
- `compare_revisions` results after a revision pass when a cached rendered quality baseline exists.

If image content blocks are unavailable, read the PNG files from the returned paths. Scoring from JSON alone measures the document, not the frame.

## Rubric

Score each axis from 1 to 5:

- 1: the issue dominates the output.
- 2: the issue is obvious.
- 3: the axis works but feels generic or uneven.
- 4: the axis supports the brief with minor issues.
- 5: the axis is polished, intentional, and specific to this piece.

Axes:

- `paletteHarmony`: color relationships, contrast, saturation control, and whether the look reads as chosen rather than defaulted.
- `typographicHierarchy`: readable type, clear role separation, scale, weight, spacing, and hierarchy matched to the message.
- `compositionWhitespace`: alignment, safe areas, focal point clarity, balance, cropping, and purposeful negative space.
- `layerDensityDepth`: visible background, midground, foreground, accents, texture, and depth relative to what the piece is going for.
- `backgroundRichness`: backdrop material, gradients, shaders, texture, and detail.
- `motionArc`: opening, development, transition energy, easing feel, staging, shot-to-shot and eye-trace continuity, camera work, and final resolve, as inferred from the contact sheet or video. Score continuity from `kind: "inbetween"` frames when a subdivided storyboard is available, not just anchor shots. When `render_storyboard` returns `cutEyeTrace`, include its displacement ratios in the evidence.

One pattern is worth naming explicitly, because it is easy to miss frame by frame: if every shot holds an identical framing across its own in-between frames — no push-in, pan, parallax, or whip-pan, only element-level animation inside a locked viewpoint — and cuts are hard swaps, the piece reads as a slide deck rather than motion graphics. That is a real finding for `motion-graphics` unless a locked-off look is what the brief wanted.

Per-type emphasis shifts the weight, not the axes: `slideshow` emphasizes transition consistency and read time; `footage-cut` emphasizes cut rhythm, visible coverage, and overlay restraint; `lyric-captions` emphasizes readability, contrast, and sync feel; `logo-intro` emphasizes motion arc, easing, final hold, and detail finish; `motion-graphics` uses the default balance.

## Finding rule

A finding is only useful if it names an edit. This is the one discipline the skill genuinely insists on, because a vague note costs a round trip and buys nothing.

Good:

- "Background is a single two-stop gradient layer. Add one midground texture layer, add a subtle grain/SKSL surface, and rotate the accent hue about +30 degrees while preserving text contrast."
- "Hero and caption use similar size and weight. Reduce caption size by about 35%, lower caption opacity, and keep the hero as the only high-contrast type role in this beat."
- "Three contact-sheet frames stay in the lower-right quadrant. Move the secondary accent path to the opposite diagonal and add one foreground sweep during the transition."
- "Shots 2-5 hold an identical framing and cut with hard swaps, so the piece reads as slides. Wrap each shot's content in a `[role:camera-rig]` DrawableGroup, add a 5% eased push-in on shots 2 and 4, and bridge the shot 3 to 4 cut with a whip-pan translate on the rig."

Not useful: "Make it more premium." / "Improve the colors." / "The motion feels bad."

## Workflow

1. Check the deterministic results first. `evaluate_edit_quality` fails only on unreadable text and malformed Element structure; those are usually accidents and worth fixing before spending a visual pass. Everything else it reports is advisory and overlaps with what you are about to score yourself.
2. Inspect the rendered images directly — image content block when present, otherwise the PNG paths.
3. Score all six axes with a one-sentence evidence note per score tied to a visible frame or contact-sheet region.
4. Write concrete edit directives for the scores you think are wrong. A 4 may carry optional polish; a 5 should not request edits. For `motionArc`, convert weak in-between frames or exceeded `cutEyeTrace` displacement into bridge-animation directives: carry an element across the cut, add a camera-rig move (an eased push-in inside the shot, or a whip-pan translate across the cut), add a sweep or wipe, preserve shared background motion, realign the focal point, or overlap transform/opacity ramps. When the cause is a locked viewpoint rather than a missing element bridge, direct a `[role:camera-rig]` `DrawableGroup` transform animation rather than more element-level accents.
5. Group directives into the smallest coherent revision pass — edits reachable through `apply_edit`, `duplicate_object`, role tags, effect recipes, or timing changes.
6. Re-render the affected stills or storyboard and rescore. Run `compare_revisions` when a cached rendered quality baseline exists; a fix that regresses another axis by more than one severity step is itself a finding, so include the introduced issue and paired still evidence next time.
7. Two revise passes is a sensible default before handing back. If a third review would still request aesthetic changes, the disagreement is probably about intent rather than execution — hand off with the advisory, the latest contact sheet path, and the remaining directives.

## Convergence loop mode

Use this when the coordinator explicitly asks for convergence, or when the run started from `beutl-agent-brief-expansion` and nobody has seen the result yet. It turns the advisory pass into a bounded improvement loop:

- **Loop**: score six axes → concrete directives → smallest coherent revision via `apply_edit` → re-render → `compare_revisions` → rescore. Continue while any axis scores below 3 and passes remain.
- **Pass budget**: `maxPasses` defaults to 3. The coordinator may raise it; raising it yourself turns a bounded loop into an unbounded one.
- **Exit**: every axis at 3 or above → `export_allowed_by_visual_review`. Budget exhausted with an axis still below 3 → `human_advisory` with the full delta ledger.
- **Anti-genericization**: phrase every directive in the piece's own concept vocabulary and name which brief field the edit serves. Adding stock particles, glow, grain, or extra layers purely to raise a score trades the piece's identity for a number — if the only fix you can articulate would change the authored concept, escalate that axis to `human_advisory` instead of applying it.
- **Anti-oscillation**: an axis already at 4 or above stays untouched by later passes except to repair a regression `compare_revisions` attributes to your own revision. Keep a per-pass delta ledger: axis scores before/after, issues resolved, issues introduced, `regression` flag.

## Output format

Return:

- `imagesReviewed`: still paths, contact-sheet path, or image-content note.
- `scores`: the six axis scores with brief visual evidence.
- `deterministicFindings`: what `evaluate_edit_quality` reported, separated into its gate-failing findings (unreadable text, malformed structure) and its advisories, or an empty list.
- `advisoryFindings`: your visual findings, each with axis, score, evidence, and a concrete edit directive.
- `revisionDelta`: `compare_revisions` resolved/introduced issue summary when available.
- `revisionPass`: the pass index.
- `convergence`: loop mode only — per-pass axis-score trajectory and whether every axis reached 3.
- `nextAction`: one of `apply_concrete_edits`, `rerender_and_review`, `human_advisory`, or `export_allowed_by_visual_review`.
