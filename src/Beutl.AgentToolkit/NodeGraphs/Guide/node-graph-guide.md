# Node graphs and GPU passes

A node graph composes nodes connected by wires. Shader nodes in it run GLSL passes on the GPU, with any number of passes, inputs and intermediate images, and no C#. Beutl allocates, reuses and frees the intermediate images, orders the passes by their connections, caches compiled programs and synchronizes the GPU. Use a graph for what built-in effects and their combinations cannot express; when an existing effect does the job, set it with apply_edit, which adds no GPU passes.

## Where a graph lives

- `NodeGraphFilterEffect` ({NodeGraphFilterEffect}) is an effect: add it where effects go, as a drawable's `FilterEffect` or in a `FilterEffectGroup`'s `Children`. Its graph reads the layer from a `FilterEffectInputNode` and replaces the layer with what reaches an `OutputNode`. Without a `FilterEffectInputNode`, or with nothing reaching an `OutputNode`, the layer passes through unchanged.
- `NodeGraphDrawable` ({NodeGraphDrawable}) is an object of `Element.Objects`; it draws what reaches its `OutputNode`.

Add either one with apply_edit and take its Id from createdIds; a new one holds an empty graph. That Id is the graphId of read_node_graph and edit_node_graph. A group or loop node's Id names its inner graph.

## Editing

- edit_node_graph applies a list of operations as one undoable edit: `add`, `update`, `remove`, `connect`, `disconnect`. A new node can take a `ref` that later operations in the same call use in place of its Id.
- A port is `"<node>.<port>"`, the node being a ref, an Id, or `@Type` for the only node of a type in that graph: `"blur.Out.color"`, `"composite.In.glow"`, `"@ShaderLoopInput.Image"`. Connect an output (`from`) to an input (`to`). An input takes one connection; pass `replace: true` to swap it.
- A group or loop node's inner graph is edited by operations with `graph` set to that node's ref or Id. Connections stay within one graph; values cross into a group through its `GroupInput` and out through its `GroupOutput`, which add a matching port when connected without a port name. Inside, a port of `GroupInput`, `GroupOutput` or a loop's input and output takes the name of the port it connects to, and the group or loop node shows the new name, so connect the group or loop from outside first; the response's `ports` gives the names that result.
- `values` sets members by name. Numbers and text are plain JSON; enums take their names; `Vector2`, `Vector3` and `Vector4` take `"x,y"`, `"x,y,z"` and `"x,y,z,w"`. `{"$kf": [[seconds, value, easing?], ...]}` sets keyframes, timed from the start of the element; a plain value replaces them.
- Every call first runs on a copy of the scene. An operation that cannot apply, or a result with an error the graph did not have before, leaves the scene untouched; the response gives each diagnostic and `applied: false`. Pass `validateOnly: true` to check a change without applying it.
- read_node_graph returns the nodes, their members with values and connections, and diagnostics from a fresh evaluation. list_node_types lists the node types and, for the ones you name, their members.
- The graph is saved with the project like any other edit, and undo reverts a whole edit_node_graph call.

## Shader passes

`ShaderFullscreenPassNode` runs a fragment shader once per pixel of its output; `ShaderComputePassNode` runs a compute shader; `ShaderRenderPassNode` draws with your vertex and fragment shaders. Their code is GLSL compiled for Vulkan:

- Start with `#version 450`; the entry point is `main`. In a fullscreen pass, read `layout(location = 0) in vec2 uv;` (0 to 1 across the output) and write `layout(location = 0) out vec4 color;`, more outputs at consecutive locations.
- Bind resources as `layout(set = 0, binding = N)`. Set 0 only; no descriptor arrays and no multisampling. Uniform blocks are std140, storage buffers std430, and push constants hold at most 128 bytes. Storage images need a format qualifier such as `rgba16f`.
- The code declares the ports. Setting `Code` updates them, keeping the value and connections of a port whose name and type stay; code that does not compile keeps the old ports, and the call is refused with the compiler's messages, each with its stage and its line within that stage's code.
  - A sampler, image or buffer becomes an input `In.<name>`. A writable image or buffer is also an output `Out.<name>`, and each fragment output is `Out.<name>`.
  - Each member of a uniform block becomes an input `In.<instance>.<member>`, and each push-constant member `In.<instance>.<member>`, by the block's instance name, or its type name without one. A specialization constant becomes `In.<name>`.
  - float, vec2, vec3 and vec4 take `float`, `Vector2`, `Vector3` and `Vector4`; mat3 and mat4 a `Matrix`; int, uint, int64, uint64 and double their own types; a `bool` member a `uint`. Arrays, structs and other types take a `byte[]` of exactly the member's size, which `ShaderDataNode` or `ShaderPackNode` can make.
- A sampler left unconnected reads the source. A wire into a value port drives it every frame, such as `TimeNode.Time` into `In.params.time`.

## Coordinates, size and resolution

- The first picture a graph reads is its source; in an effect, the layer is the source whenever the graph reads it. Any other picture is lined up with the source and cut to what it covers.
- A pass's output covers what it reads: its first target, else the first input that has a place, else the source. `Padding` adds that many pixels on each side, for a glow or a distortion that reaches past the layer. Padding adds to what later passes read, so pad the first pass that spreads, and the last.
- `uv` reads every input at the same place: an input that covers something else gets a copy aligned with the output texel for texel, so reading past the output's edge finds the sampler's border, not more of the picture.
- `ResolutionScale` sets a pass's pixel density relative to the render, such as 0.5 for a blur at half resolution. Renders at another scale, of part of a frame, or after a seek give the same picture.

## Color and formats

- Pictures enter and leave a graph linear and premultiplied. A pass writes RGBA16Float by default; its `Format` chooses another, and integer outputs become RGBA32SInt or RGBA32UInt. `ShaderConvertColorNode` converts to and from other encodings.
- What reaches the `OutputNode` of an effect must be a float 2D color image or a picture.

## Time

- `TimeNode` gives `Time`, seconds since the element started; `Start` and `Duration` of the element; and `Progress`, from 0 to 1 over it. Keyframes set with `$kf` are timed the same way.
- `ShaderRepeatNode` runs its body `Count` times, and `ShaderReplayNode` runs it in fixed steps from `ResetTime` to now, so a simulation seeks to the same state as playing. A new loop's body holds `@ShaderLoopInput`, `@ShaderLoopOutput` and `@ShaderLoopInfoNode`, with one image state named `Image`: what reaches `@ShaderLoopOutput.Image` is `@ShaderLoopInput.Image` in the next iteration, and the loop node shows the state as an input and an output named `Image`, the first iteration's and the last one's. `ShaderLoopInfoNode` gives the iteration's index, count, time and step.

## Limits and failures

- A replay of more than 100,000 steps is refused, and padding inside a loop grows with every iteration; pad before the loop.
- What only the GPU can refuse, such as an image larger than the device allows or a geometry stage on macOS, shows when the graph renders: render_still lists it in its warnings, the graph draws nothing there, and the rest of the frame renders.
- render_still, render_storyboard and export_video show the result; a graph that validates has not been seen yet.

## Examples

The examples are operation lists for edit_node_graph on a new, empty NodeGraphFilterEffect.
