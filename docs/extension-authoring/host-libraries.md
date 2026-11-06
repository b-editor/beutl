# Libraries Beutl ships

An extension that uses a library Beutl also ships has to run on Beutl's copy. Beutl loads its own copy when it is at
least the version the extension asked for. A newer version loads separately, and the same type then exists twice.
The library's native files, such as libSkiaSharp, would also end up in the extension's folder.

`BeutlHostPackageReference` takes care of both:

```xml
<ItemGroup>
  <BeutlHostPackageReference Include="FluentIcons.Avalonia.Fluent" />
</ItemGroup>
```

It becomes a `PackageReference` at the version this SDK's Beutl ships, with `ExcludeAssets="runtime;native"` and
`PrivateAssets="all"`, so the extension compiles against the library and nothing of it goes into its output or package.

A name Beutl does not ship stops the restore with an error. Reference such a library with a normal `PackageReference`,
as it belongs to the extension.

The libraries that can be named are the ones in Beutl's `Directory.Packages.props`, without its build and test tools
and without what only the FFmpeg worker process loads.
