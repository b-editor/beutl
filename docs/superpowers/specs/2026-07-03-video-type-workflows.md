# Video-Type-Aware Editing Workflows

Date: 2026-07-03
Status: Approved for implementation
Scope: `src/Beutl.AgentToolkit`, `.claude/skills/beutl-agent-*`, contracts doc, tests

## Problem

The Agent Editing Toolkit currently exposes a single editing workflow that is
motion-graphics-specific: `get_started` guidance, the
`beutl-agent-timeline-from-shotlist` skill, and the quality gates all assume
BPM beat grids, background grammar, and 2-3 foreground layers per shot. Other
video types (footage cut edits, slideshows, lyric/caption videos, logo intros)
either get blocked by inapplicable gates or miss the steps they actually need
(media inventory, per-photo duration grids, timestamp sync, single-shot motion
arcs).

## Decision

Profile-driven, single-skill architecture. A first-class `videoType` concept
lives on the server; the one `beutl-agent-timeline-from-shotlist` skill gains a
classification step and a per-type phase matrix. No per-type skill forks.

## Video types (v1)

String identifiers, case-insensitive. Unknown values are rejected with a
`validation_rejected` error listing the supported set.

| videoType | Content | Flow emphasis |
|---|---|---|
| `motion-graphics` | Authored vector/text/effect motion graphics | Current default flow, unchanged |
| `footage-cut` | Video-footage-driven cut edits (vlog, event, interview) | Media inventory, clip trim/order, audio, timeline coverage; no beat-density or background-grammar requirements |
| `slideshow` | Photo/still-image movies | Per-photo duration grid, Ken Burns-style minimal motion, transition consistency, caption read time; stillness/sparseness allowed by default |
| `lyric-captions` | Lyric videos and subtitle/caption-synced pieces | Timestamp-derived timing grid (not BPM), one text role system, per-line sync and read time; density gates off |
| `logo-intro` | 3-10 s single-shot logo/intro animations | Motion arc (anticipation, reveal, settle), detail density, easing quality; shot-count/tempo expectations off |

Omitted/`null` `videoType` everywhere means exactly the current behavior
(`motion-graphics` semantics) — full backward compatibility.

## Server changes (`Beutl.AgentToolkit`)

### 1. `VideoTypeCatalog`

New type (suggested location `src/Beutl.AgentToolkit/Design/VideoTypeCatalog.cs`)
holding one profile record per type:

- `Name`, `Description`, `WhenToUse`
- `BriefSignals`: short classification hints (e.g. footage-cut: "brief supplies
  or references video files / 'edit my clips'"; slideshow: "photos + music,
  'photo movie'"; lyric-captions: "lyrics/subtitles with or needing
  timestamps"; logo-intro: "'logo animation', 'intro', 'stinger', <=10 s single
  subject").
- `WorkflowSteps`: ordered guidance strings for `get_started` (see §3).
- `GateProfile`: which quality-gate adjustments apply (see §2).

`Resolve(string?)` returns the profile or throws the standard validation error
listing supported names; `null`/empty resolves to `motion-graphics`.

### 2. `videoType` on the quality tools

Add an optional `string? videoType = null` parameter to `evaluate_edit_quality`,
`preview_quality_risks`, `suggest_quality_fixes`, and `final_preflight`, threaded
into `QualityAnalyzer`. Semantics are **implied intent flags plus analyzer
applicability**; explicit flags remain honored and are OR-ed with implied ones.
The response `notes` must record the resolved type and the implied adjustments,
e.g. `Video type: slideshow (implied: allowStillness, allowMinimalDensity;
tempo/beat-grid checks skipped).`

Per type:

- `motion-graphics` / omitted: no change whatsoever (characterization tests
  must prove issue-for-issue identity on an existing fixture).
- `slideshow`: imply `allowStillness` + `allowMinimalDensity`; skip high-tempo
  /beat-grid analysis and the motion-graphics layer-density plan gate; keep the
  read-time blocker; keep cut-rhythm output but reword the advisory toward
  transition consistency. Run the `timelineCoverage` advisory (below).
- `footage-cut`: force `motionGraphicsIntent` off (suppresses layer-density,
  background-richness, and background-grammar-flavored advisories); skip
  tempo/beat-grid checks unless `styleProfile` explicitly signals high tempo;
  keep the read-time blocker for overlay text; run `timelineCoverage`.
- `lyric-captions`: imply `allowMinimalDensity`; suppress background-richness
  and hierarchy-overload advisories triggered purely by many caption-role text
  elements; keep the read-time blocker (this is the flow's core gate); keep
  motion-continuity blocking (text sync animation is expected).
- `logo-intro`: imply `allowMinimalDensity`; suppress cut-rhythm, shot-count,
  and tempo advisories (single shot is the norm); keep the motion-continuity
  blocker (a static logo hold is a failure unless `allowStillness` is passed
  explicitly).

### 3. `timelineCoverage` advisory (new, small)

For `footage-cut` and `slideshow` only: scan the scene duration with the
existing visible-element band sampling and report any range longer than 0.5 s
where zero elements are visible, as an **advisory** issue
(`category: "timelineCoverage"`) listing the gap ranges. Never blocks.

### 4. `get_started(videoType?)`

Add an optional `videoType` parameter.

- **Omitted**: keep today's response shape and content, plus (a) a new
  `videoTypes` array (name, description, whenToUse, briefSignals) and (b) a
  new first workflow item: classify the brief against `videoTypes` and call
  `get_started` again with the chosen `videoType`; the guidance below is the
  `motion-graphics` default.
- **Provided**: return `selectedVideoType` plus a workflow list assembled as
  *common core steps* (attach/create session, read_document_summary,
  measure_object_bounds, apply_edit schemaVersion, save_project, storyboard
  subdivision review, final_preflight-before-export — the type-agnostic items
  of the current list) *plus the type's `WorkflowSteps`*, replacing the
  motion-graphics-specific items. `recommendedSkills`, the terminology map, and
  the transport note stay unchanged.
- **Unknown value**: `validation_rejected` listing supported types.

Draft `WorkflowSteps` per type (Codex may refine wording; keep count 8-12,
keep every step actionable and tool-anchored):

- `footage-cut`: inventory media under the workspace before planning; use
  `get_schema` for the media source types (video/audio/image source objects)
  instead of guessing property names; build a cut list (clip, in/out, order,
  target Start/Length) in notes before `apply_edit`; trim via element
  Start/Length plus the media source's start-offset property discovered from
  the schema; keep narrative order and vary shot lengths deliberately; handle
  audio explicitly (music bed vs source audio, levels); use text overlays
  sparingly with `measure_object_bounds` + read-time checks; call quality tools
  with `videoType:"footage-cut"` and review the `timelineCoverage` advisory;
  skip `derive_palette`/`get_background_grammar` unless authored graphic
  overlays are requested (record that reason).
- `slideshow`: collect and order images with a rationale (chronology, theme);
  define a per-photo duration grid (typically 2.5-4 s) and a single transition
  vocabulary reused consistently; apply minimal Ken Burns-style motion (slow
  scale or translate, one direction per photo, alternating); captions follow
  read-time rules; optional palette derivation only for caption/backing
  colors; call quality tools with `videoType:"slideshow"`; review
  `timelineCoverage` and transition consistency on the subdivision storyboard.
- `lyric-captions`: obtain or estimate per-line timestamps first and record the
  sync table in notes (the timing grid comes from timestamps, not BPM); design
  one text role system (hero line, echo/secondary, credit) with `derive_palette`
  for contrast; one Element per line/caption with Start/Length matching the
  sync table; verify per-line read time and contrast (backing plates where
  needed); keep the background a simple consistent loop that never outcompetes
  the text; call quality tools with `videoType:"lyric-captions"`.
- `logo-intro`: single shot, 3-10 s; plan a motion arc — anticipation, reveal,
  settle/hold — before authoring; build the static end frame first, then
  animate toward it; invest in easing quality and secondary detail (particles,
  strokes, texture) rather than shot count; end on a stable hold frame >= 1 s;
  call quality tools with `videoType:"logo-intro"`; storyboard subdivision at
  level 2-3 to review the arc within the single shot.

## Skill changes

### `beutl-agent-timeline-from-shotlist` (single skill, new sections)

1. New **Phase -1 — Video type classification** before the current Phase 0:
   classify the brief into one of the five types using the signals table,
   record `videoType` + one-line reason in notes, call
   `get_started(videoType)` and follow the returned type workflow.
2. New **type flow matrix**: a compact table stating, per type, which existing
   phases apply as-is, which are replaced (e.g. footage-cut replaces Phase 0
   creative-direction/palette derivation with media inventory + cut list unless
   graphic overlays are requested; lyric-captions replaces the BPM
   `beatGridPlan` with the timestamp sync table; logo-intro collapses
   `shotBreakdownPlan` to a single-shot motion arc), and which are skipped.
   The motion-graphics column is "everything as written today".
3. Pass `videoType` to every `evaluate_edit_quality` / `preview_quality_risks`
   / `suggest_quality_fixes` / `final_preflight` call in the skill.
4. Storyboard-subdivision cut-continuity pass: note it applies to multi-shot
   types; for `logo-intro` it reviews the motion arc instead of cuts.

### `beutl-agent-visual-review`

Add a short per-type note: axis emphasis shifts (slideshow: transition
consistency + read time; footage-cut: cut rhythm + coverage; lyric-captions:
readability + sync feel; logo-intro: motion arc + detail finish). No structural
change to the six axes.

`.agents/skills/` mirrors are synced by the coordinator after implementation —
do not write them from the sandbox.

## Contracts

Update `docs/specs/001-agent-editing-toolkit/contracts/mcp-tools.md`: new
`videoType` parameter on the five tools, the `videoTypes` /
`selectedVideoType` response fields on `get_started`, and the
`timelineCoverage` advisory category.

## Tests (NUnit, `tests/Beutl.AgentToolkit.Tests`)

- `VideoTypeCatalog`: resolves all five names case-insensitively; null/empty →
  motion-graphics; unknown → validation error listing supported names.
- `get_started`: omitted → response includes `videoTypes` (5 entries) and the
  classification step while remaining backward-compatible; `videoType:
  "slideshow"` → `selectedVideoType` set and steps contain slideshow-specific
  items and no BPM/beat-grid items; unknown → `validation_rejected`.
- Quality gates: a sparse, still slideshow-style fixture passes the gate with
  `videoType:"slideshow"` without explicit intent flags, while the same scene
  under the default profile reports the stillness/density issues it does
  today; `footage-cut` suppresses layer-density/background-richness; notes
  record the implied adjustments; `timelineCoverage` reports a >0.5 s empty gap
  for footage-cut and stays absent for motion-graphics.
- Backward compatibility characterization: an existing motion-graphics fixture
  produces the identical issue set with `videoType` omitted vs today.

## Non-goals (v1)

- No audio waveform/beat detection; lyric timestamps come from the brief or
  are estimated by the agent.
- No new per-type skills, subagents, or composition templates.
- No new blocking gates; `timelineCoverage` and all reworded checks stay
  advisory. The existing four blockers keep their semantics.
