---
description: |
  Generate a Beutl project for manual testing. Creates a temporary console app that produces a
  project with shape and image elements for every built-in filter effect, ready to open in the
  Beutl app. Nothing is committed to git — the generator lives in a temp directory and is cleaned
  up after use. Use when the user asks to "create a test project", "generate test data",
  "テスト用プロジェクトを作成", "手動テスト用のプロジェクト", or needs a visual smoke-test of the
  rendering pipeline.
allowed-tools: Bash(dotnet build:*) Bash(dotnet run:*) Bash(dotnet restore:*) Bash(ls:*) Bash(mkdir:*) Bash(rm:*) Bash(cat:*) Read Edit Write
argument-hint: "[output-directory]"
---

# Generate a Beutl test project (on-the-fly code generation)

This skill creates a **throwaway** console app in a temp directory, builds and runs it to
produce a Beutl project, then cleans up. No files are committed to git.

## Steps

1. **Determine output directory.** If `$ARGUMENTS` is provided, use it as the output path.
   Otherwise ask the user via AskUserQuestion (default: `~/Desktop/BeutlTestProject`).

2. **Create a temp directory** for the generator code:

   ```bash
   GENDIR=$(mktemp -d) && echo "$GENDIR"
   ```

3. **Write the .csproj** to `$GENDIR/TestProjectGenerator.csproj` with the content from
   the [csproj template](#csproj-template) below.

4. **Write Program.cs** to `$GENDIR/Program.cs` with the content from the
   [Program.cs template](#programcs-template) below.
   - If the user requested specific effects only, omit unwanted entries from `CreateEffects()`.
   - If the user requested a different resolution, change the `new Scene(width, height, ...)` call.
   - If the user requested additional drawables (TextBlock, RoundedRectShape, etc.), add them
     to `CreateShape()`.

5. **Build:**

   ```bash
   dotnet build "$GENDIR/TestProjectGenerator.csproj" -f net10.0 -c Debug
   ```

6. **Run:**

   ```bash
   dotnet run --project "$GENDIR/TestProjectGenerator.csproj" -f net10.0 -- "<output-directory>"
   ```

7. **Clean up** the temp directory:

   ```bash
   rm -rf "$GENDIR"
   ```

8. Report the output path and element count to the user.

---

## csproj template

The project references are **relative to the repo root**. Adjust the `Include` paths based on
where `$GENDIR` is located (use absolute paths if the temp dir is outside the repo).

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="REPO_ROOT/src/Beutl.Engine/Beutl.Engine.csproj" />
    <ProjectReference Include="REPO_ROOT/src/Beutl.ProjectSystem/Beutl.ProjectSystem.csproj" />
    <ProjectReference Include="REPO_ROOT/src/Beutl.Configuration/Beutl.Configuration.csproj" />
    <ProjectReference Include="REPO_ROOT/src/Beutl.Engine.SourceGenerators/Beutl.Engine.SourceGenerators.csproj"
                       OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
  </ItemGroup>
</Project>
```

Replace `REPO_ROOT` with the **absolute path** to the Beutl repository root.

---

## Program.cs template

```csharp
using Beutl;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using SkiaSharp;

string outputDir = args.Length > 0
    ? args[0]
    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "BeutlTestProject");

Directory.CreateDirectory(outputDir);
string projDirName = Path.GetFileName(outputDir);

string imagePath = Path.Combine(outputDir, "test-image.png");
GenerateTestImage(imagePath, 640, 480);

var app = BeutlApplication.Current;

var effects = CreateEffects();

string projPath = Path.Combine(outputDir, $"{projDirName}.bep");
string sceneName = "EffectsTest";
string sceneDirPath = Path.Combine(outputDir, sceneName);
Directory.CreateDirectory(sceneDirPath);

string sceneFilePath = Path.Combine(sceneDirPath, $"{sceneName}.scene");

var project = new Project { Uri = new Uri(projPath) };
var scene = new Scene(1920, 1080, sceneName)
{
    Uri = new Uri(sceneFilePath),
    Duration = TimeSpan.FromSeconds(effects.Count * 10),
};

project.Items.Add(scene);
app.Project = project;

int index = 0;
foreach (var (name, shapeEffect, imageEffect) in effects)
{
    string elementDir = Path.Combine(sceneDirPath, $"{index:D3}-{name}");
    Directory.CreateDirectory(elementDir);

    int baseSeconds = index * 10;

    // --- Shape element (first 5 seconds) ---
    var shapeElement = new Element
    {
        Start = TimeSpan.FromSeconds(baseSeconds),
        Length = TimeSpan.FromSeconds(5),
        IsEnabled = true,
        ZIndex = index * 2,
        Uri = new Uri(Path.Combine(elementDir, "shape.belm")),
    };

    var shape = CreateShape(index);
    shape.FilterEffect.CurrentValue = shapeEffect;
    shapeElement.AddObject(shape);
    scene.AddChild(shapeElement);

    // --- Image element (next 5 seconds) ---
    var imageElement = new Element
    {
        Start = TimeSpan.FromSeconds(baseSeconds + 5),
        Length = TimeSpan.FromSeconds(5),
        IsEnabled = true,
        ZIndex = index * 2 + 1,
        Uri = new Uri(Path.Combine(elementDir, "image.belm")),
    };

    var sourceImage = new SourceImage();
    var imgSource = new ImageSource();
    imgSource.ReadFrom(new Uri(imagePath));
    sourceImage.Source.CurrentValue = imgSource;
    sourceImage.FilterEffect.CurrentValue = imageEffect;
    imageElement.AddObject(sourceImage);
    scene.AddChild(imageElement);

    index++;
    Console.WriteLine($"[{index:D2}/{effects.Count}] {name}");
}

CoreSerializer.StoreToUri(
    project,
    project.Uri,
    CoreSerializationMode.Write | CoreSerializationMode.SaveReferencedObjects);

Console.WriteLine();
Console.WriteLine($"Project saved to: {projPath}");
Console.WriteLine($"  Scene: {sceneFilePath}");
Console.WriteLine($"  Effects: {effects.Count}");
Console.WriteLine($"  Elements: {effects.Count * 2} (shape + image per effect)");

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

static Drawable CreateShape(int index)
{
    if (index % 3 == 0)
    {
        var rect = new RectShape();
        rect.Width.CurrentValue = 400;
        rect.Height.CurrentValue = 300;
        rect.Fill.CurrentValue = new SolidColorBrush(Colors.DodgerBlue);
        return rect;
    }

    if (index % 3 == 1)
    {
        var ellipse = new EllipseShape();
        ellipse.Width.CurrentValue = 350;
        ellipse.Height.CurrentValue = 350;
        ellipse.Fill.CurrentValue = new SolidColorBrush(Colors.Coral);
        return ellipse;
    }

    {
        var rect = new RectShape();
        rect.Width.CurrentValue = 300;
        rect.Height.CurrentValue = 400;
        rect.Fill.CurrentValue = new SolidColorBrush(Colors.MediumSeaGreen);
        var pen = new Pen();
        pen.Brush.CurrentValue = new SolidColorBrush(Colors.DarkGreen);
        pen.Thickness.CurrentValue = 4;
        rect.Pen.CurrentValue = pen;
        return rect;
    }
}

static List<(string Name, FilterEffect ShapeEffect, FilterEffect ImageEffect)> CreateEffects()
{
    var list = new List<(string, FilterEffect, FilterEffect)>();

    list.Add(("Blur", CreateBlur(), CreateBlur()));
    list.Add(("Brightness", CreateBrightness(), CreateBrightness()));
    list.Add(("Gamma", CreateGamma(), CreateGamma()));
    list.Add(("Saturate", CreateSaturate(), CreateSaturate()));
    list.Add(("Invert", CreateInvert(), CreateInvert()));
    list.Add(("HueRotate", CreateHueRotate(), CreateHueRotate()));
    list.Add(("HighContrast", CreateHighContrast(), CreateHighContrast()));
    list.Add(("LumaColor", CreateLumaColor(), CreateLumaColor()));
    list.Add(("Threshold", CreateThreshold(), CreateThreshold()));
    list.Add(("Dilate", CreateDilate(), CreateDilate()));
    list.Add(("Erode", CreateErode(), CreateErode()));
    list.Add(("DropShadow", CreateDropShadow(), CreateDropShadow()));
    list.Add(("InnerShadow", CreateInnerShadow(), CreateInnerShadow()));
    list.Add(("FlatShadow", CreateFlatShadow(), CreateFlatShadow()));
    list.Add(("Lighting", CreateLighting(), CreateLighting()));
    list.Add(("Clipping", CreateClipping(), CreateClipping()));
    list.Add(("ColorShift", CreateColorShift(), CreateColorShift()));
    list.Add(("MosaicEffect", CreateMosaic(), CreateMosaic()));
    list.Add(("Negaposi", CreateNegaposi(), CreateNegaposi()));
    list.Add(("SplitEffect", CreateSplit(), CreateSplit()));
    list.Add(("ShakeEffect", CreateShake(), CreateShake()));
    list.Add(("StrokeEffect", CreateStroke(), CreateStroke()));
    list.Add(("TransformEffect", CreateTransformEffect(), CreateTransformEffect()));
    list.Add(("PixelSortEffect", CreatePixelSort(), CreatePixelSort()));
    list.Add(("ChromaKey", CreateChromaKey(), CreateChromaKey()));
    list.Add(("ColorKey", CreateColorKey(), CreateColorKey()));
    list.Add(("BlendEffect", CreateBlend(), CreateBlend()));
    list.Add(("ColorGrading", CreateColorGrading(), CreateColorGrading()));

    return list;
}

static Blur CreateBlur()
{
    var e = new Blur();
    e.Sigma.CurrentValue = new Size(8, 8);
    return e;
}

static Brightness CreateBrightness()
{
    var e = new Brightness();
    e.Amount.CurrentValue = 150;
    return e;
}

static Gamma CreateGamma()
{
    var e = new Gamma();
    e.Amount.CurrentValue = 200;
    return e;
}

static Saturate CreateSaturate()
{
    var e = new Saturate();
    e.Amount.CurrentValue = 200;
    return e;
}

static Invert CreateInvert()
{
    var e = new Invert();
    e.Amount.CurrentValue = 100;
    return e;
}

static HueRotate CreateHueRotate()
{
    var e = new HueRotate();
    e.Angle.CurrentValue = 90;
    return e;
}

static HighContrast CreateHighContrast()
{
    var e = new HighContrast();
    e.Contrast.CurrentValue = 50;
    return e;
}

static LumaColor CreateLumaColor()
{
    return new LumaColor();
}

static Threshold CreateThreshold()
{
    var e = new Threshold();
    e.Value.CurrentValue = 40;
    e.Strength.CurrentValue = 100;
    return e;
}

static Dilate CreateDilate()
{
    var e = new Dilate();
    e.RadiusX.CurrentValue = 5;
    e.RadiusY.CurrentValue = 5;
    return e;
}

static Erode CreateErode()
{
    var e = new Erode();
    e.RadiusX.CurrentValue = 3;
    e.RadiusY.CurrentValue = 3;
    return e;
}

static DropShadow CreateDropShadow()
{
    var e = new DropShadow();
    e.Position.CurrentValue = new Point(10, 10);
    e.Sigma.CurrentValue = new Size(6, 6);
    e.Color.CurrentValue = new Color(180, 0, 0, 0);
    return e;
}

static InnerShadow CreateInnerShadow()
{
    var e = new InnerShadow();
    e.Position.CurrentValue = new Point(5, 5);
    e.Sigma.CurrentValue = new Size(4, 4);
    e.Color.CurrentValue = new Color(200, 0, 0, 0);
    return e;
}

static FlatShadow CreateFlatShadow()
{
    var e = new FlatShadow();
    e.Angle.CurrentValue = 45;
    e.Length.CurrentValue = 30;
    return e;
}

static Lighting CreateLighting()
{
    var e = new Lighting();
    e.Multiply.CurrentValue = new Color(255, 200, 200, 255);
    e.Add.CurrentValue = new Color(255, 30, 30, 30);
    return e;
}

static Clipping CreateClipping()
{
    var e = new Clipping();
    e.Left.CurrentValue = 20;
    e.Top.CurrentValue = 20;
    e.Right.CurrentValue = 20;
    e.Bottom.CurrentValue = 20;
    return e;
}

static ColorShift CreateColorShift()
{
    var e = new ColorShift();
    e.RedOffset.CurrentValue = new PixelPoint(5, 0);
    e.GreenOffset.CurrentValue = new PixelPoint(0, 0);
    e.BlueOffset.CurrentValue = new PixelPoint(-5, 0);
    return e;
}

static MosaicEffect CreateMosaic()
{
    var e = new MosaicEffect();
    e.TileSize.CurrentValue = new Size(20, 20);
    return e;
}

static Negaposi CreateNegaposi()
{
    var e = new Negaposi();
    e.Red.CurrentValue = 255;
    e.Green.CurrentValue = 255;
    e.Blue.CurrentValue = 255;
    e.Strength.CurrentValue = 100;
    return e;
}

static SplitEffect CreateSplit()
{
    var e = new SplitEffect();
    e.HorizontalDivisions.CurrentValue = 3;
    e.VerticalDivisions.CurrentValue = 3;
    e.HorizontalSpacing.CurrentValue = 10;
    e.VerticalSpacing.CurrentValue = 10;
    return e;
}

static ShakeEffect CreateShake()
{
    var e = new ShakeEffect();
    e.StrengthX.CurrentValue = 20;
    e.StrengthY.CurrentValue = 20;
    return e;
}

static StrokeEffect CreateStroke()
{
    var e = new StrokeEffect();
    var pen = new Pen();
    pen.Brush.CurrentValue = new SolidColorBrush(Colors.Red);
    pen.Thickness.CurrentValue = 6;
    e.Pen.CurrentValue = pen;
    e.Offset.CurrentValue = new Point(0, 0);
    return e;
}

static TransformEffect CreateTransformEffect()
{
    var e = new TransformEffect();
    var transform = new RotationTransform();
    transform.Rotation.CurrentValue = 15;
    e.Transform.CurrentValue = transform;
    return e;
}

static PixelSortEffect CreatePixelSort()
{
    var e = new PixelSortEffect();
    e.ThresholdMin.CurrentValue = 20;
    e.ThresholdMax.CurrentValue = 80;
    return e;
}

static ChromaKey CreateChromaKey()
{
    var e = new ChromaKey();
    e.Color.CurrentValue = Colors.Green;
    e.HueRange.CurrentValue = 40;
    e.SaturationRange.CurrentValue = 50;
    return e;
}

static ColorKey CreateColorKey()
{
    var e = new ColorKey();
    e.Color.CurrentValue = Colors.Green;
    e.Range.CurrentValue = 50;
    return e;
}

static BlendEffect CreateBlend()
{
    var e = new BlendEffect();
    e.Brush.CurrentValue = new SolidColorBrush(new Color(128, 255, 200, 100));
    e.BlendMode.CurrentValue = BlendMode.Overlay;
    return e;
}

static ColorGrading CreateColorGrading()
{
    var e = new ColorGrading();
    e.Exposure.CurrentValue = 20;
    e.Contrast.CurrentValue = 20;
    e.Saturation.CurrentValue = 30;
    e.Temperature.CurrentValue = 15;
    return e;
}

static void GenerateTestImage(string path, int width, int height)
{
    using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
    var canvas = surface.Canvas;

    canvas.Clear(SKColors.White);

    using var bgPaint = new SKPaint();
    bgPaint.IsAntialias = true;

    bgPaint.Shader = SKShader.CreateLinearGradient(
        new SKPoint(0, 0), new SKPoint(width, height),
        [SKColors.SkyBlue, SKColors.Orange, SKColors.MediumPurple],
        [0f, 0.5f, 1f],
        SKShaderTileMode.Clamp);
    canvas.DrawRect(0, 0, width, height, bgPaint);

    using var shapePaint = new SKPaint { IsAntialias = true };

    shapePaint.Color = new SKColor(255, 255, 255, 180);
    canvas.DrawCircle(width * 0.25f, height * 0.4f, 80, shapePaint);

    shapePaint.Color = new SKColor(255, 80, 80, 200);
    canvas.DrawRect(width * 0.5f, height * 0.2f, 160, 120, shapePaint);

    shapePaint.Color = new SKColor(80, 200, 80, 200);
    canvas.DrawRoundRect(width * 0.1f, height * 0.6f, 200, 100, 15, 15, shapePaint);

    shapePaint.Color = new SKColor(80, 80, 255, 200);
    canvas.DrawCircle(width * 0.7f, height * 0.7f, 60, shapePaint);

    using var textFont = new SKFont { Size = 36 };
    using var textPaint = new SKPaint
    {
        IsAntialias = true,
        Color = SKColors.White,
    };
    canvas.DrawText("Beutl Test Image", width * 0.2f, height * 0.15f, SKTextAlign.Left, textFont, textPaint);

    // Green patch for chroma-key testing
    shapePaint.Color = new SKColor(0, 200, 0, 255);
    canvas.DrawRect(width * 0.75f, height * 0.1f, 120, 100, shapePaint);

    // Grid lines for displacement / mosaic / split visual feedback
    using var linePaint = new SKPaint
    {
        IsAntialias = false,
        Color = new SKColor(0, 0, 0, 60),
        StrokeWidth = 1,
        Style = SKPaintStyle.Stroke,
    };
    for (int x = 0; x < width; x += 40)
        canvas.DrawLine(x, 0, x, height, linePaint);
    for (int y = 0; y < height; y += 40)
        canvas.DrawLine(0, y, width, y, linePaint);

    using var image = surface.Snapshot();
    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
    using var stream = File.Create(path);
    data.SaveTo(stream);

    Console.WriteLine($"Test image saved: {path} ({width}x{height})");
}
```

---

## Beutl project-system API reference (for customization)

This section documents the key APIs so you can modify the templates without re-reading the
source. Check the source if the API has changed since this was written.

### Project hierarchy

```
Project (.bep)
  └── Scene (.scene) — new Scene(width, height, name)
        └── Element (.belm) — scene.AddChild(element)
              └── EngineObject — element.AddObject(drawable)
```

- `Project.Uri`, `Scene.Uri`, `Element.Uri` — set to absolute file paths (`new Uri(path)`)
- `CoreSerializer.StoreToUri(project, project.Uri, CoreSerializationMode.Write | CoreSerializationMode.SaveReferencedObjects)` — saves all files

### Drawables (in `Beutl.Graphics.Shapes` / `Beutl.Graphics`)

| Class | Key properties |
|---|---|
| `RectShape` | `Width` (float, def 100), `Height` (float, def 100), `Fill` (Brush?), `Pen` (Pen?) |
| `EllipseShape` | `Width`, `Height`, `Fill`, `Pen` |
| `TextBlock` | `Text` (string), `Size` (float, def 12), `FontFamily`, `Fill` |
| `SourceImage` | `Source` (ImageSource?) — call `imgSource.ReadFrom(new Uri(path))` |
| `GeometryShape` | `Geometry` (Geometry?) |
| `RoundedRectShape` | `Width`, `Height`, `CornerRadius` |

All drawables inherit from `Drawable`:
- `FilterEffect` (FilterEffect?) — assign effect here
- `Opacity` (float, 0-100)
- `BlendMode` (BlendMode)
- `Transform` (Transform?)

### Property access

Properties are `IProperty<T>`. Set values via `.CurrentValue`:
```csharp
shape.Width.CurrentValue = 400;
shape.Fill.CurrentValue = new SolidColorBrush(Colors.Red);
```

### Filter effects (in `Beutl.Graphics.Effects`)

| Effect | Key properties | Visible-at-default? |
|---|---|---|
| `Blur` | `Sigma` (Size) | No — set non-zero |
| `Brightness` | `Amount` (float, def 100) | No — 100 = no change |
| `Gamma` | `Amount` (float, def 100), `Strength` (float, def 100) | No |
| `Saturate` | `Amount` (float, def 100) | No |
| `Invert` | `Amount` (float, def 100) | Yes |
| `HueRotate` | `Angle` (float) | No — 0 = no change |
| `HighContrast` | `Contrast` (float), `Grayscale` (bool), `InvertStyle` (enum) | No |
| `LumaColor` | (none) | Yes |
| `Threshold` | `Value` (float, def 50), `Smoothness` (float), `Strength` (float, def 100) | Yes |
| `Dilate` | `RadiusX` (float), `RadiusY` (float) | No — 0 = no change |
| `Erode` | `RadiusX` (float), `RadiusY` (float) | No |
| `DropShadow` | `Position` (Point), `Sigma` (Size), `Color` (Color), `ShadowOnly` (bool) | No |
| `InnerShadow` | `Position` (Point), `Sigma` (Size), `Color` (Color), `ShadowOnly` (bool) | No |
| `FlatShadow` | `Angle` (float), `Length` (float), `Brush` (Brush?, def Gray), `ShadowOnly` (bool) | No |
| `Lighting` | `Multiply` (Color, def White), `Add` (Color) | No |
| `Clipping` | `Left`, `Top`, `Right`, `Bottom` (float), `AutoCenter`, `AutoClip` (bool) | No |
| `ColorShift` | `RedOffset`, `GreenOffset`, `BlueOffset`, `AlphaOffset` (PixelPoint) | No |
| `MosaicEffect` | `TileSize` (Size, def 10,10), `Origin` (RelativePoint) | Yes |
| `Negaposi` | `Red` (int), `Green` (int), `Blue` (int), `Strength` (float, def 100) | Yes at def |
| `SplitEffect` | `HorizontalDivisions` (int, def 2), `VerticalDivisions` (int, def 2), `HorizontalSpacing` (float), `VerticalSpacing` (float) | Need spacing > 0 |
| `ShakeEffect` | `StrengthX` (float, def 50), `StrengthY` (float, def 50) | Yes |
| `StrokeEffect` | `Pen` (Pen?), `Offset` (Point), `Style` (StrokeStyles enum) | Need Pen set |
| `TransformEffect` | `Transform` (Transform?), `TransformOrigin` (RelativePoint), `BitmapInterpolationMode` | Need Transform set |
| `PixelSortEffect` | `ThresholdMin` (float, def 25), `ThresholdMax` (float, def 80), `Direction`, `SortKey`, `Ascending` | Yes |
| `ChromaKey` | `Color` (Color), `HueRange` (float), `SaturationRange` (float), `Boundary` (float, def 2) | Need Color set |
| `ColorKey` | `Color` (Color), `Range` (float), `Boundary` (float, def 2) | Need Color set |
| `BlendEffect` | `Brush` (Brush?), `BlendMode` (BlendMode, def SrcIn) | Need Brush set |
| `ColorGrading` | `Exposure`, `Contrast`, `ContrastPivot`, `Saturation`, `Vibrance`, `Hue`, `Temperature`, `Tint` (all float) | No |
| `FilterEffectGroup` | `Children` (list of FilterEffect) | Container |

### Common types

- `new SolidColorBrush(Colors.Red)` — solid fill
- `new Pen()` + `pen.Brush.CurrentValue = ...; pen.Thickness.CurrentValue = 6;`
- `new RotationTransform()` + `t.Rotation.CurrentValue = 15;` (in `Beutl.Graphics.Transformation`)
- `new Color(a, r, g, b)` — 0-255 each
- `new Size(w, h)`, `new Point(x, y)`, `new PixelPoint(x, y)`
- `Colors.Red`, `Colors.Green`, etc.
- `UriHelper` is **internal** — use `new Uri(absolutePath)` instead

### Gotchas

- Each effect instance can only be used on ONE drawable. Create separate instances for shape
  and image elements (the template calls each factory twice).
- `ImageSource.ReadFrom(uri)` takes an absolute `Uri`, not a relative path.
- The `Beutl.Serialization` namespace is needed for `CoreSerializer` and `CoreSerializationMode`.
