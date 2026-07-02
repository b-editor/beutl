# Visual Quality Benchmark Harness

This benchmark set gives agents fixed motion-graphics briefs for comparing visual-quality changes in the Beutl Agent Editing Toolkit. The briefs are intentionally narrow: prompt, target duration, and mood only. Do not add palette, typography, layout, or motion instructions to the brief files; those choices must come from the toolkit workflow being evaluated.

## Brief Set

- `briefs/01-editor-title-sequence.md`
- `briefs/02-logo-reveal-material-light.md`
- `briefs/03-data-infographic-shift.md`
- `briefs/04-product-promo-fast-beat.md`
- `briefs/05-lyric-video-typographic-pulse.md`
- `briefs/06-countdown-technical-event.md`
- `briefs/07-tech-explainer-opener.md`
- `briefs/08-flat-background-rescue.md`
- `briefs/09-palette-discipline-challenge.md`
- `briefs/10-minimal-not-sparse.md`

The set covers palette harmony, typographic hierarchy, layout discipline, motion arc, background richness, and sparseness or insufficient layer depth.

## Output Layout

Create a unique run id before generation. Use UTC time plus a short label, for example `2026-07-02T120000Z-p1-visual-loop`.

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

Use the same naming pattern for all ten briefs. Keep contact sheets and videos from different toolkit revisions in separate run ids so side-by-side comparison is possible.

## Generation Procedure

1. Start a fresh Beutl project or fresh active editor scene for the brief.
2. Load the recommended Agent Editing Toolkit skills from `get_started`.
3. Generate the scene from the exact brief text. Do not add manual design hints beyond the brief's prompt, target duration, and mood.
4. Run the toolkit's normal deterministic checks and fix hard blockers according to the existing quality-gate policy.
5. Call `render_storyboard` with explicit shot samples that cover the opening, every main transition, and the final resolve. Use `returnImageContent=true` when the MCP client can inspect images directly. Persist the contact sheet as `storyboard-contact-sheet.png`.
6. Export the video as `export.mp4` when an encoder is available. If export is unavailable, record the reason in `notes.md` and still keep the contact sheet.
7. Record the toolkit version, git commit, branch, brief path, shot sample times, deterministic quality verdicts, and any accepted advisory deviations in `notes.md`.

## Vision Scoring Procedure

Score only the persisted storyboard contact sheet. The exported video is for human side-by-side judgment and for spot checks when the motion arc cannot be inferred from the contact sheet.

Give each axis an integer score from 1 to 5:

- 1: severe failure; the issue dominates the output.
- 2: weak; the issue is obvious and needs revision.
- 3: acceptable; the axis works but feels generic or uneven.
- 4: strong; the axis supports the brief with only minor issues.
- 5: excellent; the axis is polished, intentional, and brief-specific.

Axes:

- `paletteHarmony`: color relationships, contrast, saturation control, and absence of crude default palettes.
- `typographicHierarchy`: readable type, clear scale and weight roles, spacing discipline, and hierarchy that matches the message.
- `compositionWhitespace`: alignment, safe areas, focal point clarity, cropping, balance, and purposeful negative space.
- `layerDensityDepth`: visible background, midground, foreground, accents, texture, and depth without clutter.
- `backgroundRichness`: backdrop material, gradients, shaders, texture, and detail that avoid a flat low-effort surface.
- `motionArc`: implied timing from the strip, transition variety, easing feel, staging, and final resolve.

The vision model must produce one JSON report per run:

```json
{
  "runId": "2026-07-02T120000Z-p1-visual-loop",
  "scoredAt": "2026-07-02T12:45:00Z",
  "briefs": [
    {
      "brief": "01-editor-title-sequence",
      "contactSheetPath": "agent-output/benchmark/2026-07-02T120000Z-p1-visual-loop/01-editor-title-sequence/storyboard-contact-sheet.png",
      "scores": {
        "paletteHarmony": 1,
        "typographicHierarchy": 1,
        "compositionWhitespace": 1,
        "layerDensityDepth": 1,
        "backgroundRichness": 1,
        "motionArc": 1
      },
      "findings": [
        {
          "axis": "paletteHarmony",
          "score": 1,
          "evidence": "Short visual observation tied to the contact sheet.",
          "editDirective": "Concrete operation that would improve the scene."
        }
      ]
    }
  ],
  "axisAverages": {
    "paletteHarmony": 1.0,
    "typographicHierarchy": 1.0,
    "compositionWhitespace": 1.0,
    "layerDensityDepth": 1.0,
    "backgroundRichness": 1.0,
    "motionArc": 1.0
  }
}
```

Every finding must include a concrete edit directive. Avoid vague taste notes such as "make it better" or "more premium".

## Acceptance Use

Vision scores are a regression screen only. Human side-by-side judgment of the same brief before and after a toolkit change is final. Track axis averages across P0, P1, P2, and P3 runs, but do not treat score changes as proof without visual review.
