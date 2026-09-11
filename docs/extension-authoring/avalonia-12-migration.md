# Migrating extensions to Avalonia 12

The Avalonia 12 migration also upgrades FluentAvaloniaUI to 3.1.0. This is a
breaking extension API change: binaries built against the Avalonia 11 /
FluentAvalonia 2 SDK must be rebuilt before they can run in the upgraded host.

## Icon contracts

Both public extension methods now return
`FluentAvalonia.UI.Controls.FAIconSource?`:

- `EditorExtension.GetIcon()` (abstract)
- `ToolWindowExtension.GetIcon()` (virtual)

Their previous return type was `FluentAvalonia.UI.Controls.IconSource?`.
The return type is part of the CLR method signature. An existing binary's
override does not implement the new contract, even if the method name and
namespace are unchanged. Loading exported types can fail and cause strict
package discovery to reject the entire package. Do not rely on the tool
window's default `null` icon as a compatibility fallback.

Update the override and any concrete icon sources. For example, an override
shared by either extension kind becomes:

```csharp
using FluentAvalonia.UI.Controls;

public override FAIconSource? GetIcon()
{
    return new FAFontIconSource { Glyph = "★" };
}
```

Other FluentAvalonia references in C# and XAML also need their new names,
such as `SymbolIconSource` to `FASymbolIconSource`, `PathIconSource` to
`FAPathIconSource`, and `Symbol` to `FASymbol`.

## Rebuild and publish

1. Target the `Beutl.Extensibility.Sdk` release that ships with the Avalonia 12
   host. If the project overrides `BeutlPackagesVersion`, update that value to
   the matching release too. Projects with explicit Beutl package references
   must update those references together.
2. Align direct UI dependencies with the host (`Avalonia` 12.1.2 and
   `FluentAvaloniaUI` 3.1.0 for this migration), update C# and XAML references,
   and rebuild the extension and its dependent assemblies. A C# alias alone
   cannot repair an already compiled binary.
3. Test the resulting package with the Avalonia 12 host: verify package
   discovery, editor creation, tool window creation, and icon display.
4. Publish a new extension package version and declare its supported Beutl
   release range in the package dependencies/release metadata. Keep the older
   package release for users on the Avalonia 11 host. Do not claim that a
   single unchanged binary supports both hosts.

There is no compatibility adapter for the old FluentAvalonia types. The
supported migration path is a rebuilt extension package for the new host.
The final Beutl release number is assigned by the release process; use that
release's SDK version rather than the repository's development version.

## PanelExtension

The host no longer references the `PanelExtension` package. Its
`HorizontalGridLayout` implementation is now `Beutl.Controls.HorizontalGridLayout`.
Extensions using that layout must reference `Beutl.Controls` from the matching
Beutl release and update their C# namespaces and XAML namespace mappings.
