# Beutl.Extensibility.Sdk

```xml
<Project Sdk="Beutl.Extensibility.Sdk/1.0.0">
  <PropertyGroup>
    <PackageId>Beutl.Extensions.MyExtension</PackageId>
    <Title>My Extension</Title>
    <Description>拡張機能の説明</Description>
    <PackageTags>tag1,tag2</PackageTags>
    <Version>1.0.0</Version>
    <Authors>Your Name</Authors>
  </PropertyGroup>
</Project>
```

## Auto-referenced packages

By default the SDK references every Beutl package an extension typically needs. Each can be
opted out individually:

| Property | Package |
|---|---|
| `BeutlAutoReferenceExtensibility` | `Beutl.Extensibility` (full UI / media / property-editor surface) |
| `BeutlAutoReferenceExtensibilityAbstractions` | `Beutl.Extensibility.Abstractions` (base contracts only) |
| `BeutlAutoReferenceProjectSystem` | `Beutl.ProjectSystem` |
| `BeutlAutoReferenceNodeGraph` | `Beutl.NodeGraph` |
| `BeutlAutoReferenceEditor` | `Beutl.Editor` |
| `BeutlAutoReferenceSourceGenerators` | `Beutl.Engine.SourceGenerators` |

Set `BeutlAutoReferenceAll` to `false` to opt out of everything and then enable only what you need.

### Thin (abstractions-only) plugin

`Beutl.Extensibility.Abstractions` holds just the base extension contracts — `Extension`,
`ExtensionSettings`, and `ExportAttribute` — without pulling in Avalonia, SkiaSharp, or the rest
of the full `Beutl.Extensibility` surface. A plugin that only defines these contracts can drop the
heavy package and reference the abstractions instead:

```xml
<Project Sdk="Beutl.Extensibility.Sdk/1.0.0">
  <PropertyGroup>
    <PackageId>Beutl.Extensions.MyExtension</PackageId>
    <Version>1.0.0</Version>
    <Authors>Your Name</Authors>
    <!-- Drop the full assembly; keep the lightweight base contracts. -->
    <BeutlAutoReferenceExtensibility>false</BeutlAutoReferenceExtensibility>
  </PropertyGroup>
</Project>
```

When `BeutlAutoReferenceExtensibility` is left enabled, the abstractions arrive transitively, so
`BeutlAutoReferenceExtensibilityAbstractions` only adds a direct reference for the thin case above.
