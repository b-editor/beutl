# Run — 2026-07-03T165727Z-low-effort-bde (low-effort brief pipeline)

First full 10-brief run of the **low-effort brief pipeline** (brief expansion +
convergence loop; see "Low-Effort Brief Pipeline" in `../../spec.md`),
measured against the `2026-07-03T093833Z-baseline-t2t8` baseline. Vision scores
are a regression screen; human side-by-side judgment is final (see README).

## Run metadata

- Run id: `2026-07-03T165727Z-low-effort-bde`
- Toolkit: commit `0151a7579` on `yuto-trd/mcp` (brief-expansion skill,
  visual-review convergence loop mode, timeline-skill low-effort hooks; also
  includes the `5e5a57535` headless hierarchy-rooting fix). The id-mint
  collision fix `ed6857623` landed mid-run and is deliberately NOT in the
  benchmark binary — the system under test was frozen at run start.
- Generation: one Claude Opus 4.8 agent per brief (same model as baseline),
  headless file-backed sessions over per-brief HTTP-to-stdio bridges. Every
  agent ran the full low-effort pipeline: `beutl-agent-brief-expansion`
  Workflow A (three divergent concept candidates checked against
  `recentToAvoid`) → timeline-from-shotlist Phases -1..4 → visual-review
  **convergence loop** (exit: all six axes ≥ 3, budget 3 passes).
- Vision scoring: Claude Fable 5, final storyboard contact sheets only,
  downscaled to 1400 px (baseline used 2000 px — see caveats). Scored
  independently of the agents' own loop scores.
- Deterministic gates: all 10 briefs passed `evaluate_edit_quality` and
  `final_preflight`. All 10 runs **converged** in the visual loop: 5 at
  pass 0, 5 with a single revision pass; no run needed pass 2 or 3, and none
  exited unconverged.
- Artifacts: `agent-output/benchmark/2026-07-03T165727Z-low-effort-bde/`
  (untracked); scores JSON committed alongside this file.

## Vision scores (independent rescoring)

| Brief | Pal | Typ | Comp | Depth | BG | Arc |
|---|---|---|---|---|---|---|
| 01-editor-title-sequence | 4 | 4 | 3 | 3 | 3 | 4 |
| 02-logo-reveal-material-light | 4 | 4 | 4 | 4 | 4 | 4 |
| 03-data-infographic-shift | 4 | 4 | 4 | 3 | 3 | 4 |
| 04-product-promo-fast-beat | 4 | 4 | 3 | 3 | 3 | 4 |
| 05-lyric-video-typographic-pulse | 4 | 4 | 4 | 3 | 3 | 4 |
| 06-countdown-technical-event | 4 | 4 | 4 | 3 | 3 | 4 |
| 07-tech-explainer-opener | 4 | 3 | 3 | 3 | 3 | 4 |
| 08-flat-background-rescue | 4 | 4 | 4 | 4 | 4 | 3 |
| 09-palette-discipline-challenge | 4 | 4 | 4 | 3 | 3 | 3 |
| 10-minimal-not-sparse | 4 | 4 | 4 | 3 | 3 | 3 |
| **Average** | **4.0** | **3.9** | **3.7** | **3.2** | **3.2** | **3.7** |

## Delta vs baseline

| Axis | Baseline | This run | Delta | Target |
|---|---|---|---|---|
| paletteHarmony | 3.3 | 4.0 | **+0.7** | no regression ✓ |
| typographicHierarchy | 3.2 | 3.9 | **+0.7** | no regression ✓ |
| compositionWhitespace | 2.9 | 3.7 | **+0.8** | no regression ✓ |
| layerDensityDepth | 2.5 | 3.2 | **+0.7** | ≥ 3.0 ✓ |
| backgroundRichness | 2.7 | 3.2 | **+0.5** | ≥ 3.0 ✓ |
| motionArc | 2.9 | 3.7 | **+0.8** | no regression ✓ |

Acceptance criteria 1 (axis targets) **met**: both weak axes cleared 3.0 and
no axis regressed. The baseline outlier (brief 04 motionArc = 1) resolved to
4 — the expanded brief gave the fast-beat promo a concrete percussive concept
(press strikes + per-beat accumulation) where the baseline run had produced
near-identical frames.

## Diversity check (acceptance criterion 2)

Brief 01 was run twice in fresh sessions:

- Run 1 — **"Strata"**: dark warm near-black ground, horizontal timeline
  track-bands sliding to registration, monochrome-warm + coral accent,
  slide/snap/register/lock.
- Rerun — **"Ordered Assembly"**: light editorial lilac field with a faint
  column grid, feature words arriving from three different frame edges and
  locking into an index, crimson accent seam, wordmark "Cadence" resolve.

Palette family (dark warm vs light magenta), layout grammar (horizontal
strata vs modular columns), and motif (track-bands vs directional word-lock)
all differ structurally → **met**. Note the divergence is mechanism-assisted:
run 1's fingerprint was recorded via `record_creative_direction` into the
shared workspace creative memory, so the rerun's `recentToAvoid` actively
steered away from it. That is the designed behavior, not spontaneous
variance.

Acceptance criterion 3 (reference-based direction, pipeline E) was **not
exercised** in this run — it needs a user-supplied reference image and should
be validated separately.

## Diagnosis

- The two former weakest axes improved but remain the weakest (3.2): minimal
  and restraint-led concepts (03, 09, 10) hold Depth/BG at 3 **by recorded
  intent** — the anti-genericization rule correctly stopped agents from
  padding clean/minimal briefs with filler texture, which caps those scores
  by design. The axis floor (≥ 3) is the right target for such briefs, not 4+.
- Convergence loop behavior matched design: revisions were concept-serving
  (e.g. 01 lifting band fills, 05 warming the raking-light blend, 10
  deepening vignette+shadow) and `compare_revisions` reported zero
  regressions in every applied pass.
- motionArc weakest cases are now the hold-heavy quiet pieces (08, 09, 10 at
  3) — sampled stills under-represent slow breathing motion; video spot
  checks are advised before drawing conclusions there.
- Scene-end boundary: rendering at exactly t=duration yields a blank frame
  (harmless off-by-one; export is unaffected) — visible as a blank last cell
  in the 08/10 contact sheets. Worth a cosmetic fix in `render_storyboard`
  sampling.

## Field defects observed during the run

- **Id-mint collision (real, fixed post-run in `ed6857623`)**: deterministic
  id minting could re-mint an id that already existed in the session
  (structurally identical id-less nodes at the same patch path across calls),
  bypassing the stale-handle check and persisting duplicate ids — after which
  every `apply_edit` failed with `internal_error`. Hit independently by
  briefs 02 and 09 (and observed as transform-child duplication by 07).
  The fix adds reserved-set re-salting, desired-array duplicate rejection,
  and first-wins tolerance so corrupted documents stay repairable.
- **Refuted agent reports** (verified against the run binary, no code
  change): `UseGlobalClock=false` local keyframes evaluate correctly
  (brief 05's report was a scene-timed-keytimes authoring error);
  `$replace` on nested keyframe arrays works (brief 03); `export_video`
  `crf` is wired end-to-end and changes output bytes (brief 06).

## Validity caveats

- Same scorer model as baseline but a different session and a smaller
  downscale (1400 px vs 2000 px); treat ±0.3 axis deltas as noise.
- Agents self-score the same rubric inside the convergence loop, so scores
  could drift toward the rubric (Goodhart risk). Mitigations observed: the
  independent rescore sits consistently below the agents' self-scores
  (agents reported mostly 4s; this table keeps several 3s), and minimal
  briefs deliberately held axes at 3 rather than padding.
- Human scoring column: pending, as with the baseline.
