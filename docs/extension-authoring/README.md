# Extension authoring guides

Extensions build against the `Beutl.Extensibility.Sdk` release that matches the Beutl version they target.

## Guides

- [Resolution-independent rendering](resolution-independent-rendering.md): read before implementing drawables,
  filter effects, brushes, shaders, or render nodes.
- [Tool tabs](tool-tabs.md): adding a dockable editor tool.
- [MCP tools](mcp-tools.md): giving AI agents tools through Beutl's live MCP endpoint.
- [File source properties](file-source-properties.md): file pickers for custom file-source and `FileInfo`
  properties.
- [Element recovery](element-recovery.md): completing repairs of recovered elements from a plugin.

## Migration guides

- [Avalonia 12](avalonia-12-migration.md): extensions built for Avalonia 11 and FluentAvalonia 2.
- [Audio effect latency](audio-effect-latency-migration.md): overrides of `AudioEffect.GetLatencySamples`.
- [Resource reconcile](resource-reconcile-migration.md): overrides of `EngineObject.Resource.Update`, the
  generated `PreUpdate`/`PostUpdate` hooks, and the `CompareAndUpdate` helpers.

Material and template packages for the store are described in [data packages](../data-packages.md).
