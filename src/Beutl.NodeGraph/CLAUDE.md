# Beutl.NodeGraph — local context

Node editor (graph-based programming surface). The runtime evaluation happens here; the visual layer is in `Beutl.Controls`.

## Core types

- `GraphModel` — top-level container; owns nodes and connections
- `GraphNode` — node base; subclasses declare ports
- `IInputPort` / `IOutputPort` / `IDefaultInputPort` — port abstractions; `EnginePropertyBackedInputPort` bridges to `Beutl.Engine` `CoreProperty<T>`
- `Connection` — typed edge between two ports
- `GraphGroup` — sub-graph that exposes a smaller port surface to its parent
- `IDynamicPort` / `IDynamicPortNode` — nodes that grow / shrink ports at runtime
- `GraphNodeRegistry` — discovers node implementations via attribute-based registration

## Mandatory rules

1. **Type compatibility on connect.** `Connection` enforces port type compatibility; do not bypass it. Adding implicit conversions belongs in `Composition/` or a dedicated converter node, not in `Connection.Connect`.
2. **No allocations per evaluation.** `Evaluate(...)` is called per frame per visible node. Cache reusable buffers on the node instance.
3. **Dynamic ports survive serialization.** When adding an `IDynamicPort` node, ensure save / load round-trips preserve the port arity. Tests under `tests/Beutl.UnitTests/NodeGraph/` cover the basic pattern.
4. **Group I/O parity.** A `GraphGroup`'s inner inputs / outputs must match its outer ports 1:1. Drift here corrupts loaded files.

## Common traps

- **Stale evaluation** — connections cache the producer's last output; if a node's evaluation order changes, invalidate the cache rather than reading the stale value.
- **`IDefaultInputPort`** holds a literal value that takes effect when no connection is present. It must serialise even when a connection *is* present, so reconnects restore the literal.
