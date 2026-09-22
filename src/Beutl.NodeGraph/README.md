# Beutl.NodeGraph contributor guide

Node editor (graph-based programming surface). The runtime evaluation happens here; the visual layer is in `Beutl.Controls`.

## Core types

- `GraphModel` — top-level container; owns nodes and connections
- `GraphNode` — node base; subclasses declare ports
- `IInputPort` / `IOutputPort` / `IDefaultInputPort` — port abstractions; `EnginePropertyBackedInputPort` bridges to `Beutl.Engine` `CoreProperty<T>`
- `Connection` — typed edge between two ports
- `GraphGroup` — sub-graph that exposes a smaller port surface to its parent
- `IDynamicPort` / `IDynamicPortNode` — nodes that grow / shrink ports at runtime
- `INestedInputPort` — binds an input to an engine property inside a member's local object or list
- `GraphNodeRegistry` — discovers node implementations via attribute-based registration

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

## Mandatory rules

1. **Type compatibility on connect.** `Connection` enforces port type compatibility; do not bypass it. Adding implicit conversions belongs in `Composition/` or a dedicated converter node, not in `Connection.Connect`.
2. **No allocations per evaluation.** `Evaluate(...)` is called per frame per visible node. Cache reusable buffers on the node instance.
3. **Dynamic ports survive serialization.** When adding an `IDynamicPort` node, ensure save / load round-trips preserve the port arity. Tests under `tests/Beutl.UnitTests/NodeGraph/` cover the basic pattern.
4. **Group I/O parity.** A `GraphGroup`'s inner inputs / outputs must match its outer ports 1:1. Drift here corrupts loaded files.

## Common traps

- **Stale evaluation** — connections cache the producer's last output; if a node's evaluation order changes, invalidate the cache rather than reading the stale value.
- **`IDefaultInputPort`** holds a literal value that takes effect when no connection is present. It must serialise even when a connection *is* present, so reconnects restore the literal.
