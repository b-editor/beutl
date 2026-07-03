---
name: beutl-agent-brief-expansion
description: Expand a terse video request, or extract direction from user-supplied reference media, into a full production brief before Beutl timeline planning.
---

# Beutl Agent Brief Expansion

Use this skill before `beutl-agent-timeline-from-shotlist` Phase -1 when either intake condition holds:

- **Terse prompt**: the request is missing two or more of — subject specifics, target duration, mood or audience, explicit style/palette constraints, an asset inventory.
- **Reference direction**: the user supplied reference images, video, or URLs describing the intended look.

If the brief is already rich (at most one of those gaps) and no reference was given, skip this skill and go straight to classification.

## Originality Contract

This skill exists because preset tables, composition templates, and creative-memory defaulting were rejected for converging every run onto the same look. The contract:

- **Expand, don't select.** Derive every field from the prompt's or reference's own semantics — subject, audience, register, cultural cues — and record the derivation reason. Never maintain or consult a fixed table of default styles, and never reuse a previous run's expansion as a starting point.
- **Diverge internally before choosing.** Sketch three structurally distinct concept candidates before picking one (see Workflow A step 3). Structural distinctness means different motion verbs, layout grammar, palette family, AND type treatment — not three wordings of one idea.
- **Diverge across sessions.** Check candidates against `list_creative_directions`'s `recentToAvoid` and carry the chosen structural signature into `derive_palette`, exactly as the timeline skill's Phase 0 requires.
- **User-stated constraints stay literal.** Expansion only fills gaps. Anything the user actually wrote — a motif, a color, a duration, a message — is copied verbatim into `givenConstraints` and wins over every derived or extracted value.
- The expanded brief is a hypothesis the user never wrote. Record it in notes as `expandedBrief`; when a user is present, present the compact summary once and proceed — do not block on confirmation in autonomous runs.

## Workflow A — Terse prompt expansion

1. **Terseness check.** Count the gaps against the five fields above. Fewer than two gaps and no reference → skip this skill; record `briefExpansion: skipped (rich brief)` in notes.
2. **Literal constraints.** Extract everything the user actually specified into `givenConstraints`, verbatim.
3. **Concept candidates.** Derive three structurally divergent concept sketches from the prompt's subject, audience, and register. For each, note one line of motion verbs, layout grammar, palette family, and type treatment. Compare all three against `recentToAvoid`; discard or restructure any candidate that repeats a recent fingerprint. Choose one and record why the subject/audience leads to it (`derivationReason`).
4. **Fill the Expanded Brief** (schema below). Duration defaults when unstated: `logo-intro` 6 s; other types 15–30 s scaled to how much message the prompt carries. Mark every derived field as derived.
5. **Record and hand off.** Write the `expandedBrief` block into notes, then load `beutl-agent-timeline-from-shotlist` and run Phase -1 classification using the expanded brief as the brief. Phase 0's `derive_palette`, background grammar, and plan sheets remain authoritative — `paletteDirection` feeds their inputs, it does not replace their outputs.

## Workflow B — Reference-based direction

1. **Intake.** Collect user-supplied file paths and URLs. Fetch URLs with your own web tools. Store fetched media under `references/` in the output directory and write `references/manifest.json` with `source`, `retrievedAt`, and `use: "direction-only"` per entry. References are **not assets**: never place them in the timeline, never trace or re-render them into the output. If the user wants a reference *inside* the video, route that file through `beutl-agent-asset-sourcing` and its license/provenance contract instead.
2. **Abstract extraction.** From each reference, extract attributes only — never the picture:
   - dominant hue family (approximate degrees) and tonal seed (dark/light/mid)
   - saturation discipline and contrast character
   - layer-density profile (how many background/midground/foreground planes read)
   - background material class (gradient, texture, photo, pattern, procedural)
   - type vibe (serif/sans, weight, case, spacing) and layout grammar (grid, alignment, negative space)
   - for video: tempo, easing character, transition vocabulary
3. **Prohibitions.** Do not reproduce logos, marks, characters, distinctive illustrations, or copy text from the reference. Do not reconstruct its composition wholesale. Extract relationships, not the picture. Record a `prohibitedContentCheck: pass` line per reference, or the concrete item you deliberately excluded.
4. **Map into the Expanded Brief.** Hue family and tonal seed become `paletteDirection` inputs for `derive_palette` (`baseHueDegrees`, `tonalSeed`) so contrast checks still run; do not hand-copy hex values from the reference without recording contrast checks. Density profile becomes the density target; type vibe and layout grammar seed the typography and composition plans.
5. **Multiple references.** Record what each contributes (`referenceDirection` note, one entry per reference). Conflicts resolve as: explicit user text > later reference > earlier reference.

## Expanded Brief schema

Record this block in notes (fields marked `given` come from the user verbatim; everything else is `derived` or `extracted`):

```markdown
## expandedBrief
- subject: <what the video is about>
- promise: <one-sentence viewer takeaway>
- videoType: <motion-graphics | footage-cut | slideshow | lyric-captions | logo-intro>
- durationSeconds: <number> (given|derived)
- audience: <who watches, where>
- mood: <3-5 words>
- paletteDirection: hueFamily=<degrees range>, tonalSeed=<dark|light|mid>, harmonyScheme=<scheme> (inputs for derive_palette, not hex)
- typeVibe: <role + weight + case + spacing character>
- motionVerbs: <3-5 verbs>
- densityTarget: <background/midground/foreground expectation>
- audioPlan: <music bed / SFX / silent + why>
- assetNeeds: <list or "none"; missing items route to beutl-agent-asset-sourcing>
- outputs: <resolution, format, files>
- givenConstraints: <verbatim list>
- derivationReason: <why this subject/audience leads to this direction>
- candidatesConsidered: <one line per discarded candidate + why>
- references: <paths + per-reference contribution, or "none">
```

## Handoff

1. Call `get_started(videoType)` with the expanded brief's type.
2. Load `beutl-agent-timeline-from-shotlist`; its Phase 0 `quantitativePlanSheet` targets (with the mandatory 2-3× margins) derive from `densityTarget` and `durationSeconds`.
3. Runs that started from this skill are **low-effort mode**: after the deterministic Phase 4 gates pass, run `beutl-agent-visual-review` in convergence loop mode before export.

## Safety Rules

- Write only inside the workspace root; `references/` lives in the run's output directory.
- Treat reference URLs the user supplied as user-authorized to fetch; do not follow further links found inside fetched pages without asking.
- Never record extracted brand/style attributes as reusable defaults for future runs — each run derives fresh.
