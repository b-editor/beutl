---
name: beutl-agent-brief-expansion
description: Expand a terse video request, or extract direction from user-supplied reference media, into a full production brief before Beutl timeline planning.
---

# Beutl Agent Brief Expansion

Use this skill before `beutl-agent-timeline-from-shotlist` Phase -1 when the request does not yet say enough to plan a timeline:

- **Terse prompt**: missing two or more of — subject specifics, target duration, mood or audience, explicit style/palette constraints, an asset inventory.
- **Reference direction**: the user supplied reference images, video, or URLs describing the intended look — or the subject is a real product, site, or brand you can go and read for yourself.

If the brief is already rich and no reference was given, go straight to classification.

## What this skill is for

Expanding a terse brief means inventing most of the piece. The failure mode is not inventing badly — it is inventing the *same thing every time*, so that ten different prompts produce ten variations of one video. Preset tables, composition templates, and creative-memory defaulting were removed from this toolkit for exactly that reason.

So the guidance here is about where your choices come from, not what they should be:

- **If the subject is a real thing, go and look at it.** A product, site, brand, or place has an actual appearance, and reading it beats imagining it by a wide margin — this is Workflow B, and it is worth entering even when the user handed over no reference at all. Only fall back to deriving from semantics alone when the subject has no real-world artefact to read.
- **Derive from the prompt's own semantics** — subject, audience, register, cultural cues — rather than from a stock of looks. There is no default-style table to consult, and a previous run's expansion is the one starting point guaranteed to converge.
- **Sketch more than one concept before choosing.** Three structurally distinct candidates is a useful number. Structurally distinct means different motion verbs, layout grammar, palette family, *and* type treatment — not three wordings of one idea. Then pick one, knowing what you rejected.
- **`list_creative_directions` reports what recent runs looked like.** Comparing candidates against `recentDirections` is how you find out you are about to repeat yourself. Carry the chosen structural signature into `derive_palette` so the repeat check has something to work with.
- **Anything the user actually wrote stays literal.** Expansion fills gaps. A motif, color, duration, or message the user stated goes into `givenConstraints` verbatim and wins over every derived value.
- **The expanded brief is a hypothesis the user never wrote.** Record it as `expandedBrief`; present the compact summary once when a user is present, and proceed rather than blocking on confirmation in autonomous runs.

## Workflow A — terse prompt

1. **Count the gaps** against the five fields above. Fewer than two and no reference: skip this skill.
2. **Extract literal constraints** into `givenConstraints`, verbatim.
3. **Sketch concept candidates** from the prompt's subject, audience, and register. For each, one line of motion verbs, layout grammar, palette family, and type treatment. Compare against `recentDirections`. Choose one and record why the subject/audience leads there.
4. **Fill the Expanded Brief** (schema below). When duration is unstated, `logo-intro` around 6 s and other types 15–30 s scaled to how much message the prompt carries are reasonable starting points. Mark derived fields as derived.
5. **Hand off.** Write `expandedBrief` into notes, then load `beutl-agent-timeline-from-shotlist` and run Phase -1 classification with it as the brief. `paletteDirection` feeds `derive_palette`'s inputs rather than replacing its outputs.

## Workflow B — reference-based direction

1. **Intake.** Collect user-supplied paths and URLs. Fetch URLs with your own web tools. Store fetched media under `references/` in the output directory with a `references/manifest.json` recording `source`, `retrievedAt`, and `use: "direction-only"` per entry. References are not assets: they do not go in the timeline and are not traced or re-rendered into the output. If the user wants a reference *inside* the video, that file goes through `beutl-agent-asset-sourcing` and its license/provenance contract instead.
2. **Extract attributes, not the picture.** This is where most of the specificity comes from: in the best-scoring run to date, roughly four fifths of what made the piece look like its subject came from reading the real artefact rather than imagining one — tracking on the eyebrow text, a two-tone heading, the exact shape of a UI chip, which token colours were warm, the presence of status dots, and the site's own copy. "A dark-mode tech video" invented from scratch does not produce any of that. Per reference: dominant hue family (approximate degrees) and tonal seed; saturation discipline and contrast character; layer-density profile; background material class (gradient, texture, photo, pattern, procedural); type vibe and layout grammar; for video, tempo, easing character, and transition vocabulary.
3. **Do not reproduce protected content.** Logos, marks, characters, distinctive illustrations, and copy text are not yours to reuse, and neither is the reference's composition wholesale. This is a rights boundary, not an aesthetic one. Record `prohibitedContentCheck: pass` per reference, or the specific item you excluded.
4. **Map into the Expanded Brief.** Hue family and tonal seed become `paletteDirection` inputs for `derive_palette` (`baseHueDegrees`, `tonalSeed`) so contrast stays checked; hand-copying hex values skips that check. Density profile becomes the density target; type vibe and layout grammar seed the typography and composition plans.
5. **Multiple references.** Record what each contributes. Conflicts resolve as: explicit user text > later reference > earlier reference.

## Expanded Brief schema

Record this in notes. Fields marked `given` come from the user verbatim; everything else is `derived` or `extracted`:

```markdown
## expandedBrief
- subject: <what the video is about>
- promise: <one-sentence viewer takeaway>
- videoType: <motion-graphics | footage-cut | slideshow | lyric-captions | logo-intro>
- durationSeconds: <number> (given|derived)
- audience: <who watches, where>
- mood: <3-5 words>
- paletteDirection: hueFamily=<degrees range>, tonalSeed=<dark|light|mid>, harmonyScheme=<scheme>
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

1. `get_started(videoType)` with the expanded brief's type.
2. Load `beutl-agent-timeline-from-shotlist`. Its Phase 0 shot breakdown and density target derive from `densityTarget` and `durationSeconds`.
3. A run that started here has nobody who has seen the result. `beutl-agent-visual-review` in convergence loop mode before export is how it gets looked at.

## Boundaries

- Write only inside the workspace root; `references/` lives in the run's output directory.
- Reference URLs the user supplied are user-authorized to fetch. Links found inside fetched pages are not — ask first.
- Extracted brand/style attributes belong to this run. Recording them as reusable defaults is how every later run starts converging on one look.
