# Data packages (materials and templates)

The Beutl store distributes three kinds of package. Two of them ship data rather than
code, and Beutl never loads an assembly from them:

| Kind | Reserved tag | Ships |
|---|---|---|
| Extension | *(none)* | assemblies loaded into the editor |
| Material | `material` | images, audio, video, fonts |
| Template | `template` | object templates (`.json`) |

## Declaring the kind

The kind lives in the package's tags, not in a field of its own. Add exactly one reserved
tag to the `.nuspec`:

```xml
<package>
  <metadata>
    <id>Contoso.Materials.CityPhotos</id>
    <version>1.0.0</version>
    <description>Royalty-free city photography.</description>
    <tags>material photography cc0</tags>
  </metadata>
</package>
```

A package that carries neither `material` nor `template` is an extension. One that carries
both is treated as a material. The reserved tags are set from the package type selector in
the developer portal, which also refuses them as hand-typed tags — do not add them through
the tag editor.

The two reserved tags are hidden wherever the store lists an author's tags; the package
page shows the kind instead.

## Content layout

Beutl copies one directory out of the package, chosen by the kind:

```
material package
  materials/**            ->  {home}/materials/{package-id}/

template package
  templates/**            ->  {home}/templates/{package-id}/
```

Both are copied recursively, so subdirectories are preserved. `{home}` is `$BEUTL_HOME`
when that directory exists, otherwise `~/.beutl`.

Pack the payload with `<files>` (or `contentFiles`), keeping the directory name at the
root of the package:

```xml
<files>
  <file src="assets\**\*" target="materials" />
</files>
```

Do **not** ship a `lib/` directory in a data package. Nothing in it is loaded, and its
presence only suggests otherwise.

## What Beutl does with the payload

- **Templates** — `ObjectTemplateService` watches `{home}/templates` recursively for
  `*.json` and registers what it finds, so a template package's contents appear in the
  editor without a restart. The per-package subdirectory is what keeps two packages from
  colliding on a file name.
- **Materials** — the library tab's **Materials** view lists everything under
  `{home}/materials`, grouped by the package that installed it. An item is dragged out as
  a plain file, which the player and the timeline already accept; whether a given file
  becomes an image, a sound or nothing at all is the drop target's decision.
- **Fonts** — `FontManager` scans `{home}/materials` alongside the configured font
  directories, so a font a material package ships is available to text elements. It builds
  its family list once per launch, so a font installed while Beutl is running appears after
  the next start.

## Updating and uninstalling

Installing replaces the package's payload directory wholesale, so a file dropped between
versions does not survive an update. Uninstalling removes
`{home}/materials/{package-id}` and `{home}/templates/{package-id}` along with the
extracted package.

## Listing by kind

`GET /api/v3/discover/search` and `GET /api/v3/discover/featured` take an optional `type`
parameter — `all` (the default), `extension`, `material`, or `template`. Omitting it lists
every kind, which is what older clients do.
