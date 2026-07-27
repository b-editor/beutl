---
name: beutl-agent-timeline-from-shotlist
description: Build a Beutl timeline from a shot list through the Agent Editing Toolkit MCP tools.
---

# Beutl Agent Timeline From Shot List

Use this skill to turn a shot list, storyboard, or timed brief into a Beutl project through the Agent Editing Toolkit.

This is a capability guide, not a rulebook. Most of it is **mechanics** — how the engine and the toolkit actually behave. Getting mechanics wrong means the patch is rejected, or renders something you did not author, so those parts are worth following closely. The rest is **craft**: what tends to read well, and what the toolkit will measure and report. Craft notes are knowledge, not permission — the only findings that fail a gate anywhere in this toolkit are unreadable text and malformed Element structure.

## Recommended order: storyboard-first

Plan a shot breakdown, build the **static** layout of every shot with no keyframes and no effects, confirm it reads, then add effects, then motion.

The reason is economic rather than moral: a composition problem is cheap to fix in a static frame and expensive once thirty keyframes depend on it, and a storyboard that does not read clearly does not start reading once it moves. Deviate when the piece genuinely calls for it — but deviate knowing that is the cost.

### Phase -1 — Video type classification

If the request is terse (missing two or more of: subject specifics, target duration, mood or audience, explicit style/palette constraints, an asset inventory) or the user supplied reference images/video/URLs, `beutl-agent-brief-expansion` fills the gaps first; use its `expandedBrief` as the brief here. User-stated constraints stay literal; the derived `paletteDirection` feeds `derive_palette`'s inputs rather than replacing its outputs. A run that started from brief expansion has nobody who has seen the result — the visual-review convergence loop in Phase 4 is how it gets looked at.

Classify the brief, record `videoType` and a one-line reason, then call `get_started(videoType)` for the type-tailored guidance. Supported: `motion-graphics`, `footage-cut`, `slideshow`, `lyric-captions`, `logo-intro`. If the brief needs clips, photos, music, SFX, or fonts missing from the workspace, `beutl-agent-asset-sourcing` covers acquisition and its license/provenance contract before media is placed.

| videoType | Signals |
|---|---|
| `motion-graphics` | Authored graphics, kinetic type, promo/explainer/infographic motion, BPM or beat-grid language, layered background grammar, no source media inventory as the main driver. |
| `footage-cut` | User supplies video clips, says edit my clips/vlog/event/interview/B-roll, asks for trim/order/source audio/music bed, or needs narrative clip coverage. |
| `slideshow` | Photos/stills plus music, photo movie/album/memories language, per-photo duration grid, gentle Ken Burns motion, consistent transitions, caption read time. |
| `lyric-captions` | Lyrics, subtitles, captions, transcript, timestamp sync, per-line timing, readability and contrast as the core deliverable. |
| `logo-intro` | Logo animation, intro, stinger, bumper, brand mark, 3-10 seconds, single subject, anticipation/reveal/settle/hold arc. |

Pass the resolved `videoType` on `evaluate_edit_quality(staticLayout:true)`, `suggest_quality_fixes`, `evaluate_edit_quality`, and `final_preflight` so their reports are tailored; omitting it means the legacy motion-graphics behavior. For `motion-graphics` and `slideshow`, passing the derived palette role colors as `paletteRoleColors` (an array of `{ "role": string, "color": "#RRGGBB" }`, or a JSON string containing that array when the MCP client cannot send complex arrays) gets you the authored 60-30-10 area mix. Drop the alpha channel if `derive_palette` returned `#AARRGGBB`.

| videoType | Phases as written | Specialized | Usually skipped |
|---|---|---|---|
| `motion-graphics` | Everything. | None. | None. |
| `footage-cut` | Phase 1 staged `apply_edit`, `read_document_summary`, `measure_object_bounds`, Phase 4 still/quality/preflight. | Phase 0 becomes media inventory plus cut list unless authored graphic overlays are requested; missing clips route through `beutl-agent-asset-sourcing`; the shot breakdown becomes clip in/out/order/Start/Length; audio handling is explicit. | `derive_palette` and `get_background_grammar` unless graphic overlays are requested; density/beat-grid planning. |
| `slideshow` | Phase 1 static storyboard, measured captions, staged `apply_edit`, Phase 4 still/quality/preflight. | Phase 0 becomes image ordering, per-photo duration grid, and transition vocabulary; Phase 3 uses minimal Ken Burns-style scale/translate per photo. | Background grammar and dense foreground planning unless captions/backing graphics need it; BPM beat-grid checks. |
| `lyric-captions` | Phase 1 layout, measured text/backing plates, Phase 4 still/quality/preflight. | The beat grid becomes a timestamp sync table; the shot breakdown becomes one Element per line with Start/Length from that table; type roles are hero line/echo/credit. | Background richness and density planning; BPM-driven tempo targets unless requested. |
| `logo-intro` | Phase 1 static end-frame build, Phase 2 effects, Phase 3 motion, Phase 4 still/quality/preflight. | The shot breakdown collapses to one 3-10 second shot with anticipation/reveal/settle/hold; storyboard subdivision reviews the internal motion arc instead of adjacent cuts. | Multi-shot cut-continuity planning, shot-count expectations, BPM tempo planning unless requested. |

### Phase 0 — Direction and shot breakdown

**The direction is yours to author.** Decide the concept, base hue, tonal seed, harmony scheme, type system, motion vocabulary, and shot structure from the brief — or from scratch when the brief is vague — before reaching for any tool. Concrete constraints the user stated (motif, style, palette, message, audience, subject) stay literal.

Tools that help, none of them mandatory:

- **`list_creative_directions`** returns inspiration seeds plus `recentDirections` — what recent runs in this workspace actually looked like. Pass a fresh `seed` each run to vary the stimulus. The seeds work as stimulus once a direction already exists; anchoring on them at the start is how ten prompts become one video. `recentDirections` is the cheapest way to notice you are about to repeat yourself: structural language (motion verbs, layout, palette family, type treatment) is what makes two pieces differ, not wording.
- **`derive_palette`** with `baseHueDegrees`, `tonalSeed`, `harmonyScheme`, an optional `derivationReason`, and `structuralSignature` returns role colors with contrast already solved. Its repeat warnings compare against your own history — information, not a verdict. Hand-picking colors is fine and leaves the contrast checks to you.
- **`get_background_grammar`** lists background slots, options, and ranges. Background + midground + foreground reads as depth; fewer bands read as deliberately flat.
- **`record_creative_direction`** files this run's fingerprint so later runs can see it.
- **`plan_original_scaffold`** returns a one-call seed-derived skeleton (background, headline, subtitle, placeholder foreground) to rewrite for the brief — an original starting point, not a reusable template.

Recording the concept label, derivation reason, palette roles, background slots, motion verbs, and structural signature in notes is what lets a later pass — or a human — pick the work up without re-deriving it. Map the concept into your own named Elements/Objects; seed names describe the stimulus, not the result. For unconstrained briefs, neutral output basenames (`project.bep`, `preview.mp4`, `still-*.png`) keep the concept in notes rather than in filenames.

#### Media inventory

For media-driven types, inventory sources before schema authoring, then `get_schema` for any drawable, media, or audio type you do not already know.

- `footage-cut`: every available clip, duration note, usable range, audio role. `slideshow`: supplied photos or the collection needed. `lyric-captions`: the music bed or why none is needed.
- Missing clips/photos/music/SFX/fonts route through `beutl-agent-asset-sourcing`; its license/provenance contract and `assets/manifest.json` entry come before `apply_edit`.
- **Real capability types beat fakes.** `ParticleEmitter` (`get_schema type=ParticleEmitter`) covers `EmitterShape` point/line/circle, `EmissionRate`, `Lifetime`, `Speed`/`Direction`/`Spread`, `Gravity`, `TurbulenceScale`, size/color/opacity over particle life, and `ParticleDrawable` — any Drawable, including a glyph fragment or `GeometryShape`, can be the emitted sprite. A swarm faked from many ellipse Elements costs more and reads as less. For music-driven briefs, `AudioWaveformDrawable`, `AudioSpectrumDrawable`, and `AudioSpectrogramDrawable` render real waveform/spectrum motion with bar, radial, mirrored, line, filled, dots, and block styles.
- For organic heat, ink, glass, smoke, grain, or caustic fields, `list_effect_recipes` with a shader/organic intent surfaces `SKSLScriptEffect`. SKSL is CPU-safe in still renders, which makes it the better choice over GLSL for low-context file sessions.
- GPU/stylize effects (GLSL, `PixelSortEffect`, `ColorShift` on split-character text) are render-guarded to skip degenerate targets rather than crash, and run on the bundled SwiftShader software fallback when no hardware GPU is present — slower, not skipped. Confirm with `render_still` before relying on them. Collapsing to one safe effect is how a piece ends up looking like every other piece.

#### Source grounding

When the work depends on layout, transform composition, bounds, text measurement, render scale, effect units, reconciliation, or live-session semantics — and especially when a rendered result contradicts the plan — read `.claude/skills/beutl-agent-source-grounding/SKILL.md` and do narrow `rg`/read passes over what it points at. Note the assumption, evidence, derived rule, and remaining uncertainty before the first dependent `apply_edit`. Skip if the user forbade source reading.

#### What is worth deciding before you author

Not a form to fill in. These are the decisions that are expensive to change later, so making them explicitly — in whatever notation suits you — saves rework:

- **Message and hierarchy.** Primary message, secondary emphasis, supporting information per shot; which type roles (hero, secondary, caption, label, texture) carry them; one primary focal point per shot and how grouping, alignment, scale, color, and repetition support it.
- **Shot breakdown.** Index, `Start`, `Length`, focal point, message, role per shot. This is what Element boundaries come from in Phase 1, so it is the one plan the rest genuinely depends on. Derive the count from duration × tempo where a tempo exists: at 130 BPM a 30 s piece is about 65 beats, and a foreground event every 1-2 beats means tens of fine shots rather than six coarse ones. Long holds subdivided into distinct beats read as paced; one shot spanning many beats reads as a stall unless it is a named resolve or background texture.
- **Beat grid.** When a music bed file exists, `analyze_audio_rhythm` before authoring gives `estimatedBpm`, `confidence`, `beatTimesSeconds`, and `strongOnsetTimesSeconds` — anchor Element boundaries, accent keyframes, and transition hits to those measured beats. Without a bed, convert explicit BPM or tempo language into a nominal grid: at 130 BPM one beat is about 462 ms, two about 923 ms, four about 1.85 s.
- **Density target.** How many foreground layers per shot you are going for. Passing it as `plannedForegroundElementsPerShot` to `evaluate_edit_quality`/`final_preflight` lets `metrics.layerDensity` compare authored density against your intent and tell you where execution shrank the plan — which it usually does. That comparison is advisory; it never blocks.
- **Camera treatment.** Beutl's 2D pipeline has no scene camera, so viewpoint work is authored as animated rig transforms. Per shot or shot run: `locked` (deliberate static frame), `push-in`/`pull-back` (rig `ScaleTransform` around the focal point via `TransformOrigin`), `pan`/`tilt` (rig `TranslateTransform.X/Y`), `whip-pan` (fast rig translate + blur as a cut bridge), `roll` (rig `RotationTransform`), or `parallax` (per-depth-band rigs at different amplitudes, background slowest). There is no automatic inverse: camera-left means rig-right, camera-in means rig scale-up.
- **Cut continuity.** Per adjacent shot pair, the visible bridge across the cut: an element crossing it, a camera move continuing across it, a sweep, shared background continuity, an overlapping transform/opacity ramp — or a hard cut you meant.
- **Transform intent.** For every rotated moving object, whether motion is screen-space or local/rotated-space. Screen-space drift on a tilted object needs static orientation transforms before the animated `TranslateTransform`; local-axis motion is the other order. This one is genuinely ambiguous from the document alone, so deciding it up front avoids a silent wrong result.
- **Verification samples.** At least three still times plus the motion/quality sample set.
- **Structure.** One ordinary Element owns one `EngineObject`. Multiple Objects in one Element are valid only when it contains an `IFlowOperator` — `DrawableGroup`, `DrawableDecorator`, `SoundGroup`, `Scene3D`. `DrawableGroup` is for multi-child grouping (camera rigs, portal intake); `DrawableDecorator` is for applying one shared transform/effect/opacity/blend to children as a single composited unit (group fade, shared blur/shadow). The difference is subtle enough to source-ground when the composited result matters. This is the one structural rule the quality gate enforces, because a violation is a malformed document rather than an unusual one.
- **Role tags.** `[role:background]`, `[role:text-backing]`, `[role:decorative]`, `[role:camera-rig]` on important objects let the quality tools tell a real text plate from a decorative accent and a camera rig from a content group — and let a later pass read your intent. Pair the tag with the job in the name: `[role:decorative] beat sweep`, `[role:text-backing] title plate`, `[role:background] surface`.

#### Contrast relationships worth deriving

Descriptions of relationships, not palettes to copy:

- `text-primary` much lighter than a dark `bg-base` while still clearing 4.5:1 against the brighter `bg-accent`; a small saturated `accent` clearing 3.0:1 against `bg-base`.
- On a light tonal seed: `text-primary` dark and low-saturation, `foreground` the readable material color, the bright accent reserved for small motion cues.
- Cyan text on pale blue or bright yellow relies on hue difference and fails luma contrast — the frame looks colorful and reads as mush.
- Three fully saturated roles at similar lightness compete; muting support colors lets one accent carry saturation.
- Dark teal base plus cyan and magenta neon is the most-reached-for combination in this space, so it reads as a default rather than a choice.

### Phase 1 — Static storyboard

Build the static layout of every enumerated shot with no keyframes and no effects. The deliverable is a readable static storyboard: composition, hierarchy, typography, color, and layering at each shot's representative frame.

1. **Session.** Stdio/headless: `create_project` or `open_project` with a `.bep` path (extensionless paths normalize to `.bep`; `.beutl` is reserved for exported packages). Live editor: `attach_active_editor` — in the in-app host, `open_project`/`create_project` open the project in the running editor, which holds a single open project, so opening a different one is rejected. If live attach fails and the task allows headless output, switch to the stdio route rather than writing a custom generator.
2. **Notes.** When an output directory is requested, create/update `notes.md` there before the first edit and after every `apply_edit`, `save_project`, `render_storyboard`, `evaluate_edit_quality(staticLayout:true)`, `suggest_quality_fixes`, `render_still`, `evaluate_motion_variation`, `evaluate_edit_quality`, `compare_revisions`, `final_preflight`, and `export_video` result — success/failure, change count or verdict/path, next action.
3. **`read_document`** and keep the returned `schemaVersion`.
4. **Author the static layout** as a declarative document:
   - PascalCase property names exactly as `get_schema` returns them.
   - New timeline Elements need `$type: "[Beutl.ProjectSystem]:Element"`. Use stable `Id` handles for existing elements; omit `Id` only for genuinely new elements/objects so the toolkit mints one.
   - Do not add a second `Object` to an ordinary existing `Element` — create another Element with its own single `EngineObject`. Multiple Objects belong to an intentional `IFlowOperator` chain; keep the parent `Element.Id` and omit `Id` only for genuinely new child objects in that chain.
   - Keep `Start`, `Length`, and layer/Z values consistent with the shot breakdown.
   - Static property values only — no `KeyFrameAnimation`/`KeyFrame` (Phase 3), no `FilterEffect` (Phase 2).
   - **Coordinates.** For default-aligned `TextBlock` and shape objects, `TranslateTransform(0, 0)` is centered; `(x, y)` offsets from the scene center. Half-frame coordinates such as `(960, 540)` do not center content in a 1920x1080 scene unless `AlignmentX=Left`/`AlignmentY=Top` was deliberately selected and source-grounded.
   - **`GeometryShape` paths.** Author with the artwork's top-left at `(0, 0)`, all coordinates non-negative: the drawn center lands at the alignment-resolved center *plus* the path bounds origin, so a path centered on `(0, 0)` renders up-left by half its size, and scene-absolute coordinates shift by their full offset. When a path cannot be normalized, add a static `TranslateTransform(-boundsX, -boundsY)`; `measure_object_bounds` reports `geometryBoundsOrigin` and `evaluate_edit_quality(staticLayout:true)` flags uncompensated offsets as `geometryPathOffset`.
   - **`measure_object_bounds`** after creating or modifying layout-sensitive text, shape, and backing-plate pairs — render-node size, scene-space center, transformed bounds, padding — before trusting a still render.
   - For just the container shape, fetch the targeted `insert-new-element-skeleton` example rather than reading a full-scene starter.
5. **Camera rigs.** When a shot has a planned camera move, structure it now so Phase 3 animates one rig transform instead of retrofitting per-element motion. Two patterns, both requiring the `PortalObject` immediately before the flow operator in `Objects` — a bare `IFlowOperator` is rejected by `apply_edit`:
   - **Portal (timeline) rig**, best for multi-element shots: keep the shot's content as ordinary one-object Elements on contiguous ZIndex rows, and put the rig Element directly below them (lower ZIndex) with `Objects` = a `PortalObject` whose `Count` spans the content rows, then a `DrawableGroup` with empty `Children`. `Count` is an inclusive ZIndex span, not an element count: every active Element with ZIndex in `rig+1`..`rig+Count` is pulled in, so content keeps per-element `Start`/`Length` and stays visible as timeline layers to the quality metrics. Keep pulled rows ZIndex-contiguous and time-aligned with the rig — they render ungrouped whenever the rig Element is inactive. `get_examples`: `insert-camera-rig-portal`.
   - **Nested rig**, only for a small cluster that shares the shot's timing exactly: parent the drawables inside the `DrawableGroup`'s `Children`; `PortalObject.Count` stays `0`. `get_examples`: `insert-camera-rig-push-in`.

   Nothing in the toolkit stops you from putting content straight into `Children` — `DrawableGroup.Children` accepts any `Drawable`, and only the `PortalObject` pairing in `Element.Objects` is validated. The cost is silent: a nested child has no `Start`/`Length` of its own, so it cannot be retimed, cannot be cut independently, and disappears from the timeline and from every quality metric that counts layers. Default to the portal rig and reach for nesting only when the cluster genuinely has one lifetime.

   Author the rig's transform statically at the shot's end-state framing here, and keep locked full-frame background plates outside the rig, below the portal's ZIndex range — that separation is also what makes parallax possible. The same portal + flow pairing works for `DrawableDecorator`, `SoundGroup`, and `Scene3D`.
6. **Apply in small stages** mapping to the shot breakdown — background/surface, primary structure/shapes, typography, text backing plates — static values only. Inspect `valid`, `changes`, `validation`, and `createdIds` after each stage. Pass `quiet: true` for large staged patches; the full echoed change set can exceed the response size limit.
7. **On failure**, fix from `get_schema`/`get_examples`/`read_document` and retry only that stage. `validation_rejected`, `unknown_type`, stale handles, invalid animation discriminator tokens, and fallback-object guidance all mean the patch shape is wrong, not that the idea is. Do not invent shorthand values for colors, pens, animations, brushes, transforms, or effects. Falling back to cut-only timing after a keyframe failure silently changes the motion model — only do it if the user accepted that.
8. **`save_project`** after each successful major stage for file sessions; omit `session` unless deliberately disambiguating an older session id. LiveEditor sessions report that saving is not required/supported — record that message rather than treating it as a blocker.
9. **Verify with `read_document_summary`** after each major stage. Compare expected element names/roles against actuals. `isFallback: true` on any object means a placeholder, not a usable visual — fix the patch from schema before rendering. Audit objects whose summary shows nested transform animation plus a static rotation/skew/scale: confirm `TransformGroup.Children` order matches the transform intent you decided, and patch the order with `$before`/`$after`/`$index` if not. Audit object counts: an ordinary Element with multiple objects needs splitting unless it holds a named `IFlowOperator`.
10. **Review the storyboard** before any effects or motion:
    - `render_storyboard` gives one still per shot plus a contact-sheet PNG. Read it as a storyboard: every planned shot present, one clear focal point per shot, readable typography, intended layering and color, aligned text/backing-plate pairs. For many Elements the synchronous call can exceed the MCP client timeout — pass `background: true` for `{ status: "running", jobId }` and poll `read_render_job(jobId)` until `completed`. Do not issue `apply_edit` while a background render runs; `cancel_render_job(jobId)` aborts one.
    - For a continuous single-shot piece whose Element boundaries make no useful auto shots, pass explicit `timeSeconds` — opening, reveal/development beats, settle, final hold — with `subdivisionLevel:1` or `2` to inspect the arc between anchors.
    - `evaluate_edit_quality(videoType:<resolved>, staticLayout:true)` runs document-only checks. Of its categories only `elementStructure` can fail the gate; the rest describe the scene. `suggest_quality_fixes(videoType:<resolved>)` groups multiple issues into the smallest repair. `styleProfile` such as `high-tempo-promo`, `kinetic-type`, or `high-tempo-promo 130bpm` tailors the tempo advisories.
    - `evaluate_edit_quality(videoType:<resolved>, staticLayout:true)` skips motion checks, so the motionless storyboard is judged on composition, typography, readability, and structure only.
    - `measure_object_bounds` for any text/backing-plate pair that looks misaligned on the contact sheet.
    - Do **not** run `evaluate_motion_variation`, `evaluate_edit_quality` without `staticLayout`, or `final_preflight` here — they are motion-phase tools and a static storyboard reads as zero motion.

### Phase 2 — Effects

Add effect chains onto the locked storyboard, one job at a time, and confirm they read on stills before starting motion.

- Material texture, hierarchy separation, transition energy, color grade, and text legibility are what an effect chain usually buys. A stack where no layer has a job generally reads as noise — worth checking, not a rule.
- For organic heat, ink, glass, smoke, grain, caustic, or atmospheric fields, `SKSLScriptEffect` (via `list_effect_recipes`) beats stacking blurred gradient shapes and is CPU-safe in still renders. `fine-film-grain-field` ships a monochrome film-grain shader; `organic-shader-field` ships a colored field shader. `validate_shader` any custom SKSL before `apply_edit`.
- GPU/stylize effects are render-guarded and fall back to SwiftShader when no hardware GPU is present. Confirm each with `render_still`/`render_storyboard`.
- **Masking.** `Drawable.BlendMode` is animatable, and its Porter-Duff modes (`SrcIn`, `DstIn`, `SrcOut`, `DstOut`, `Modulate`) composite against the content below it *in the same flow* — scope the matte by putting mask and content inside one `DrawableGroup`/`DrawableDecorator` Element so it does not knock out the whole frame. The `Clipping` FilterEffect (animatable `Left`/`Top`/`Right`/`Bottom`) is the rectangular wipe/crop primitive. Matte behavior depends on flow order, so source-ground the exact rule and verify with `render_still` before building a sequence on it.
- **Emissive glow/bloom** (light that adds over the original, not a `DropShadow` fake): `duplicate_object` with `wrapInGroup=true`, then `apply_edit` the `additive-bloom` recipe (blur + `BlendMode` `Plus` + reduced `Opacity`) onto the returned `objectId` so the copy glows over the untouched original. `wrapInGroup=true` puts both under a `DrawableGroup`, which keeps the `elementStructure` check clean; move the copy to its own Element at higher `ZIndex` when it needs independent timing or z-order. Lower `Opacity` or switch to `Screen` for bright footage that blows out.
- Re-verify with `render_storyboard`/`render_still` and `evaluate_edit_quality(videoType:<resolved>, staticLayout:true)`.

### Phase 3 — Motion

Add keyframes on top of the locked storyboard and effects. Build reveal, development, and resolution phases, and animate multiple property families (transform, opacity, brush/gradient, effect parameters, text spacing) rather than X plus opacity alone. Apply in small `apply_edit` stages per beat, inspecting `validation` after each; `quiet: true` for large patches.

- **Camera first, accents second.** Keyframe the `[role:camera-rig]` `DrawableGroup`'s `TransformGroup` children — `TranslateTransform.X/Y` for pans and whip-pans, `ScaleTransform.Scale` for push-in/pull-back with `TransformOrigin` set on the shot's focal point, `RotationTransform.Rotation` for roll. `get_examples`: `insert-camera-rig-push-in` (nested) or `insert-camera-rig-portal` (timeline). Camera moves read best slow and eased — a push-in of roughly 3-8% scale over a shot already defeats the static-slide feel — with whip-pan bridges as the fast exception. Parallax means each depth-band rig's translate at a different amplitude, background slowest. Remember the inverse: camera-left = rig-right, camera-in = scale-up. A true 3D dolly or orbit is a `Scene3D` object with keyframed `Camera.Position`/`Camera.Target`, not faked 2D scale.
- **Easing is a vocabulary, not a default.** Cubic/quintic ease-out for entrances, ease-in-out for moves, linear reserved for deliberate mechanical travel. `BackEase*`/`ElasticEase*` give overshoot and anticipation, `BounceEase*` gives physical settles, `SplineEasing` takes custom cubic-bezier control points. A bounce on every element turns cartoonish; used on accents it reads as craft. Staggering related starts by roughly 0.1-0.3 s and varying durations/directions is what makes follow-through and overlapping action visible.
- **Perspective on 2D content** (card flips, page turns, tilted reveals): `Rotation3DTransform` (`RotationX`/`RotationY`/`RotationZ`, `CenterX/Y/Z`, `Depth` — perspective distance, default 500) inside the `TransformGroup`, rather than faking depth with 2D scale.
- **Kinetic type**: `TextBlock.SplitByCharacters=true` (animatable) composites glyphs individually — the enabling property behind per-glyph `ColorShift` fringing and `PartsSplitEffect` shatter — and `TextBlock.Spacing` animates tracking-in/out reveals. Per-character stagger reads differently from moving the block as one unit.
- **Line-drawing reveals**: animate `Pen.TrimStart`/`Pen.TrimEnd` (0-100) on a `GeometryShape`/shape/text stroke. Marching dashes: static `Pen.DashArray` list plus animated `Pen.DashOffset`.
- **Audio is authorable.** Keyframe `Sound.Gain` (percent: 100 = unity, above 100 amplifies) for fades and ducking under narration; set `Sound.Effect` to an `AudioEffectGroup` with `DelayEffect`/`EqualizerEffect`/`CompressorEffect`/`LimiterEffect` children; `SoundGroup` (an `IFlowOperator`, so the `PortalObject` pairing applies) submixes audio Elements.
- **Clock mode is a decision.** `UseGlobalClock=false` means `KeyFrame.KeyTime` is local to the owning Element and normally stays within `00:00:00`..`Element.Length`. `UseGlobalClock=true` means scene timeline time, which should intersect the visible Element range. A `Warning` in `apply_edit.validation` about relative keyframes outside the Element local range is a timing bug unless you meant it — fix by converting to local times or setting `UseGlobalClock=true`.
- **Keyframe shorthand.** `Animations.<Property> = {"$kf": [[0, 0, "CubicEaseOut"], [0.4, 100]]}` expands to the full typed animation: the `KeyFrameAnimation<T>`/`KeyFrame<T>` discriminators come from the property's value type and easings take a bare name. The object form `[{"t": 0.4, "v": 100, "easing": "BackEaseOut"}]` reads better for sparse envelopes; times accept seconds or a TimeSpan string. `UseGlobalClock` and `Id` still apply alongside it. Use this for anything with more than a couple of keys — the explicit form below is ~200 characters per keyframe.
- **Keyframe shapes come from examples.** `get_examples` for `animate-float-property-keyframes` (existing object) or `insert-new-animated-text-keyframes` (new animated text); copy the concrete `KeyFrameAnimation<T>` and `KeyFrame<T>` discriminators. Build one local helper snippet from the example and reuse that exact JSON shape for every animated `Single`, `Boolean`, `Color`, `Size`, or transform property. Hand-typing or manually Unicode-escaping the generic discriminator strings is where invalid tokens come from — an invalid token means the helper is wrong and needs rebuilding from `get_examples`.
- **Rotated moving shapes.** Animating only `TranslateTransform.X` does not travel along the rotated visual axis. Screen-space horizontal/vertical drift with a tilted object needs static `RotationTransform`/`SkewTransform`/non-animated `ScaleTransform` *before* the animated `TranslateTransform` in the `TransformGroup`. A diagonal screen-space path animates X and Y as a vector. A local-axis path that depends on transform order needs a rendered still/motion sample to confirm.

### Phase 4 — Motion verification and export

1. **Re-render the storyboard** with `subdivisionLevel: 1` (raise to `2` for suspicious gaps). Read the contact sheet including every `kind: "inbetween"` frame, and check `cutEyeTrace`. For multi-shot types, each adjacent pair should show the continuity you planned — an element crossing the cut, a camera move continuing, a sweep, shared background, an overlapping ramp, a consistent slideshow transition, or a hard cut you meant. Shots planned as push-in/pan/parallax should show visible framing change between in-between frames; identical framing on a non-locked shot means the camera work did not land. `cutEyeTrace.exceedsEyeTraceBudget` with no planned bridge points at a cut the eye cannot follow. For `logo-intro`, review the single-shot arc instead: anticipation, reveal, easing, settle, final hold.
2. **`render_still`** at representative shot boundaries. Returned `warnings` usually mean something you authored is not visible — check before exporting, then act or note why it is fine. In live-editor sessions renders honor the editor's layer state (`TimelineLayer` lock/solo/audio-mute/video-mute, `Element.IsEnabled`), so a missing layer may be a flag rather than your edit. Per still, `visibilityAnalysis.visiblePixelRatio`, `foregroundPixelRatio`, `occupiedBoundsRatio`, and `maxQuadrantForegroundRatio` plus `activeElements` tell you whether the planned elements actually made it into the frame.
3. **`evaluate_motion_variation`** across 4-6 samples. `low-motion-variation` or `poor-frame-coverage` means the frames barely change — which is a finding about the render, and deliberate stillness is a legitimate cause.
4. **`evaluate_edit_quality(videoType:<resolved>)`** with the same sample set (no `staticLayout`). Pass `plannedForegroundElementsPerShot` for the density comparison, `paletteRoleColors` for the 60-30-10 area mix on motion-graphics/slideshow, and `beatTimesSeconds` from `analyze_audio_rhythm` when a bed was analyzed.

   **What blocks and what does not.** `passesQualityGate` goes false only for unreadable text (`typographyReadTime`, rendered `typographyContrast`) and malformed Element structure (`elementStructure`) — the cases where the result is unusable rather than unusual. `motionContinuity`, `layerDensity`, `tempoRhythm`, `cutRhythm`, `paletteBalance`, `audioSync`, `timelineCoverage`, and `textBackgroundFit` are advisory: they report what the scene measures. Palette harmony, background richness, shape clarity, gradient falloff, and motion arc are no longer analyzed — judge those from a rendered still. Act on the ones that contradict your intent; ignore the rest. Intent flags (`allowStillness`, `allowDenseText`, `allowMultiObjectElements`, `allowMonochrome`, `allowMinimalDensity`) and `[role:...]` tags reword findings as expected rather than unexpected; `relaxAesthetics` drops the advisory block wholesale.

   - `animatedPropertyCount: 0` on a motion-graphics deliverable usually means the motion pass did not land — worth checking before export even when the gate passes.
   - `textBackgroundFit` on decorative glass/light/texture rectangles is better fixed than suppressed: constrain the rectangle to its intended beat, align it as a named backing plate with `measure_object_bounds`, lower it into the background, or replace it with a non-plate treatment.
   - For tempo briefs, `metrics.tempo.RequiredTimelineEventsPerSecond`, `TimelineEventsPerSecond`, `RequiredTotalEventsPerSecond`, `LongForegroundGapCount`, and `LongestForegroundEventGapSeconds` show whether background motion is hiding sparse foreground change.
   - After each revision made in response to a Phase 4 finding, `compare_revisions` before the next broad fix. A `regression=true` means your fix cost something elsewhere.
5. **Compare against your plan.** `read_document_summary` for shot/Element count, `metrics.tempo` (`TimelineEventsPerSecond`, `SlowHoldCount`, `LongestForegroundHoldSeconds`), `metrics.layerDensity` (`Bands`, `AverageForegroundLayerCount`, `MinimumForegroundLayerCount`, `BandsBelowHalfPlannedForegroundLayerCount`), and `evaluate_motion_variation` give the actuals. Execution reliably shrinks a plan, so the gap between planned and actual is the useful number here — it tells you where the piece quietly got sparser than you intended. Whether to close the gap or revise the plan is your call; the toolkit does not decide it.
6. **Convergence loop for unreviewed runs.** When the run started from `beutl-agent-brief-expansion`, or the coordinator asked for convergence, run `beutl-agent-visual-review` in convergence loop mode before `final_preflight`.
7. **`final_preflight(videoType:<resolved>)`** bundles the pre-export checks. Pass `requireAnimatedProperties=true` when a piece with no animated property would be a mistake for this brief, plus `plannedForegroundElementsPerShot`, `paletteRoleColors`, and `beatTimesSeconds` as above. Its `blockers` are limited to unreadable text, malformed structure, and checks you explicitly requested; low motion, sparse density, and still-visibility warnings arrive as `advisories`, so a deliberately still or minimal piece still reports `readyForExport`.
8. **`export_video`** for a preview when an encoder is available; record the reason in notes if not. `crf` (0-51, higher = smaller; ~28-30 for full-frame grain or other hard-to-compress content) or `bitrate` (bits/s, ABR) control size — mutually exclusive. Long exports take `background: true` and `read_render_job(jobId)` polling. In the final report, surface CC-BY-SA share-alike obligations, `license: "unverified"` user-supplied assets, and required attribution text from `assets/manifest.json`.
9. **`save_project`** for file sessions after final revisions. For LiveEditor sessions, `read_operation_status` or `save_project` once near the end reports that the live edit is applied but not file-saved by the toolkit.

## Craft notes

What tends to read well in motion graphics, and why. None of this is checked anywhere:

- **A storyboard that does not read will not read animated.** Composition problems get more expensive with every keyframe layered on top.
- **Three timing phases** — reveal, development, resolution — give a piece shape. A single continuous drift has none.
- **Tempo comes from contrast**, not uniform speed. Quick accents against held readability beats read as fast; every layer moving at one speed reads as busy. Background-only drift does not read as tempo at all.
- **Motion monotony** is mostly two habits: leaving every keyframe transition linear, and starting every element at the same time with the same duration and direction.
- **Move the viewpoint, not just the elements.** A piece where every shot is a static composition with element-level animation, swapped by cuts, reads as a slide deck. This is the single most common way an otherwise competent motion-graphics piece falls flat.
- **One focal point per shot.** Supporting text, marks, panels, and effects sitting lower in scale, contrast, timing, or density is what makes the focal point legible as one.
- **Readability is timed.** At roughly 1.5 s per shot a viewer reads about 1-3 hero words and 2-4 supporting words; past that the text is present but not read. Short copy, split beats, or a longer hold are the three ways out.
- **Density is layered, not counted.** Background surface, foreground motion, accents, and typography together. A lone title over one moving shape reads sparse — which is exactly right for a minimal brief and wrong for a dense one. `metrics.layerDensity` measures it; you decide what it should be.
- **Perceived information beats literal information.** Short typography plus repeated non-rectangular nodes, particles, strokes, texture, and accent motion carries a fast promo further than long copy.
- **Rectangles read as rectangles.** `RectShape` works as a full-frame plate or deliberately plain geometry. A large persistent foreground rect behind several unrelated text beats usually means a backing plate that outlived its shot.
- **Abstract light blobs describe the render, not the idea.** Glint/glow/aperture/lens/glass ellipses give the viewer nothing to parse. Strokes, particles, letter fragments, editor/timeline marks, masks, media, and procedural texture do.
- **A two-stop gradient shows its boundary as a band.** Three or more stops, wider alpha/color transitions, a real Blur/SKSL texture, or a procedural surface hide it.
- **Procedural texture suits organic and atmospheric concepts.** A short `SKSLScriptEffect` on one broad shape usually beats many low-contrast blurred ellipses for heat, ink, glass, smoke, caustics, grain, or shimmer.
- **Names are the interface to your own work later.** A shape whose job cannot be stated — beat sweep, scan texture, pulse reveal, transition wipe, text backing — is indistinguishable from a leftover on the next pass.
- **Held title/logo sections need real motion to register as motion.** `evaluate_motion_variation` requires roughly 2% changed pixels at 48-per-channel delta between adjacent samples; a soft low-alpha ambient wash stays under that threshold. Drifting the held text plus a slow scale breath registers. A calm 3-act brand piece will trip `tempoRhythm`'s 130 BPM-oriented advisories harmlessly.
- **Numerical motion variation is necessary, not sufficient.** Frames can change while the planned elements are never visible or the text is never readable.

## Avoiding convergence

The same brief producing the same video every time is the failure mode this toolkit was reshaped to avoid, which is why preset tables and default look-packs were removed from it.

- Build creative work with small staged `apply_edit` calls. `list_compositions`/`plan_composition` and full-scene examples carry their own shape, so they fit when the user asked for a template, starter, or quick draft — and pull everything toward one look when they do not. When a template is requested, pick a specific name from `list_compositions` rather than relying on implicit first-item selection.
- Treat examples as schema snippets. Adapting their structure to the brief is the point; copying a starter scene unchanged is how the starter becomes the deliverable.
- `list_creative_directions` output is raw stimulus. Seed names as the concept title, Element/Object names, layer order, or file basename put the stimulus in the deliverable.
- Overused no-context motifs — orbit rings, radar sweeps, map/atlas labels, signal nodes, dashboard bars, dark teal with cyan/magenta neon — are what a viewer recognizes as generic.
- Comparing a direction against `recentDirections`, passing the structural signature into `derive_palette`, and calling `record_creative_direction` once the concept is locked is the mechanism that lets successive runs know about each other. Changing the structural language — motion verbs, layout grid, palette family, type treatment, transition style — is what actually differentiates them.

## Shot list mapping

- Element boundaries come from the enumerated shot breakdown. One shot maps to one or more Elements with explicit `Start`, `Length`, and `ZIndex`; each ordinary Element holds exactly one drawable/audio `EngineObject`.
- Multiple `Objects` in one Element are for explicit `IFlowOperator` chains (`DrawableGroup`, `DrawableDecorator`, `SoundGroup`, `Scene3D`); compound visuals otherwise split into separate Elements.
- A shot with a camera move maps to one `[role:camera-rig]` rig Element — a portal rig (`PortalObject.Count = N`) pulling N contiguous content layers directly above it, or a nested `DrawableGroup` parenting the content as `Children` — with locked background plates as separate Elements below the rig, outside the portal's ZIndex range.
- Background plates take lower `ZIndex`; titles, logos, and overlays take higher.
- Explicit durations beat relying on media original duration unless the brief asks to preserve source timing.

## Merge-patch rules

- Arrays of objects with `Id` are id-keyed. A bare id-less array merges/appends into existing members; it does not replace them.
- `{ "Id": "...", "$delete": true }` removes.
- To wholesale-replace an id-keyed array in one patch (swapping a `FilterEffectGroup.Children` chain rather than appending), make the FIRST element the sentinel `{ "$replace": true }`; the following elements rebuild the array in order — omit `Id` to mint fresh, reuse an `Id` to keep that child. `[{ "$replace": true }]` alone clears it. Replacement elements cannot also carry `$delete`/`$index`/`$after`/`$before`. Keep the group's own `Id` so only its children change.
- `$index`, `$after`, or `$before` for ordering; do not combine ordering directives.
- Unknown `Id` means a stale handle — call `read_document` again rather than guessing.
- Existing flow parent with a new child: `{ "Elements": [{ "Id": "<existing-flow-element-id>", "Objects": [{ "$type": "<discriminator-from-get_schema>", "Name": "new-flow-child" }] }] }`. Only for an intentional `IFlowOperator` chain.
- New Element: `{ "Elements": [{ "$type": "[Beutl.ProjectSystem]:Element", "Name": "new-element", "Start": "00:00:00", "Length": "00:00:02", "Objects": [{ "$type": "<drawable-discriminator-from-get_schema>", "Name": "new-object" }] }] }`. New Elements and Objects omit `Id`.
- New flow-operator Element (e.g. a `[role:camera-rig]` `DrawableGroup`): `"Objects": [{ "$type": "[Beutl.ProjectSystem]:PortalObject", "Count": <N> }, { "$type": "<DrawableGroup-discriminator>", "Name": "[role:camera-rig] ...", "Children": [...] }]`. A bare flow operator without the `PortalObject` is rejected. `Count` is an inclusive ZIndex span: every active Element with ZIndex in `rig+1`..`rig+Count` is pulled in (leave `Children` empty then). `Count: 0` pulls no timeline rows, so with the portal first the operator consumes only its nested `Children`; `Clear: true` explicitly discards earlier same-Element flow. `get_examples`: `insert-camera-rig-push-in` (nested), `insert-camera-rig-portal` (timeline).

## Progress watchdog

- Keep `notes.md` granular enough for another observer to reconstruct the route: every apply, save, render, evaluate, export, validation failure, and route change.
- During long patch authoring between tool calls, update `notes.md` before the three-minute mark with a heartbeat such as `drafting stage N patch; next tool: apply_edit`.
- If no tool success, saved artifact, render/export artifact, or notes update happens for about three minutes while editing, report the blocker rather than continuing silently.
- On a status request, `read_operation_status` when available, then respond immediately with session/source, last successful stage, and blocker.

## Boundaries

- Keep values in documented ranges. Coercion or rejection from `apply_edit` means adjust and retry the same small stage.
- Confirm destructive output overwrites only when the user explicitly asked for overwrite.
- Do not write outside `BEUTL_WORKSPACE`.
