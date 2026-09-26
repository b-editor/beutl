---
name: beutl-agent-look-effect-chain
description: Apply a consistent look or effect chain to Beutl elements through the Agent Editing Toolkit MCP tools.
---

# Beutl Agent Look Effect Chain

Use this skill when applying color, blur, shadow, stylization, or other effect chains across Beutl elements.

This skill is a capability guide, not a rulebook. The mechanics sections describe how the toolkit actually behaves — get those wrong and the patch fails or renders something you did not author. The craft sections describe what tends to happen visually; they are observations you are free to overrule, and nothing in them is checked anywhere in the toolkit.

Start by describing the intended appearance, material, and motion from the brief or reference. Then break that result into operations to implement. Effect/object catalogs list building blocks; recipes show some combinations. Neither enumerates every expression those blocks can produce. Look up unfamiliar pieces as needed, and test a small representative edit before expanding the look across shots.

Choosing the implementation is part of the editing task. Use a built-in feature when it produces the requested result, combine features when that is enough, and write a custom script when the result needs it. Do not require the user to name an effect, request a shader, or choose a programming language before making that choice; honor any explicit constraint they did give. Every discovery call should resolve a concrete implementation question. Once the next edit is understood, apply it and inspect the render instead of continuing to inventory features. An unavailable type or missing recipe is a reason to try another implementation of the same visual intent, not to silently substitute a simpler look. Report a limitation only with the specific missing capability or failed prototype.

## Mechanics

How to make the edit land:

1. **Check the surface needed by the planned edit.** `read_document` gives the element/object handles to modify. For unfamiliar types, `get_schema(type=...)` gives parameter ranges, defaults, animatable flags, and expression support. Use a category filter only when the needed type is unknown; a full effect/drawable inventory is not a prerequisite. Values outside the schema range come back coerced — retry the same small stage with the accepted value.
2. **Use PascalCase property keys exactly as `get_schema` exposes them.** Treat effect arrays as id-keyed when entries have `Id`. Reorder with `$index`, `$after`, or `$before` rather than deleting and reinserting, which mints a new `Id` and loses identity.
3. **Copy object shapes, don't invent them.** For `Pen`, brush, transform, animation, and effect values, copy the shape from `get_schema`/`read_document` with a concrete `$type` discriminator instead of a shorthand field you guessed.
4. **Prefer a merge-patch** so existing element timing and unrelated properties survive. Patch only the target `Objects`, effect collections, and property values.
5. **Apply in small stages.** Call `apply_edit` per coherent look stage and inspect `valid`, `changes`, `validation`, and `createdIds` before continuing. Resolve `validation_rejected`, `unknown_type`, fallback-object, and stale-handle errors by re-reading targeted `get_schema`/`read_document` and retrying only that stage. `unknown_type` means that serialized type was not resolved; check its discriminator and runtime availability. If unavailable, preserve the intended look by trying supported building blocks or a custom script effect before reporting a concrete limitation.
6. **Save when the session is file-backed.** Call `save_project` after a successful major stage. LiveEditor sessions do not need it; `save_project`/`read_operation_status` will say so.
7. **Verify by rendering.** `render_still` before and after the most visible transition points. A look cannot be judged from the JSON document.

### Coordinate and transform semantics

- Default `Drawable` alignment is centered: `TranslateTransform(0, 0)` means centered, and `(x, y)` offsets the object center from the scene center unless `AlignmentX=Left`/`AlignmentY=Top` is set deliberately.
- Use `measure_object_bounds` for text/backing-plate or shape alignment before judging alignment from a still.
- `TransformGroup.Children` order is behavior, not formatting. For screen-space drift on a tilted object, static orientation transforms precede the animated `TranslateTransform`; for local-axis motion the reverse. When you change a moving rotated object, decide which you meant and preserve the existing order unless the change is specifically fixing it.
- If a target has animated transform children plus a static rotation/skew/scale, verify the result with a still or motion sample rather than assuming the order composed as intended.

### Effect vocabulary

Use `list_effects(intent=...)` when a planned operation needs an unfamiliar effect, or go directly to `get_schema(type=...)` for a known building block. `list_effect_recipes` and `get_effect_recipe` are optional implementation examples. An empty intent/recipe match says nothing about what a combination can express. For a result with no recipe, compose geometry, masks, transforms, keyframes, and effect chains, or write a custom `SKSLScriptEffect`/`GLSLScriptEffect`/`CSharpScriptEffect` using its schema contract. Call `validate_shader` for custom scripts and verify the rendered result before repeating it across shots. Never invent serialized types or property names.

For masked reveals, knockouts, and alpha mattes: `Drawable.BlendMode` Porter-Duff modes (`SrcIn`, `DstIn`, `SrcOut`, `DstOut`, `Modulate`) composite against the content below in the same flow — scope the matte with a `DrawableGroup`/`DrawableDecorator` so it does not affect the whole frame. The `Clipping` FilterEffect (animatable `Left`/`Top`/`Right`/`Bottom`) is the rectangular wipe primitive. Source-ground the compositing rule and verify with `render_still` before relying on it.

For GLSL with multiple inputs, intermediate passes, or expanded output, use `CSharpScriptEffect` to orchestrate `CreateGlslShader(fragmentSource, inputCount)` and `shader.Render(execution, inputs, outputBounds, pushConstants)` inside `Context.CustomEffect`. Dispose each shader wrapper with `using`; the effect owns a bounded cache of compiled programs so stable code is reused across frames. Inputs bind as `sampler2D` at set 0, bindings 0 through `inputCount-1`. `fragCoord` spans the output texture; use each target's `RasterBounds` and `Scale` when mapping differently sized or padded inputs. The destination-callback overload supplies the actual allocated size/density. Push constants must match GLSL layout, occupy a multiple of 4 bytes, and fit in 128 bytes. The caller owns returned targets: dispose intermediates, return the final target through `execution.ForEach`, and dispose an empty preview result while keeping its source. Validate embedded GLSL separately from the C# wrapper; C# compilation alone does not validate it. Keep time-varying values in constants, and derive deterministic animation from `Time` so seeking does not depend on playback order.

### Element structure

Keep ordinary Elements to one EngineObject. Multiple Objects in one Element are structurally valid only when the Element contains an `IFlowOperator` — `DrawableGroup`, `DrawableDecorator`, `SoundGroup`, or `Scene3D`. This is one of the two findings that fails `evaluate_edit_quality`, because a multi-object Element without a flow operator is a malformed document rather than an unusual one. Split the visual into its own Element, or add the flow operator you meant.

### Source grounding

When an edit depends on effect-unit semantics, transform composition, bounds, text measurement, backing-plate alignment, render scale, or live-session behavior, load and follow the installed `beutl-agent-source-grounding` skill, then do narrow `rg`/read passes over the source and tests it points at. Note what you verified before the first dependent `apply_edit`. Skip this if the user has forbidden source reading.

## Deriving the look

`derive_palette` takes `baseHueDegrees`, `tonalSeed` (dark/light/balanced), `harmonyScheme` (analogous, complementary, split-complementary, triadic, tetradic, monochromatic), an optional `saturation`, an optional `derivationReason`, and an optional `structuralSignature`. It returns role-tagged colors (`bg-base`, `bg-accent`, `foreground`, `text-primary`, `accent`) with contrast relationships already solved — `text-primary` clears 4.5:1 against both background roles by construction. Hand-picking colors is equally valid; it just leaves the contrast checks to you. Use concrete serialized values such as `#ffffb34d` rather than palette names such as `Amber`.

`list_creative_directions` reports what recent runs in this workspace looked like. If the point is that this piece should not resemble the last one, that list is the cheapest way to know. If a repeat is what the brief wants, ignore it.

`get_background_grammar` lists background slots, options, and parameter ranges when the look touches a background, surface, atmospheric layer, glow field, vignette, or depth treatment. Background plus midground plus foreground is the combination that reads as depth; fewer bands read as deliberately flat.

## Craft notes

Observations about what viewers tend to see. Treat them as knowledge, not permission:

- **Measure the black rather than eyeballing it.** Dark-mode work is decided by how tight the blacks are, and a first shader pass routinely lands far brighter than intended — SKSL runs in linear, premultiplied space, so an sRGB constant pasted into a shader reads roughly 4× too bright. Compare against a reference and rebuild the value rather than nudging it.
- **Contrast survives on luma, not hue.** Changing hue while keeping text and background at similar luma fails readability even when the colors feel different.
- **Saturation needs a hierarchy.** Several saturated glows at the same lightness compete; one dominant role plus quieter support reads as deliberate.
- **Dark teal with cyan/magenta neon** is the combination most generators reach for, so it reads as a default rather than a choice.
- **A two-stop gradient shows its boundary as a band.** Three or more stops, wider offsets, a real Blur/SKSL texture, or a procedural surface hide it — relevant for ambient, aperture, and glow fields.
- **Effects that serve nothing read as noise.** Material texture, hierarchy separation, transition energy, color grade, and text legibility are what an effect chain usually buys. Three or more foreground objects each carrying dense three-effect stacks reads as maximal, which is a look you can want.
- **Rectangles read as rectangles.** `RectShape` works as a full-frame plate or as deliberately plain geometry; as a foreground glint or slash it reads as a placeholder. Non-rectangular accents, strokes, and procedural texture read as form.
- **Abstract light blobs describe the render, not the idea.** A shape named only glint, glow, aperture, lens, glass, reflection, or refraction gives the viewer nothing to parse — it reads as haze. A concrete visual system, or the same light moved into the background with soft falloff, reads as authored.
- **Naming carries intent forward.** `[role:background]`, `[role:text-backing]`, and `[role:decorative]` tags, plus role/motion-purpose names, are what let a later pass — yours or the quality tools' — tell a designed accent from a leftover.
- **Hierarchy is a zero-sum budget.** A look change that makes supporting effects, panels, or labels compete with the primary focal point has moved the focal point, whether or not that was the intent.

## Consistency

- For a shared look across shots, the same property values across matching shots is what makes it read as one piece; brief-named exceptions are the point of the exceptions.
- Preserve source media and audio bindings unless the user asked to replace them.
- If text uses a backing plate, keep the text and `[role:text-backing]` plate timing, center, and padding aligned after the look change.

## Checking the result

`evaluate_edit_quality(staticLayout:true)` (document-only) and `evaluate_edit_quality` (rendered) measure the scene and report what they find. Only two families fail the gate — unreadable text (read time, rendered contrast) and malformed Element structure — because only those mark a result nobody can use. Density, motion, palette, and tempo findings are advisory: they describe the scene, they do not prescribe one. Background design and shape/form quality are not analyzed by this tool; judge both from rendered stills even when the quality report passes.

Read the advisories, act on the ones that contradict your own intent, and ignore the rest. Setting an intent flag (`allowStillness`, `allowDenseText`, `allowMultiObjectElements`, `allowMinimalDensity`) or a `[role:...]` tag rewords a targeted finding as expected rather than unexpected, which is useful when a later pass reads the report. Monochrome palettes need no opt-in; use `[role:decorative]` only for intentionally non-informational low-contrast text. `relaxAesthetics` suppresses only repeated hard-cut cadence, transition-vocabulary inconsistency, and high-tempo long-hold/short-segment pacing advisories. It leaves layer density, audio sync, palette balance, text/background fit, and other advisory output intact.

`final_preflight` bundles the pre-export checks. `export_video` never consults any of them.
