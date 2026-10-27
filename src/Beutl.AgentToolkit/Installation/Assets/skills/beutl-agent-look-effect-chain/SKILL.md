---
name: beutl-agent-look-effect-chain
description: Edit Beutl effect chains, masks, and shader properties through the Agent Editing Toolkit MCP tools.
---

# Beutl Effects and Shaders

Use the requested appearance and motion to choose the edit. Beutl exposes editable building blocks, compilation diagnostics, and rendered output. It does not choose a palette, score a look, or decide that a video is finished.

## Editing contracts

Inspect the target with `read_document` and query unfamiliar properties with `get_schema(type=...)`. `list_effects(intent=...)` and `get_effect_recipe` are optional discovery aids. An absent recipe does not make a composition of geometry, masks, transforms, animation, and effects unsupported.

Patch the target through `apply_edit` with PascalCase properties and runtime `$type` discriminators. Id-keyed arrays preserve unmentioned entries. Effect order matters; inspect the existing chain before changing it. Preserve unrelated timing and media bindings. Use natural content-based names in the user's language.

When scoping effects with an `IFlowOperator` such as `DrawableGroup` or `DrawableDecorator`, prefer input through flow over directly populating its `Children`. For shared timing, use `Element.Objects = [sources..., PortalObject(Count=0, Clear=false), operator]`. For separate timing or timeline rows, place sources at `ZIndex = z+1` through `z+Count`, where `z` is the operator Element's `ZIndex`, and keep a `PortalObject` immediately before the operator. Ordinary effect/transform collections such as `FilterEffectGroup.Children` still use direct children.

Groups consume all compatible upstream inputs, including an earlier background. When applying an effect to only a subset, give its input/operator chain its own Element and keep unrelated content in separate Elements outside the portal span, preserving timing and draw order.

For effects such as additive bloom, inspect the source location first. For a direct `Element.Objects` drawable, use `duplicate_object(wrapInGroup:false)` and feed the original and copy into the group through one of those arrangements. For a drawable already nested in `DrawableGroup.Children`, use `wrapInGroup:true` to append the copy to its existing group; the false path searches only direct objects. Preserve the group hierarchy and apply the recipe's drawable patch at the copy's actual nested `Children` path from `read_document`, using the returned copy ID. `LayerEffect` on a group flattens its content before group opacity, avoiding overlapping inputs showing through one another during a group fade.

A matte uses drawable blend/compositing behavior, not a generic `Mask` property. `Clipping` provides an animatable rectangular crop. Scope compositing with a group/decorator and verify its actual rendered bounds. `measure_object_bounds` measures direct `Element.Objects`; nested drawables require measuring their containing group or an isolated probe.

## Shader contracts

Honor explicit language and implementation constraints. Built-in effects and declarative shaders can be combined directly. The current multi-input/multi-pass GLSL route requires `CSharpScriptEffect` and `Context.CustomEffect`; it cannot satisfy a no-C# or no-CustomEffect requirement. Beutl source code is not required; use the runtime's documented script examples and schemas instead of guessing internal APIs.

When that route is permitted, `CreateGlslShader(fragmentSource, inputCount)` and `shader.Render(execution, inputs, outputBounds, pushConstants)` run inside `Context.CustomEffect`. Dispose shader wrappers with `using`; compiled programs are cached by the effect. Inputs bind as `sampler2D` at set 0, bindings 0 through `inputCount-1`; `GLSLShader.MaximumInputCount` is the active device limit. `fragCoord` spans the output texture. Map padded or differently sized inputs using their `RasterBounds` and `Scale`; the destination-callback overload supplies the actual allocated size and density.

Push constants must match GLSL layout, occupy a multiple of 4 bytes, and fit in 128 bytes. The caller owns returned targets: dispose intermediates, return the final target through `execution.ForEach`, and dispose an empty preview result while retaining its source. Keep time-varying values in constants and derive animation from `Time` so seeking does not depend on playback order.

`validate_shader` checks compilation. Validate embedded GLSL separately from a C# wrapper: C# compilation alone does not compile GLSL. Render the relevant frames or motion with `render_still`, `render_storyboard`, or `export_video` to assess the effect itself. A successful compile or nonzero pixel difference is not evidence that the requested look has been achieved.
