# Visual Quality Regression Baseline

Briefs in `briefs/` give agents fixed motion-graphics prompts for comparing
visual-quality changes in the Agent Editing Toolkit. Briefs are intentionally
narrow: prompt, target duration, and mood. Do not add palette, typography,
layout, or motion instructions to the brief files; those choices must come
from the toolkit workflow being evaluated.

Baseline JSON files under `visual-quality-baselines/` record vision-model
axis averages produced by past runs and are a regression screen only.

Output layout and scoring procedure mirror the original benchmark harness.

## Brief Set

10 briefs cover palette harmony, typographic hierarchy, layout discipline,
motion arc, background richness, and sparseness or insufficient layer depth.
See `briefs/01-editor-title-sequence.md` … `briefs/10-minimal-not-sparse.md`.

For SC-001 / SC-004 / SC-007 / SC-011 thresholds, see
[`acceptance-matrix.md`](acceptance-matrix.md).

## Output Layout

Create a unique run id before generation. Use UTC time plus a short label,
for example `2026-07-02T120000Z-p1-visual-loop`.

Persist every run under:

```text
agent-output/benchmark/<run-id>/
```

For each brief, create a directory whose name starts with the brief number:

```text
agent-output/benchmark/<run-id>/01-editor-title-sequence/
agent-output/benchmark/<run-id>/01-editor-title-sequence/storyboard-contact-sheet.png
agent-output/benchmark/<run-id>/01-editor-title-sequence/export.mp4
agent-output/benchmark/<run-id>/01-editor-title-sequence/notes.md
```

Use the same naming pattern for all ten briefs. Keep contact sheets and videos
from different toolkit revisions in separate run ids so side-by-side comparison
is possible.

## Generation Procedure

1. Start a fresh Beutl project or fresh active editor scene for the brief.
2. Load the recommended Agent Editing Toolkit skills from `get_started`.
3. Generate the scene from the exact brief text. Do not add manual design
   hints beyond the brief's prompt, target duration, and mood.
4. Run the toolkit's normal deterministic checks and fix hard blockers
   according to the existing quality-gate policy.
5. Call `render_storyboard` with explicit shot samples that cover the opening,
   every main transition, and the final resolve. Use `returnImageContent=true`
   when the MCP client can inspect images directly. Persist the contact sheet
   as `storyboard-contact-sheet.png`.
6. Export the video as `export.mp4` when an encoder is available. If export is
   unavailable, record the reason in `notes.md` and still keep the contact sheet.
7. Record the toolkit version, git commit, branch, brief path, shot sample
   times, deterministic quality verdicts, and any accepted advisory deviations
   in `notes.md`.

## Vision Scoring Procedure

Score only the persisted storyboard contact sheet. The exported video is for
human side-by-side judgment and for spot checks when the motion arc cannot be
inferred from the contact sheet.

Give each axis an integer score from 1 to 5:

- 1: severe failure; the issue dominates the output.
- 2: weak; the issue is obvious and needs revision.
- 3: acceptable; the axis works but feels generic or uneven.
- 4: strong; the axis supports the brief with only minor issues.
- 5: excellent; the axis is polished, intentional, and brief-specific.

Axes:

- `paletteHarmony`: color relationships, contrast, saturation control, and
  absence of crude default palettes.
- `typographicHierarchy`: readable type, clear scale and weight roles,
  spacing discipline, and hierarchy that matches the message.
- `compositionWhitespace`: alignment, safe areas, focal point clarity,
  cropping, balance, and purposeful negative space.
- `layerDensityDepth`: visible background, midground, foreground, accents,
  texture, and depth without clutter.
- `backgroundRichness`: backdrop material, gradients, shaders, texture, and
  detail that avoid a flat low-effort surface.
- `motionArc`: implied timing from the strip, transition variety, easing
  feel, staging, and final resolve.

The vision model produces one JSON report per run. Every finding must include
a concrete edit directive. Avoid vague taste notes such as "make it better"
or "more premium".

## Acceptance Use

Vision scores are a regression screen only. Human side-by-side judgment of
the same brief before and after a toolkit change is final. Track axis
averages across P0, P1, P2, and P3 runs, but do not treat score changes as
proof without visual review.
