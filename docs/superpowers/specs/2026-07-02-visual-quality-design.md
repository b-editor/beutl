# Visual Quality Improvement for AI-Driven Editing — Design

- **Date**: 2026-07-02
- **Status**: Draft (approved in brainstorming; pending written-spec review)
- **Scope**: `Beutl.AgentToolkit` / `Beutl.AgentToolkit.Mcp`, bundled agent skills (`.claude/skills/beutl-agent-*`, mirrored in `.agents/skills/`), quality agents (`.claude/agents/beutl-agent-*`), and `QualityAnalyzer`
- **Relation to Spec-Kit**: this document is the input for a new Spec-Kit feature (working name `002-visual-quality`) to be created via `/speckit-specify` when implementation starts. No code is written as part of this document.

## 1. Problem

When AI agents author motion-graphics videos through the Agent Editing Toolkit, the output is consistently amateur-looking ("dasai"). Concrete failure modes reported by the user, all in scope:

1. **Palette / look** — clashing or oversaturated colors, crude gradients and shadows.
2. **Typography** — poor font/size/spacing/placement choices, weak hierarchy.
3. **Layout / composition** — cluttered or cramped placement, ignored safe areas, no alignment discipline.
4. **Motion** — unnatural easing, over- or under-animated, dated transition choices.
5. **Backgrounds, shaders, gradients** — flat, low-effort backdrops.
6. **Sparseness** — motion-graphics scenes with far too few objects (one text on a flat background instead of a layered composition).

Primary target content: **motion graphics** (title sequences, logo reveals, infographics — generated from scratch, no footage). Improvements must land **both** server-side (MCP tools / bundled assets, benefiting any MCP client) **and** client-side (Claude skills / subagents), in balance.

## 2. Current-State Findings (2026-07-02 audit)

The existing quality machinery is entirely deterministic and intentionally conservative:

- **Gate design** (`src/Beutl.AgentToolkit/Rendering/QualityAnalyzer.cs`): only three categories can fail a gate — `typographyReadTime`, `elementStructure`, `motionContinuity`. Everything aesthetic (`paletteHarmony`, `gradientFalloff`, `materialUiLook`, `shapeDiversity`, `visualHierarchy`, `tempoRhythm`, …) is advisory and never blocks.
- **Color checking is a deny-list**, not a harmony model: dark-teal+cyan+magenta detection, oversaturation, low luma contrast. It cannot say whether a palette is *good*.
- **Agents never see pixels.** `render_still` / `render_storyboard` write PNGs to disk and return only file paths plus numeric `visibilityAnalysis` (near-black, low contrast, quadrant concentration). No MCP `ImageContent` is ever returned; `MotionVariationAnalyzer` reduces frames to change-ratio numbers. This is the single largest architectural gap: no multimodal "look at what you made" loop exists.
- **No design ingredients.** There are no curated palettes, type scales, background recipes, or easing vocabularies anywhere in the toolkit; the model invents every color and layout from scratch, which regresses to the training-data mean.
- **Density is newly planned but only counted.** Commit `d93a37ed3` added `quantitativePlanSheet` (shots, edits/sec, hold seconds, foreground elements per shot, each planned at 2–3× the gate floor) plus an export-time plan-conformance check. This measures *quantity* of density, not visual layering quality.
- **Creative memory** (`Sessions/CreativeMemoryStore.cs`) is anti-repeat only (fingerprints of palette roles / motion verbs / structure, max 12, workspace + machine-global); it stores no aesthetic judgment.
- Prior art warning from the user: **a previous design-ingredient-like system converged to the same look on every run.** Any fixed, finite choice set risks the model always picking the same "safe" option.

## 3. Approach

Decompose "ugly output" into three root causes and ship one independent layer per cause, measured by an evaluation harness built first:

| Root cause | Layer | Primary home |
|---|---|---|
| No vocabulary of good palettes/backgrounds/motion — the model invents from scratch | **L1 Parametric design system** | MCP server tools + skills |
| The agent never sees its own rendered output | **L2 Visual feedback loop** | MCP server (image return) + quality-reviewer agent |
| Sparseness / disharmony not measurable numerically | **L3 Heuristic upgrades** | `QualityAnalyzer` |

Cross-cutting: **L0 Evaluation harness** (fixed benchmark prompts + vision-model scoring + human side-by-side) built before anything else so every layer's effect is measured against a baseline.

Rejected alternatives:
- *Visual-loop-only*: the model can notice ugliness but lacks the vocabulary to fix it; per-edit token/render cost grows.
- *Template library*: highest floor but heavy authoring cost, "template smell", and poor fit with the declarative-first architecture.
- *Fixed look packs* (original L1 draft): rejected due to the user's prior experience — finite packs converge to one look. Replaced by the parametric design below.

## 4. Layer 0 — Evaluation Harness (build first)

- **Fixed benchmark set**: ~10 motion-graphics briefs checked into the repo (title sequence, logo reveal, infographic, product promo, lyric-video style, countdown, tech-explainer opener, …), chosen to exercise every failure mode in §1. Each brief is a text file: prompt + target duration + mood, nothing else.
- **Standardized run procedure** (skill or doc): generate each brief through the Agent Toolkit; persist (1) a storyboard contact sheet and (2) the exported video under `agent-output/benchmark/<run-id>/`.
- **Vision-model scoring**: score each contact sheet with the same rubric as L2 (palette, typography, composition, density/layering, background richness, motion-from-strip; 1–5 each) into a JSON report. Used as a screening/regression signal.
- **Human judgment is final**: before/after outputs for the same brief are compared side-by-side by the user. Vision scores never replace this.
- **Cadence**: rerun the full benchmark after each layer ships; track score trajectory per axis.

## 5. Layer 1 — Parametric Design System

**Principle**: do not hand the model finished styles; hand it *quality-guaranteeing rules* plus a *brief-derived seed*. Variety comes from the brief; the quality floor comes from the rules. This directly avoids the fixed-pack convergence failure.

- **Palette derivation, not palette selection.** No bundled fixed palettes. Flow: (1) the model derives a base hue and tonal range (light/dark, saturation band) from the brief's subject, mood, and keywords; (2) a new server tool `derive_palette` expands that seed into a role-tagged palette (bg-base / bg-accent / foreground / text-primary / accent) using a selectable harmony scheme (analogous, split-complementary, triadic, …) with contrast ratios guaranteed by construction. Hue is continuous, so the output space is effectively unbounded while harmony and readability always hold.
- **Background recipe grammar, not background JSON.** Provide a compositional grammar: `base layer (multi-stop gradient | shader) + 1–2 depth layers (particles | geometric accents | vignette) + motion (drift | parallax)`. Each slot lists options and parameter ranges; concrete values are derived from the brief. The minimum layering bar (three depth bands: background / midground / foreground) is embedded in the grammar itself, attacking both "flat background" and "too few objects".
- **Forced brief→direction derivation.** Strengthen the existing `directionContract`: the agent must record *why* this subject leads to this hue/tone/motion vocabulary. Unjustified choices — the ones that regress to the training-data mean — are structurally disallowed.
- **Anti-repeat integration.** Feed existing creative-memory fingerprints (palette roles, motion verbs) into `derive_palette` and the direction step; the server warns when the derived direction matches the last N works' hue band or structure, preventing cross-brief homogenization.
- **Exemplars as "do not copy" references only.** Skills carry a small set of good/bad contrast examples explicitly labeled "derive, don't copy".
- **Delivery**: server tools (advertised from `get_started`) + revisions to `beutl-agent-timeline-from-shotlist` and `beutl-agent-look-effect-chain` so the workflow becomes "derive direction → derive palette → instantiate grammar", with deviations requiring a recorded reason. Harmony/contrast logic ships with NUnit tests.

## 6. Layer 2 — Visual Feedback Loop

**Principle**: create, for the first time, a path where the agent sees its own render.

- **Server: image return.** `render_still` / `render_storyboard` gain an option to return the image as MCP `ImageContent` (any multimodal MCP client benefits). Cost controls: long edge downscaled to ~768 px; storyboards composited into a single **contact sheet** (grid with burned-in timecodes) so one image conveys the motion arc.
- **Client: visual review rubric.** Revise `beutl-agent-quality-reviewer` to add a phase where it actually looks at rendered images (Claude Code's Read handles PNGs). A new skill `beutl-agent-visual-review` defines the rubric:
  - Per-failure-mode checks scored 1–5: palette harmony, typographic hierarchy, composition/whitespace, layer density & depth, background richness, motion arc as read from the contact sheet.
  - **Every finding must translate into a concrete edit operation** ("background is a single 2-stop gradient layer — add a depth layer and rotate the accent hue +30°"), never vague taste notes.
  - **Bounded iteration: max 2 revise passes.** A third failure hands off to the human with the advisory attached.
- **Relationship to existing gates**: the three deterministic blockers stay untouched. The visual review is an aesthetic layer on top; whether it blocks export is the calling agent's policy. Server determinism is preserved.

## 7. Layer 3 — Heuristic Upgrades (`QualityAnalyzer`)

**Principle**: catch sparseness, flat backdrops, and disharmony numerically, so the floor holds even for non-multimodal MCP clients.

- **Layer density / depth metrics**: under motion-graphics intent, measure visible layer count per time band and coverage of the three depth bands (background / midground / foreground). Directly detects "too few objects"; wired into the existing `quantitativePlanSheet` plan-conformance check.
- **Color harmony scoring**: extend from the current deny-list to positive scoring based on hue-wheel relationships (analogous, split-complementary, triadic, …) and saturation/luma balance. **Shares the same harmony engine as `derive_palette`** so hand-picked colors are judged by the same rules as derived ones.
- **Background richness check**: warn when a full-frame background is single-layer, ≤2 gradient stops, and unanimated — the same floor the L1 grammar encodes.
- **Severity policy unchanged**: new metrics honor the intent-flag downgrade design (deliberate minimalism is never blocked). One blocking candidate: motion-graphics intent with authored density below **half** of the planned `quantitativePlanSheet` values — a clear plan violation, not a taste call.

## 8. Phasing

| Phase | Content | Key deliverables |
|---|---|---|
| **P0** | Evaluation harness + baseline capture | benchmark briefs, scoring rubric, baseline scores + contact sheets |
| **P1** | Visual feedback loop | `ImageContent` return option, contact-sheet compositing, `beutl-agent-visual-review` skill, quality-reviewer revision |
| **P2** | Parametric design system | `derive_palette` tool, background grammar, `directionContract` strengthening, anti-repeat integration, skill revisions |
| **P3** | Heuristic upgrades | density/depth metrics, harmony scoring, background richness check (+ NUnit tests) |

P1 precedes P2 because it is the smallest implementation with the fastest verification, and its contact-sheet compositing is reused by P0 scoring. P2 is the main quality lift; P3 is the defensive floor for non-multimodal clients. The full benchmark reruns after every phase.

## 9. Risks and Mitigations

- **Vision-model scores may be unreliable** → screening only; human side-by-side is the acceptance criterion (L0).
- **Parametric system could still converge** if the model derives similar seeds for similar briefs → anti-repeat feedback into the derivation step; benchmark briefs intentionally span diverse moods to detect convergence early.
- **Token/render cost growth from the visual loop** → downscaling, contact sheets, hard 2-iteration cap.
- **Server determinism** → all L1/L3 server logic stays deterministic (harmony math, not model calls); the only nondeterministic judgment lives client-side in the reviewer agent.
- **Constitution/architecture fit**: no GUI automation, no GPL reference, no new IPC; everything rides the existing declarative-first + render-tools surface of feature 001.
