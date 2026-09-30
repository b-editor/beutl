---
name: beutl-agent-look-effect-chain
description: Edit Beutl effect chains, masks, and shader properties through the Agent Editing Toolkit MCP tools.
---

# Beutl Effects and Shaders

Use the requested appearance and motion to choose the edit. Beutl exposes editable building blocks, compilation diagnostics, and rendered output. It does not choose a palette, score a look, or decide that a video is finished.

## Editing contracts

Inspect the target with `read_document` and query unfamiliar properties with `get_schema(type=...)`. `list_effects(intent=...)` and `get_effect_recipe` are optional discovery aids. An absent recipe does not make a composition of geometry, masks, transforms, animation, and effects unsupported.

Patch the target through `apply_edit` with PascalCase properties and runtime `$type` discriminators. Id-keyed arrays preserve unmentioned entries. Effect order matters; inspect the existing chain before changing it. Preserve unrelated timing and media bindings. Use natural content-based names in the user's language.

`duplicate_object(wrapInGroup:true)` places the source and copy under a `DrawableGroup` for effects such as additive bloom. Use separate Elements when timing or z-order must differ. `LayerEffect` on a group flattens its children before group opacity, avoiding overlapping children showing through one another during a group fade.

A matte uses drawable blend/compositing behavior, not a generic `Mask` property. `Clipping` provides an animatable rectangular crop. Scope compositing with a group/decorator and verify its actual rendered bounds. `measure_object_bounds` measures direct `Element.Objects`; nested drawables require measuring their containing group or an isolated probe.

## Shader contracts

Honor explicit language and implementation constraints. Built-in effects and declarative shaders can be combined directly. The current multi-input/multi-pass GLSL route requires `CSharpScriptEffect` and `Context.CustomEffect`; it cannot satisfy a no-C# or no-CustomEffect requirement. Beutl source code is not required; use the runtime's documented script examples and schemas instead of guessing internal APIs.

When that route is permitted, `CreateGlslShader(fragmentSource, inputCount)` and `shader.Render(execution, inputs, outputBounds, pushConstants)` run inside `Context.CustomEffect`. Dispose shader wrappers with `using`; compiled programs are cached by the effect. Inputs bind as `sampler2D` at set 0, bindings 0 through `inputCount-1`; `GLSLShader.MaximumInputCount` is the active device limit. `fragCoord` spans the output texture. Map padded or differently sized inputs using their `RasterBounds` and `Scale`; the destination-callback overload supplies the actual allocated size and density.

Push constants must match GLSL layout, occupy a multiple of 4 bytes, and fit in 128 bytes. The caller owns returned targets: dispose intermediates, return the final target through `execution.ForEach`, and dispose an empty preview result while retaining its source. Keep time-varying values in constants and derive animation from `Time` so seeking does not depend on playback order.

`validate_shader` checks compilation. Validate embedded GLSL separately from a C# wrapper: C# compilation alone does not compile GLSL. Render the relevant frames or motion with `render_still`, `render_storyboard`, or `export_video` to assess the effect itself. A successful compile or nonzero pixel difference is not evidence that the requested look has been achieved.
