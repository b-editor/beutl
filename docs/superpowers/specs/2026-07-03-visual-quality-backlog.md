# Visual-Quality Improvement Backlog (Theory-Grounded)

Date: 2026-07-03
Status: Backlog — not yet scheduled; each task is implemented only on explicit request
Scope: `src/Beutl.AgentToolkit`, skills, benchmark harness
Prereqs: builds on the delivered 4-layer system (parametric design, visual
feedback loop, heuristics, video-type profiles) — see
`2026-07-02-visual-quality-design.md` and `2026-07-03-video-type-workflows.md`.

Each task names the film/motion-design theory it operationalizes. The point is
not academic decoration: the theory defines *what "good" means*, so the
implementation has a measurable target instead of taste.

Priority order reflects the recommendation: **T1 (measure) → T2 (music sync) →
T3 (rendered contrast)**, then the rest by leverage.

---

## T1 — Benchmark baseline run

**Priority**: P0 (do first — everything else is unmeasured without it)

**Theory**: *Dailies / screening-room practice*. Every professional pipeline
reviews yesterday's output against the brief before shooting more; iteration
without a fixed reference drifts. Also basic experimental method: no baseline,
no effect size.

**Task**: Run the 10 briefs in `docs/benchmarks/visual-quality/` end-to-end
with the current toolkit (agent authors → storyboard + stills + export), score
each with the 6-axis rubric via a vision model AND human eyes, and record the
per-axis baseline in the benchmark README. Define the re-run procedure so any
future change (T2+) reports a per-axis delta against this baseline.

**Acceptance**: a committed baseline table (10 briefs × 6 axes × {vision,
human}) plus a documented, repeatable scoring procedure.

**Effort**: mostly orchestration; no production code.

---

## T2 — Audio-driven timing grid (`analyze_audio_rhythm`)

**Priority**: P1

**Theory**:
- *Eisenstein's metric/rhythmic montage*: cut rhythm derives from the measure
  of the accompanying material, not from an abstract tempo label.
- *Michel Chion's synchresis* (Audio-Vision, 1994): an audio onset and a visual
  event that co-occur fuse perceptually into one event; a near-miss (~>40 ms)
  reads as sloppy rather than intentional.
- Music-information-retrieval basics: onset detection via spectral-flux /
  energy novelty is sufficient for beat anchoring; full beat-tracking is not
  required for v1.

**Task**: New MCP tool `analyze_audio_rhythm(path)` returning estimated BPM,
beat times, and strong-onset times (downbeats/accents) from a music-bed file.
Implementation: decode via the existing FFmpegIpc path, compute an energy /
spectral-flux novelty curve, pick peaks. The
`beutl-agent-timeline-from-shotlist` beat-grid plan then anchors Element
boundaries and accent keyframes to *measured* beats instead of nominal BPM;
lyric-captions uses onsets to sanity-check its sync table. Quality side: an
advisory that reports cuts landing 40-120 ms off the nearest strong onset
("late cut" per synchresis; suppressed when no music bed exists).

**Acceptance**: unit tests on synthetic audio (click track at known BPM →
detected beats within ±20 ms); skill updated; advisory covered by tests.

**Effort**: medium (DSP is small; IPC decode plumbing exists).

---

## T3 — Rendered (measured) text contrast

**Priority**: P1

**Theory**: *Legibility research operationalized as WCAG 2.x contrast* (4.5:1
body, 3:1 large text) — but contrast is a property of the **rendered result**,
not the palette: broadcast title practice measures the title against the
actual moving background, which is why title-safe workflows use preview
scopes. The current guarantee is design-time only (palette roles), so gradients
, motion, and effects can silently destroy it mid-shot.

**Task**: After `render_still`, sample the pixels inside each text object's
measured bounds (background composite without the text vs. text color; or
luminance histogram behind the glyph area) and compute the effective contrast
ratio. Report per-text-per-sample contrast in `visibilityAnalysis`; a Major
(gate-eligible under the existing `typographyReadTime` family) only when a
text falls below 3:1 during its visible range with no `[role:decorative]`
opt-out. Wire into `evaluate_edit_quality` sampled frames and
`final_preflight`.

**Acceptance**: tests with a fixture scene (white text over animated
white-to-black gradient → flagged at the white end, clean at the black end);
no new blocking category beyond the agreed one.

**Effort**: medium (renderer access exists via StillRenderer).

---

## T4 — Easing & motion-monotony analysis

**Priority**: P2

**Theory**: *Disney's 12 principles of animation* (Thomas & Johnston, The
Illusion of Life) — specifically **slow-in/slow-out** (linear moves read as
mechanical), **anticipation**, **follow-through/overlapping action** (things
don't start and stop together), and **secondary action**. Motion-design craft
translates these to: vary easing families, stagger starts, avoid uniform
velocity fields.

**Task**: Document-only analyzer additions (cheap — no rendering):
(a) easing-diversity metric — share of keyframes using linear/default easing;
advisory when a scene animates ≥ N properties all-linear;
(b) uniform-motion metric — advisory when most animated elements share the
same direction+duration+start (no stagger/overlap);
(c) logo-intro only: check the arc has a detectable anticipation (small
counter-move or hold before the main reveal) and settle (overshoot/ease-out
into the hold), reusing the motion-arc language already in the `logo-intro`
profile.

**Acceptance**: unit tests per metric; all advisory; wired into the skill's
Phase 3 guidance ("stagger starts; vary easing families per the 12
principles").

**Effort**: small-medium.

---

## T5 — Eye-trace continuity across cuts

**Priority**: P2

**Theory**: *Walter Murch's Rule of Six* (In the Blink of an Eye) — his cut
criteria weight emotion 51%, story 23%, rhythm 10%, **eye-trace 7%**, 2D plane
5%, 3D space 4%. Eye-trace: the audience's point of attention just before a
cut should land near the new shot's point of interest, or the viewer spends
the first frames of every shot searching. This is the *measurable* slice of
Murch we can automate; it directly extends the storyboard-subdivision
continuity pass that already exists.

**Task**: For each adjacent shot pair in `render_storyboard`, estimate the
focal point of the outgoing last frame and incoming first frame (reuse
`visibilityAnalysis` quadrant/foreground data, or a cheap saliency proxy:
largest high-contrast foreground cluster + text bounds). Report the normalized
displacement per cut; advisory when displacement exceeds ~1/3 frame diagonal
with no bridging motion. Feed the finding into the
`beutl-agent-visual-review` `motionArc` axis and the cut-continuity rework
rule.

**Acceptance**: tests on fixture storyboards (aligned vs. opposite-corner
focal points); advisory only.

**Effort**: medium.

---

## T6 — Transition vocabulary recipes + consistency classification

**Priority**: P2

**Theory**: *Continuity-editing grammar*: transitions carry meaning (a
dissolve signals elapsed time; a wipe signals location change; a match cut
signals conceptual rhyme — cf. Bordwell & Thompson, Film Art). A piece that
mixes transition types at random reads as unedited; classical editing picks a
small vocabulary and reuses it, which is exactly what the slideshow profile
already demands verbally.

**Task**: (a) Ship 4-6 parametric transition recipes via `list_effect_recipes`
(overlap dissolve with transform continuation, directional wipe/sweep, mask
reveal, dip-to-color, match-move cut); each recipe documents its semantic
("time passage", "location change", ...). (b) Analyzer classifies each cut
boundary by detected transition type (opacity ramp overlap → dissolve;
shared moving element → sweep; none → hard cut) and reports the vocabulary
histogram; advisory when >2 distinct types are mixed without a recorded
reason (slideshow/footage-cut), reusing the existing reworded cut-rhythm
plumbing.

**Acceptance**: recipe render tests + classifier unit tests; advisory only.

**Effort**: medium.

---

## T7 — Palette role-balance check (60-30-10)

**Priority**: P3

**Theory**: *The 60-30-10 rule* from interior/graphic design practice
(dominant/secondary/accent area ratio), consistent with Itten's contrast-of-
extension (color areas must be balanced by weight, not hue alone — The Art of
Color). `derive_palette` already emits roles (`bg-base`, `bg-accent`,
`foreground`, `text-primary`, `accent`); nothing checks whether the *rendered
area* each role occupies matches its role.

**Task**: On sampled stills, bucket pixels to nearest palette role color and
report the area distribution; advisory when the accent exceeds ~20% of frame
area or the dominant falls below ~40% (tunable per video type; skipped for
footage-cut where the palette does not own the frame).

**Acceptance**: fixture tests (accent-flooded frame flagged; balanced frame
clean); advisory only.

**Effort**: small-medium.

---

## T8 — Revision diff review (before/after ledger)

**Priority**: P3

**Theory**: *Regression discipline from editorial QC*: every change is
reviewed against the previous cut on the same monitor ("compare to the last
cut" screening practice); and *multi-objective optimization*: fixing one axis
routinely regresses another, which is invisible without paired comparison.

**Task**: A `compare_revisions` flow: cache the previous quality report +
sampled stills per session; after a revision `apply_edit`, re-render the same
`timeSeconds` and emit per-axis metric deltas plus paired thumbnails
(`returnImageContent`). The visual-review skill gains a rework rule: a fix
that regresses another axis by more than one severity step is itself a rework
finding. Record the ledger in notes.

**Acceptance**: session-level test (edit → compare returns deltas on identical
sample times); advisory only.

**Effort**: medium.

---

## T9 — Quality-outcome feedback into creative memory

**Priority**: P3

**Theory**: *Deliberate practice / critique loops* (Ericsson): improvement
requires that evaluated outcomes feed back into the next attempt, not just
that attempts differ (the current anti-repeat only enforces difference).

**Task**: Extend `CreativeMemoryStore` fingerprints with quality outcomes:
after `final_preflight`/visual review, record per-axis scores against the
direction fingerprint. `list_creative_directions` then reports not only
`recentToAvoid` (repetition) but `knownWeakPatterns` (fingerprints whose
outcomes scored low), with the failing axis named so the agent avoids the
pattern *for that reason*.

**Acceptance**: store round-trip tests with isolated globalRoot (per the
existing test-isolation rule); guidance strings covered by tests.

**Effort**: small-medium.

---

## T10 — Export QC (decode-back verification + loudness)

**Priority**: P3

**Theory**: *Broadcast delivery QC standards*: EBU R128 / ITU-R BS.1770
loudness normalization (streaming targets ≈ -14 LUFS integrated, broadcast
-23 LUFS) and picture QC (decode the delivered file, not the preview — color
pipeline and encoder can diverge from the compositor).

**Task**: Post-`export_video` verification step: decode N frames from the
exported file via FFmpegIpc and compare against fresh `render_still` frames
(mean ΔE / PSNR threshold → advisory on mismatch); compute integrated
loudness of the audio track and report vs. a target profile (streaming /
broadcast). Surface both in the export job result.

**Acceptance**: round-trip test on a small export fixture; loudness test on a
generated tone; advisory only.

**Effort**: medium (decode path exists via IPC).

---

## Explicit non-goals

- No new blocking gates anywhere in this backlog: T3 is the only gate-eligible
  addition and it folds into the existing read-time/typography blocker family.
- No ML-trained aesthetic scorers in-process; vision-model scoring stays in
  the benchmark harness (T1) and the advisory review loop.
- No beat-tracking research project: T2 is peak-picking on a novelty curve,
  good enough for anchoring, replaceable later.

## Suggested sequencing

1. **T1** — freeze the baseline (no code).
2. **T2 + T3** — the two changes most likely to move the baseline; re-run T1
   after each to capture the delta.
3. **T4 + T5** — motion craft (12 principles, eye-trace); re-measure.
4. **T6-T10** — by observed weakness in the benchmark, not by list order.
