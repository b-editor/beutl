# File source properties

The built-in property editor provides a file picker for concrete reference types
that implement `Beutl.IO.IFileSource`, including custom `MediaSource` subclasses.
The type must have a public parameterless constructor. Selecting a file creates a
new instance and calls `ReadFrom(Uri)` with the selected file's absolute URI.
Clearing the path sets the property to `null`. Changes use the normal property
editing history and can be undone and redone, including edits to keyframes.
If construction or `ReadFrom` fails, the previous value is retained and an error
notification is shown.

Specify picker filters on the property with `FileFilterAttribute` from
`Beutl.Core`, available through the extension SDK:

```csharp
using Beutl;
using Beutl.Engine;

[FileFilter("Photoshop documents", "*.psd", "*.psb")]
public IProperty<PsdSource?> Source { get; } = Property.Create<PsdSource?>();
```

`PsdSource` should implement `IFileSource` or derive from `MediaSource` and
implement its resource behavior. Annotate the concrete source type with
`[JsonConverter(typeof(FileSourceJsonConverter))]`, or with
`[JsonConverter(typeof(MediaSourceJsonConverter))]` for a `MediaSource` subclass,
to use the existing relative-path serialization. Each selection gets a fresh
instance, so `ReadFrom` must initialize that instance rather than mutate another
shared source. Keeping the property as a file source allows the existing resource
relocation, missing-file validation, resource collection, and proxy-source
discovery to recognize it.

The same attribute works on `FileInfo` properties. Multiple attributes offer
multiple file types. Optional `MimeTypes` and `AppleUniformTypeIdentifiers` named
arguments supply platform-specific picker metadata:

```csharp
[FileFilter("Subtitles", "*.srt", MimeTypes = new[] { "application/x-subrip" })]
[FileFilter("Text files", "*.txt", AppleUniformTypeIdentifiers = new[] { "public.plain-text" })]
public IProperty<FileInfo?> SubtitleFile { get; } = Property.Create<FileInfo?>();
```

Without an attribute, the generic editor leaves the picker unfiltered. Existing
built-in source editors retain their specialized behavior and default filters;
`FileFilter` attributes override those filters when supplied. Filters constrain
the picker, so `ReadFrom` remains responsible for validating file contents.

List items also use the file editor. Their accessors do not inherit `FileFilter`
attributes from the containing list property, so their pickers are unfiltered.
