# Beutl.Engine — local context

This subtree is the **rendering / scene-graph core**. Nothing else in the solution may depend on the UI layer from here. Keep the dependency arrow pointing only toward `Beutl.Core`, `Beutl.Threading`, `Beutl.Configuration`, `Beutl.Language`.

## What lives here

- `Graphics/` — 2D pipeline, `IDrawable` chain, SKSurface ↔ `GraphicsContext`
- `Graphics/Rendering/` — `IRenderNode` tree built during rendering; nodes are **structurally immutable per frame** — do not mutate fields after a node is added to its parent in the same frame
- `Graphics3D/` — 3D pipeline, mesh / material / camera
- `Animation/` — `IAnimation<T>` / animators / easing; integer animators must use checked arithmetic (see `cd6c40b5b`)
- `Audio/` — sample-rate-agnostic audio graph
- `Composition/` — track / clip / element composition layered on top of Graphics
- `Engine/` — top-level scene container
- `Media/Proxy/` — MIT-side proxy metadata, resolver, queue, eviction, and value types. Concrete FFmpeg generation stays outside Engine.

## Mandatory rules in this subtree

1. **No UI references.** `using Avalonia.*` or any `Beutl.Editor*` / `Beutl.Controls*` symbol here is a build break. The project file enforces this via `ProjectReference` shape; do not loosen it.
2. **Source generators run from `Beutl.Engine.SourceGenerators`** as an analyzer reference. Changes that touch generated APIs (e.g. `CoreProperty` registration) must be exercised by `tests/SourceGeneratorTest/` and `tests/Beutl.UnitTests/` together — coverage on one side is not enough.
3. **`InternalsVisibleTo`** is granted to `Beutl`, `Beutl.UnitTests`, `Beutl.Graphics3DTests`. Do not add more without a discussion.
4. **`FilterEffect` / `Drawable` authoring** has dedicated guides. When the user describes a new effect or drawable, prefer the `beutl-filter-effect` / `beutl-drawable` skills over hand-rolled implementations.
5. **Avoid allocations on the render hot path.** Prefer `stackalloc` / pooled arrays in render-node `Render(...)` and `Hit(...)` implementations. Profile before optimising past that.
6. **Proxy media stays on the MIT side.** `Media/Proxy/` may expose Engine abstractions such as `IProxyGenerator`, but it must not reference `Beutl.Extensions.FFmpeg`, `Beutl.FFmpegWorker`, or FFmpeg IPC implementation types.

## Common traps

- **Integer animator overflow** — interpolating `int` / `long` between distant keyframes can overflow. Always go through the checked helpers in `Animation/Animators/`; see `cd6c40b5b` for the regression test pattern.
- **`IRenderNode.Equals` semantics** — render-graph diffing relies on structural equality. If you add fields to a node, override `Equals` and `GetHashCode` consistently or diffing will silently skip updates.
- **Disposal ordering** — `GraphicsContext` owns SKSurface lifetime. Returning a context to a caller that outlives the render pass leaks GPU memory.
