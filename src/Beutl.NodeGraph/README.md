# Beutl.NodeGraph contributor guide

Node editor (graph-based programming surface). The runtime evaluation happens here; the editor UI is in `Beutl.Editor.Components/NodeGraphTab/`.

## Core types

- `GraphModel` — top-level container; owns nodes and connections
- `GraphNode` — node base; subclasses declare ports
- `IInputPort` / `IOutputPort` / `IDefaultInputPort` — port abstractions; `EnginePropertyBackedInputPort` bridges to a `Beutl.Engine` `IProperty<T>`
- `Connection` — typed edge between two ports
- `GraphGroup` — sub-graph that exposes a smaller port surface to its parent
- `IDynamicPort` / `IDynamicPortNode` — nodes that grow / shrink ports at runtime
- `INestedInputPort` — binds an input to an engine property inside a member's local object or list
- `GraphNodeRegistry` — node catalog; `NodesRegistrar.RegisterAll` registers the built-in nodes with explicit `RegisterNode` / `RegisterNodes` calls

## Nested object inputs

`GraphNode.Items` contains only the declared root members. Use `EnumerateMembers()` for evaluation,
connection lookup, and cascade removal; nested inputs live in `NestedInputPorts` so group I/O indices
remain unchanged. Their bindings are maintained by the model, independently of expanded editors.
Property paths use property names and stable object IDs for list elements. Replacing an object keeps
matching paths and value types; replacing a list element does not transfer its connections to the new
element. `CanConnectInput` enforces exclusive connections between an object input and its descendants.
Descendant inputs bind to local values and are unavailable while an ancestor has an animation or
expression; the overridden property itself remains connectable.
Fallback objects retain their serialized nested ports and connections without evaluating them until
the original type is restored. Bulk list edits publish port changes together; consumers can use
`NestedInputPortsChanged` to reconcile once after the bindings and collection are synchronized.

## Shader nodes

`Nodes/Shaders` exposes `ShaderGraphBuilder` as a graph. A wire carries a `ShaderGraphValue<T>`: a recipe that
declares a resource and the passes writing it, evaluated when a `ShaderApplyNode` records. Evaluating a value
evaluates what it reads, so connections alone decide what runs and in which order, and a node nothing reaches
records nothing. A write in place comes out as a new value; read that one to see the write.

- **One pass is one node.** A pass compiles its GLSL with `ShaderAuthoringInterface` and shows the declarations as
  ports: samplers, images and buffers as wires, block members and specialization constants as values, what it writes
  as outputs. Saved ports come back without compiling; code that fails to compile keeps its ports and connections.
- **An output covers what the pass reads.** It takes its shape from the first target, else the first input that has
  a position, else the source; a fixed-size input such as a lookup table does not count. Padding adds to that.
- **`uv` reads every input at the same place.** Padding an output, or reading images of different coverage, would
  otherwise move the picture, so an input that covers something else gets a copy that lines up with what the pass
  writes. A read past the output's edge therefore finds what the sampler gives outside the copy. Fixed-size images,
  meshes and passes wired to image parameters keep their own coordinates.
- **Loops are groups.** Each state output feeds the state input at the same position, added and removed as a pair;
  a state nothing writes keeps its value. `ShaderLoopInfoNode` gives the iteration's number and time. Evaluating the
  body builds wire values, so recording visits iterations in order and a long replay cannot overflow the stack.
  A pass's padding adds to what it reads, so padding inside a loop grows with every iteration; pad before the loop.
- **Failures stay on the node.** Recording and validation run during evaluation, so the message appears on the pass
  and on the apply node. What only the device can refuse, such as a stage it lacks or an image too large, reaches the
  apply node when the graph runs. Either way that graph draws nothing while the rest of the frame renders.

## Mandatory rules

1. **Type compatibility on connect.** `Connection.Connect` does not check types: the editor's `CompatibleNodeFinder` matches port types, `GraphNode.CanConnectInput` decides whether an input can take a connection, and `ItemValue<T>.PropagateFrom` converts values. Adding implicit conversions belongs in `Composition/` or a dedicated converter node, not in `Connection.Connect`.
2. **No allocations per evaluation.** `GraphNode.Resource.Update` is called from `GraphSnapshot.Evaluate` per frame per visible node. Cache reusable buffers on the node instance.
3. **Dynamic ports survive serialization.** When adding an `IDynamicPort` node, ensure save / load round-trips preserve the port arity. Tests under `tests/Beutl.UnitTests/NodeGraph/` cover the basic pattern.
4. **Group I/O parity.** A `GraphGroup`'s inner inputs / outputs must match its outer ports 1:1. Drift here corrupts loaded files.

## Common traps

- **Stale evaluation** — connections cache the producer's last output; if a node's evaluation order changes, invalidate the cache rather than reading the stale value.
- **`IDefaultInputPort`** holds a literal value that takes effect when no connection is present. It must serialise even when a connection *is* present, so reconnects restore the literal.
