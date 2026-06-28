using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.NodeGraph;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.Services;

namespace Beutl.AgentToolkit.Schema;

public sealed class SchemaGenerator
{
    private static readonly string[] s_formats =
    [
        KnownLibraryItemFormats.Drawable,
        KnownLibraryItemFormats.Sound,
        KnownLibraryItemFormats.FilterEffect,
        KnownLibraryItemFormats.AudioEffect,
        KnownLibraryItemFormats.Brush,
        KnownLibraryItemFormats.Transform,
        KnownLibraryItemFormats.Geometry,
        KnownLibraryItemFormats.Pen,
        KnownLibraryItemFormats.Easing,
        KnownLibraryItemFormats.GraphNode,
        KnownLibraryItemFormats.EngineObject
    ];

    private static readonly Lazy<ExampleSpec[]> s_exampleSpecs = new(CreateExampleSpecs);
    private static readonly Lazy<EffectRecipeSpec[]> s_effectRecipeSpecs = new(CreateEffectRecipeSpecs);
    private static readonly Dictionary<Type, EffectMetadata> s_effectMetadata = CreateEffectMetadata();

    public CapabilitySchema Generate(
        string? typeFilter = null,
        string? categoryFilter = null,
        bool includeProperties = true,
        bool includeExamples = true)
    {
        TypeRegistration.EnsureRegistered();

        List<TypeDescriptor> types = [];
        foreach ((string category, Type type) in EnumerateRegisteredTypes().DistinctBy(item => (item.Category, item.Type)))
        {
            string discriminator = IdentityHelper.WriteDiscriminator(type);
            if (!Matches(typeFilter, type, discriminator) || !MatchesCategory(categoryFilter, category))
            {
                continue;
            }

            types.Add(CreateDescriptor(category, type, discriminator, includeProperties));
        }

        return new CapabilitySchema(
            SchemaVersion.Current,
            types.OrderBy(type => type.Type, StringComparer.Ordinal).ToArray(),
            includeExamples ? CreateExamples(typeFilter, categoryFilter) : []);
    }

    public bool ContainsType(string typeOrDiscriminator)
    {
        return Generate(typeFilter: typeOrDiscriminator).Types.Count > 0;
    }

    private static IEnumerable<(string Category, Type Type)> EnumerateRegisteredTypes()
    {
        foreach (string format in s_formats)
        {
            foreach (Type type in LibraryService.Current.GetTypesFromFormat(format))
            {
                yield return (format, type);
            }
        }

        foreach ((string category, Type type) in EnumerateLibraryItems(LibraryService.Current.Items))
        {
            yield return (category, type);
        }
    }

    private static IEnumerable<(string Category, Type Type)> EnumerateLibraryItems(IEnumerable<LibraryItem> items)
    {
        foreach (LibraryItem item in items)
        {
            if (item is SingleTypeLibraryItem single)
            {
                yield return (single.Format, single.ImplementationType);
            }
            else if (item is MultipleTypeLibraryItem multiple)
            {
                foreach ((string format, Type type) in multiple.Types)
                {
                    yield return (format, type);
                }
            }
            else if (item is GroupLibraryItem group)
            {
                foreach ((string category, Type type) in EnumerateLibraryItems(group.Items))
                {
                    yield return (category, type);
                }
            }
        }
    }

    public IReadOnlyList<DeclarativeExample> GenerateExamples(
        string? typeFilter = null,
        string? categoryFilter = null,
        string? nameFilter = null)
    {
        TypeRegistration.EnsureRegistered();
        return CreateExamples(typeFilter, categoryFilter, nameFilter);
    }

    public IReadOnlyList<DeclarativeExampleSummary> ListExamples(string? typeFilter = null, string? categoryFilter = null)
    {
        TypeRegistration.EnsureRegistered();
        return s_exampleSpecs.Value
            .Where(spec => ExampleMatches(spec, typeFilter, categoryFilter, nameFilter: null))
            .Select(spec => new DeclarativeExampleSummary(
                spec.Example.Name,
                spec.Example.Description,
                spec.Categories.ToArray(),
                spec.Tags.ToArray()))
            .ToArray();
    }

    public IReadOnlyList<EffectSummary> ListEffects(string? intent = null, bool includePropertyNames = true)
    {
        TypeRegistration.EnsureRegistered();
        return EnumerateRegisteredTypes()
            .Where(item => MatchesCategory(KnownLibraryItemFormats.FilterEffect, item.Category))
            .Select(item => item.Type)
            .Distinct()
            .Select(type => CreateEffectSummary(type, includePropertyNames))
            .Where(summary => MatchesIntent(summary, intent))
            .OrderBy(summary => summary.Name, StringComparer.Ordinal)
            .ToArray();
    }

    public IReadOnlyList<EffectRecipeSummary> ListEffectRecipes(string? intent = null)
    {
        TypeRegistration.EnsureRegistered();
        return s_effectRecipeSpecs.Value
            .Where(spec => MatchesIntent(spec.Summary.IntentTags, intent)
                           || string.IsNullOrWhiteSpace(intent)
                           || spec.Summary.EffectNames.Any(name => name.Contains(intent, StringComparison.OrdinalIgnoreCase)))
            .Select(spec => spec.Summary)
            .OrderBy(summary => summary.Name, StringComparer.Ordinal)
            .ToArray();
    }

    public EffectRecipe GetEffectRecipe(string? name = null, string? intent = null)
    {
        TypeRegistration.EnsureRegistered();
        EffectRecipeSpec? spec = !string.IsNullOrWhiteSpace(name)
            ? s_effectRecipeSpecs.Value.FirstOrDefault(item => string.Equals(item.Summary.Name, name, StringComparison.OrdinalIgnoreCase))
            : s_effectRecipeSpecs.Value.FirstOrDefault(item => MatchesIntent(item.Summary.IntentTags, intent));

        if (spec is null)
        {
            throw new ReconcileException(new ToolError(
                ErrorCode.UnknownType,
                $"No effect recipe matched name='{name}' intent='{intent}'.",
                name ?? intent,
                "Call list_effect_recipes to inspect available recipe names and intent tags."));
        }

        return new EffectRecipe(
            spec.Summary.Name,
            spec.Summary.Description,
            spec.Summary.IntentTags.ToArray(),
            spec.Summary.EffectNames.ToArray(),
            spec.Summary.Notes.ToArray(),
            (JsonObject)spec.Patch.DeepClone());
    }

    private static EffectSummary CreateEffectSummary(Type type, bool includePropertyNames)
    {
        string discriminator = IdentityHelper.WriteDiscriminator(type);
        TypeDescriptor descriptor = CreateDescriptor(KnownLibraryItemFormats.FilterEffect, type, discriminator, includeProperties: includePropertyNames);
        EffectMetadata metadata = GetEffectMetadata(type);
        return new EffectSummary(
            type.Name,
            descriptor.Type,
            descriptor.Discriminator,
            descriptor.DisplayName,
            descriptor.Description,
            metadata.IntentTags.ToArray(),
            includePropertyNames
                ? descriptor.Properties.Select(property => property.Name).ToArray()
                : [],
            metadata.Notes.ToArray(),
            metadata.RequiresGpu);
    }

    private static EffectMetadata GetEffectMetadata(Type type)
    {
        if (s_effectMetadata.TryGetValue(type, out EffectMetadata? metadata))
        {
            return metadata;
        }

        return new EffectMetadata(InferEffectTags(type.Name), [], RequiresGpu: false);
    }

    private static string[] InferEffectTags(string name)
    {
        string lower = name.ToLowerInvariant();
        List<string> tags = ["effect"];
        if (lower.Contains("blur", StringComparison.Ordinal)
            || lower.Contains("shadow", StringComparison.Ordinal)
            || lower.Contains("stroke", StringComparison.Ordinal))
        {
            tags.AddRange(["glow", "depth", "outline"]);
        }

        if (lower.Contains("color", StringComparison.Ordinal)
            || lower.Contains("hue", StringComparison.Ordinal)
            || lower.Contains("saturate", StringComparison.Ordinal)
            || lower.Contains("brightness", StringComparison.Ordinal)
            || lower.Contains("contrast", StringComparison.Ordinal)
            || lower.Contains("gamma", StringComparison.Ordinal)
            || lower.Contains("threshold", StringComparison.Ordinal)
            || lower.Contains("invert", StringComparison.Ordinal)
            || lower.Contains("curve", StringComparison.Ordinal)
            || lower.Contains("luma", StringComparison.Ordinal))
        {
            tags.AddRange(["color", "grade"]);
        }

        if (lower.Contains("mosaic", StringComparison.Ordinal)
            || lower.Contains("pixel", StringComparison.Ordinal)
            || lower.Contains("shift", StringComparison.Ordinal)
            || lower.Contains("shake", StringComparison.Ordinal)
            || lower.Contains("split", StringComparison.Ordinal))
        {
            tags.AddRange(["glitch", "stylize"]);
        }

        if (lower.Contains("key", StringComparison.Ordinal))
        {
            tags.AddRange(["keying", "transparent"]);
        }

        if (lower.Contains("transform", StringComparison.Ordinal)
            || lower.Contains("displacement", StringComparison.Ordinal)
            || lower.Contains("path", StringComparison.Ordinal)
            || lower.Contains("delay", StringComparison.Ordinal)
            || lower.Contains("layer", StringComparison.Ordinal)
            || lower.Contains("blend", StringComparison.Ordinal))
        {
            tags.AddRange(["motion", "composite"]);
        }

        if (lower.Contains("script", StringComparison.Ordinal)
            || lower.Contains("nodegraph", StringComparison.Ordinal))
        {
            tags.AddRange(["advanced", "programmable"]);
        }

        return tags.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static bool MatchesIntent(EffectSummary summary, string? intent)
    {
        return MatchesIntent(summary.IntentTags, intent)
               || (!string.IsNullOrWhiteSpace(intent)
                   && (summary.Name.Contains(intent, StringComparison.OrdinalIgnoreCase)
                       || summary.DisplayName?.Contains(intent, StringComparison.OrdinalIgnoreCase) == true));
    }

    private static bool MatchesIntent(IReadOnlyList<string> tags, string? intent)
    {
        return string.IsNullOrWhiteSpace(intent)
               || tags.Any(tag => string.Equals(tag, intent, StringComparison.OrdinalIgnoreCase));
    }

    private static TypeDescriptor CreateDescriptor(string category, Type type, string discriminator, bool includeProperties)
    {
        LibraryItem? item = LibraryService.Current.FindItem(type);
        return new TypeDescriptor(
            type.FullName ?? type.Name,
            discriminator,
            category,
            includeProperties ? CreateBaseFields(type) : [],
            includeProperties ? CreateProperties(type) : [],
            item?.DisplayName,
            item?.Description);
    }

    private static IReadOnlyList<FieldDescriptor> CreateBaseFields(Type type)
    {
        if (!typeof(ICoreObject).IsAssignableFrom(type))
        {
            return [];
        }

        return PropertyRegistry.GetRegistered(type)
            .Select(property =>
            {
                ICorePropertyMetadata metadata = property.GetMetadata<ICorePropertyMetadata>(type);
                return new FieldDescriptor(property.Name, property.PropertyType.FullName ?? property.PropertyType.Name, metadata.GetDefaultValue());
            })
            .ToArray();
    }

    private static IReadOnlyList<PropertyDescriptor> CreateProperties(Type type)
    {
        if (!typeof(EngineObject).IsAssignableFrom(type)
            || Activator.CreateInstance(type) is not EngineObject engineObject)
        {
            return [];
        }

        return engineObject.Properties.Select(CreateProperty).ToArray();
    }

    private static IReadOnlyList<DeclarativeExample> CreateExamples(string? typeFilter, string? categoryFilter, string? nameFilter = null)
    {
        ExampleSpec[] examples = s_exampleSpecs.Value;
        if (string.IsNullOrWhiteSpace(typeFilter)
            && string.IsNullOrWhiteSpace(categoryFilter)
            && string.IsNullOrWhiteSpace(nameFilter))
        {
            return examples.Select(CloneExample).ToArray();
        }

        return examples
            .Where(spec => ExampleMatches(spec, typeFilter, categoryFilter, nameFilter))
            .Select(CloneExample)
            .ToArray();
    }

    private static DeclarativeExample CloneExample(ExampleSpec spec)
    {
        DeclarativeExample example = spec.Example;
        return new DeclarativeExample(example.Name, example.Description, (JsonObject)example.Patch.DeepClone());
    }

    private static ExampleSpec[] CreateExampleSpecs()
    {
        string animationType = IdentityHelper.WriteDiscriminator(typeof(KeyFrameAnimation<float>));
        string keyFrameType = IdentityHelper.WriteDiscriminator(typeof(KeyFrame<float>));
        string linearEasingType = IdentityHelper.WriteDiscriminator(typeof(LinearEasing));
        string sineEaseOutType = IdentityHelper.WriteDiscriminator(typeof(SineEaseOut));

        return
        [
            new ExampleSpec(
                CreateEmptySceneMotionExample(),
                ExampleCategories(
                    KnownLibraryItemFormats.Drawable,
                    KnownLibraryItemFormats.EngineObject,
                    KnownLibraryItemFormats.Brush,
                    KnownLibraryItemFormats.FilterEffect,
                    KnownLibraryItemFormats.Transform,
                    KnownLibraryItemFormats.Easing),
                ExampleTypes(
                    typeof(Element),
                    typeof(RectShape),
                    typeof(TextBlock),
                    typeof(LinearGradientBrush),
                    typeof(GradientStop),
                    typeof(FilterEffectGroup),
                    typeof(Blur),
                    typeof(Brightness),
                    typeof(TransformGroup),
                    typeof(TranslateTransform),
                    typeof(RotationTransform),
                    typeof(KeyFrameAnimation<float>),
                    typeof(KeyFrame<float>),
                    typeof(CubicEaseOut),
                    typeof(SineEaseInOut)),
                ExampleTags("starter", "empty-scene", "ribbon", "typography", "gradient", "effect")),
            new ExampleSpec(
                CreateOrbitalRadarExample(),
                ExampleCategories(
                    KnownLibraryItemFormats.Drawable,
                    KnownLibraryItemFormats.EngineObject,
                    KnownLibraryItemFormats.Brush,
                    KnownLibraryItemFormats.FilterEffect,
                    KnownLibraryItemFormats.Transform,
                    KnownLibraryItemFormats.Pen,
                    KnownLibraryItemFormats.Easing),
                ExampleTypes(
                    typeof(Element),
                    typeof(RectShape),
                    typeof(EllipseShape),
                    typeof(TextBlock),
                    typeof(LinearGradientBrush),
                    typeof(SolidColorBrush),
                    typeof(Pen),
                    typeof(FilterEffectGroup),
                    typeof(Blur),
                    typeof(DropShadow),
                    typeof(TransformGroup),
                    typeof(TranslateTransform),
                    typeof(RotationTransform),
                    typeof(KeyFrameAnimation<float>),
                    typeof(KeyFrame<float>),
                    typeof(CubicEaseOut),
                    typeof(SineEaseInOut)),
                ExampleTags("starter", "empty-scene", "orbital", "radar", "rings", "pen", "glow")),
            new ExampleSpec(
                CreateSplitScreenTypographyExample(),
                ExampleCategories(
                    KnownLibraryItemFormats.Drawable,
                    KnownLibraryItemFormats.EngineObject,
                    KnownLibraryItemFormats.Brush,
                    KnownLibraryItemFormats.FilterEffect,
                    KnownLibraryItemFormats.Transform,
                    KnownLibraryItemFormats.Easing),
                ExampleTypes(
                    typeof(Element),
                    typeof(RectShape),
                    typeof(RoundedRectShape),
                    typeof(TextBlock),
                    typeof(LinearGradientBrush),
                    typeof(SolidColorBrush),
                    typeof(FilterEffectGroup),
                    typeof(Blur),
                    typeof(Brightness),
                    typeof(Saturate),
                    typeof(HueRotate),
                    typeof(TransformGroup),
                    typeof(TranslateTransform),
                    typeof(KeyFrameAnimation<float>),
                    typeof(KeyFrame<float>),
                    typeof(CubicEaseOut),
                    typeof(SineEaseInOut)),
                ExampleTags("starter", "empty-scene", "split-screen", "editorial", "blocks", "typography")),
            new ExampleSpec(
                new DeclarativeExample(
                    "animate-float-property-keyframes",
                    "Patch snippet for a float animatable property such as Opacity. Replace the placeholder Ids and property name. For non-float properties, use the matching KeyFrameAnimation<T> and KeyFrame<T> discriminator from a serialized sample.",
                    new JsonObject
                    {
                        ["Elements"] = new JsonArray(new JsonObject
                        {
                            [nameof(CoreObject.Id)] = "<element-id>",
                            [nameof(Element.Objects)] = new JsonArray(new JsonObject
                            {
                                [nameof(CoreObject.Id)] = "<engine-object-id>",
                                ["Animations"] = new JsonObject
                                {
                                    ["Opacity"] = new JsonObject
                                    {
                                        ["$type"] = animationType,
                                        [nameof(KeyFrameAnimation.KeyFrames)] = new JsonArray(
                                            new JsonObject
                                            {
                                                ["$type"] = keyFrameType,
                                                [nameof(KeyFrame.KeyTime)] = TimeSpan.Zero.ToString("c"),
                                                [nameof(KeyFrame<float>.Value)] = 0,
                                                [nameof(KeyFrame.Easing)] = linearEasingType
                                            },
                                            new JsonObject
                                            {
                                                ["$type"] = keyFrameType,
                                                [nameof(KeyFrame.KeyTime)] = TimeSpan.FromSeconds(1).ToString("c"),
                                                [nameof(KeyFrame<float>.Value)] = 100,
                                                [nameof(KeyFrame.Easing)] = sineEaseOutType
                                            })
                                    }
                                }
                            })
                        })
                    }),
                ExampleCategories(KnownLibraryItemFormats.Drawable, KnownLibraryItemFormats.EngineObject, KnownLibraryItemFormats.Easing),
                ExampleTypes(typeof(KeyFrameAnimation<float>), typeof(KeyFrame<float>), typeof(LinearEasing), typeof(SineEaseOut)),
                ExampleTags("targeted", "keyframes", "animation")),
            new ExampleSpec(
                CreateBrushAndEffectExample(),
                ExampleCategories(KnownLibraryItemFormats.Drawable, KnownLibraryItemFormats.EngineObject, KnownLibraryItemFormats.Brush, KnownLibraryItemFormats.FilterEffect),
                ExampleTypes(typeof(LinearGradientBrush), typeof(GradientStop), typeof(FilterEffectGroup), typeof(Blur), typeof(Brightness)),
                ExampleTags("targeted", "gradient", "effect"))
        ];
    }

    private static EffectRecipeSpec[] CreateEffectRecipeSpecs()
    {
        EffectRecipeSpec[] curated =
        [
            CreateEffectRecipe(
                "glow-depth",
                "Reusable glow/depth chain for text, nodes, panels, and highlight shapes.",
                ["glow", "depth", "title", "node"],
                CreateFilterEffectGroup(
                    CreateBlur(5),
                    CreateDropShadow(0, 0, 22, "#bb43e7ff"),
                    CreateBrightness(112))),
            CreateEffectRecipe(
                "editorial-color-grade",
                "Color grading chain for stronger palette separation without changing geometry.",
                ["color", "grade", "editorial", "palette"],
                CreateFilterEffectGroup(
                    CreateSaturate(132),
                    CreateHueRotate(18),
                    CreateBrightness(108),
                    CreateHighContrast(14))),
            CreateEffectRecipe(
                "digital-glitch",
                "Glitch chain with channel separation, mosaic sampling, and procedural shake.",
                ["glitch", "stylize", "motion", "distort"],
                CreateFilterEffectGroup(
                    CreateColorShift(14, 0, -12, 0),
                    CreateMosaic(18),
                    CreateShake(18, 7, 120))),
            CreateEffectRecipe(
                "graphic-outline",
                "Outline and flat-shadow chain for poster-like labels, icons, and hard-edged shapes.",
                ["outline", "graphic", "poster", "depth"],
                CreateFilterEffectGroup(
                    CreateStroke("#ff36f0ff", 6),
                    CreateFlatShadow(138, 34, "#aa05121f"))),
            CreateEffectRecipe(
                "pixel-sort-distortion",
                "GPU-dependent pixel-sort chain for harsher scanline and data-corruption looks.",
                ["glitch", "pixel", "scanline", "gpu"],
                CreateFilterEffectGroup(
                    CreatePixelSort(),
                    CreateColorShift(8, 0, -8, 0)))
        ];

        EffectRecipeSpec[] individual = EnumerateRegisteredTypes()
            .Where(item => MatchesCategory(KnownLibraryItemFormats.FilterEffect, item.Category))
            .Select(item => item.Type)
            .Distinct()
            .OrderBy(type => type.Name, StringComparer.Ordinal)
            .Select(CreateSingleEffectRecipe)
            .ToArray();

        return curated.Concat(individual)
            .DistinctBy(spec => spec.Summary.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static EffectRecipeSpec CreateEffectRecipe(
        string name,
        string description,
        IReadOnlyList<string> tags,
        FilterEffectGroup effects)
    {
        string[] effectNames = effects.Children
            .Select(effect => effect.GetType().Name)
            .ToArray();
        string[] notes = effectNames
            .SelectMany(name => GetEffectMetadataByName(name).Notes)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return new EffectRecipeSpec(
            new EffectRecipeSummary(name, description, tags.ToArray(), effectNames, notes),
            CreateEffectPatch(effects));
    }

    private static EffectRecipeSpec CreateSingleEffectRecipe(Type type)
    {
        FilterEffect effect = CreateRecipeEffectInstance(type);
        EffectMetadata metadata = GetEffectMetadata(type);
        string displayName = TypeNameToWords(type.Name);
        string name = $"effect-{ToKebabCase(type.Name)}";
        string[] tags = metadata.IntentTags
            .Append("single-effect")
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        string[] notes = metadata.Notes.ToArray();
        JsonObject patch = type == typeof(FilterEffectGroup)
            ? CreateEffectPatch((FilterEffectGroup)effect)
            : CreateEffectPatch(CreateFilterEffectGroup(effect));

        return new EffectRecipeSpec(
            new EffectRecipeSummary(
                name,
                $"Single-effect recipe for {displayName}. Use this when you want to intentionally exercise the {type.Name} filter.",
                tags,
                [type.Name],
                notes),
            patch);
    }

    private static FilterEffect CreateRecipeEffectInstance(Type type)
    {
        FilterEffect effect = type == typeof(FilterEffectGroup)
            ? new FilterEffectGroup()
            : Activator.CreateInstance(type) as FilterEffect
              ?? throw new InvalidOperationException($"Filter effect '{type.FullName}' does not have a usable parameterless constructor.");

        TuneEffectDefaults(effect);
        return effect;
    }

    private static void TuneEffectDefaults(FilterEffect effect)
    {
        switch (effect)
        {
            case FilterEffectGroup group:
                group.Children.Add(CreateBlur(4));
                group.Children.Add(CreateBrightness(108));
                break;
            case Blur blur:
                blur.Sigma.CurrentValue = new Size(8, 8);
                break;
            case DropShadow dropShadow:
                dropShadow.Position.CurrentValue = new Point(0, 0);
                dropShadow.Sigma.CurrentValue = new Size(18, 18);
                dropShadow.Color.CurrentValue = Color.Parse("#aa36f0ff");
                break;
            case InnerShadow innerShadow:
                innerShadow.Position.CurrentValue = new Point(10, 12);
                innerShadow.Sigma.CurrentValue = new Size(18, 18);
                innerShadow.Color.CurrentValue = Color.Parse("#aa000000");
                break;
            case FlatShadow flatShadow:
                flatShadow.Angle.CurrentValue = 138;
                flatShadow.Length.CurrentValue = 34;
                flatShadow.Brush.CurrentValue = new SolidColorBrush(Color.Parse("#aa05121f"));
                break;
            case StrokeEffect stroke:
                stroke.Pen.CurrentValue = CreatePen("#ff36f0ff", 6);
                break;
            case Clipping clipping:
                clipping.Left.CurrentValue = 16;
                clipping.Top.CurrentValue = 16;
                clipping.Right.CurrentValue = 16;
                clipping.Bottom.CurrentValue = 16;
                break;
            case Dilate dilate:
                dilate.RadiusX.CurrentValue = 6;
                dilate.RadiusY.CurrentValue = 6;
                break;
            case Erode erode:
                erode.RadiusX.CurrentValue = 4;
                erode.RadiusY.CurrentValue = 4;
                break;
            case HighContrast highContrast:
                highContrast.Contrast.CurrentValue = 18;
                break;
            case HueRotate hueRotate:
                hueRotate.Angle.CurrentValue = 24;
                break;
            case Lighting lighting:
                lighting.Multiply.CurrentValue = Color.Parse("#ffffffff");
                lighting.Add.CurrentValue = Color.Parse("#221ad8ff");
                break;
            case Saturate saturate:
                saturate.Amount.CurrentValue = 136;
                break;
            case Threshold threshold:
                threshold.Value.CurrentValue = 52;
                threshold.Smoothness.CurrentValue = 18;
                threshold.Strength.CurrentValue = 70;
                break;
            case Brightness brightness:
                brightness.Amount.CurrentValue = 116;
                break;
            case Gamma gamma:
                gamma.Amount.CurrentValue = 92;
                gamma.Strength.CurrentValue = 65;
                break;
            case ColorGrading colorGrading:
                colorGrading.Temperature.CurrentValue = -8;
                colorGrading.Tint.CurrentValue = 5;
                colorGrading.Contrast.CurrentValue = 12;
                colorGrading.Saturation.CurrentValue = 18;
                colorGrading.Vibrance.CurrentValue = 20;
                break;
            case Invert invert:
                invert.Amount.CurrentValue = 32;
                invert.ExcludeAlphaChannel.CurrentValue = true;
                break;
            case BlendEffect blend:
                blend.Brush.CurrentValue = new SolidColorBrush(Color.Parse("#7736f0ff"));
                blend.BlendMode.CurrentValue = BlendMode.Plus;
                break;
            case Negaposi negaposi:
                negaposi.Red.CurrentValue = 255;
                negaposi.Blue.CurrentValue = 255;
                negaposi.Strength.CurrentValue = 65;
                break;
            case ChromaKey chromaKey:
                chromaKey.Color.CurrentValue = Color.Parse("#ff00ff00");
                chromaKey.HueRange.CurrentValue = 12;
                chromaKey.SaturationRange.CurrentValue = 35;
                chromaKey.Boundary.CurrentValue = 3;
                break;
            case ColorKey colorKey:
                colorKey.Color.CurrentValue = Color.Parse("#ffffffff");
                colorKey.Range.CurrentValue = 18;
                colorKey.Boundary.CurrentValue = 3;
                break;
            case SplitEffect split:
                split.HorizontalDivisions.CurrentValue = 3;
                split.VerticalDivisions.CurrentValue = 2;
                split.HorizontalSpacing.CurrentValue = 12;
                split.VerticalSpacing.CurrentValue = 8;
                break;
            case TransformEffect transform:
                transform.Transform.CurrentValue = new RotationTransform(8);
                transform.TransformOrigin.CurrentValue = RelativePoint.Center;
                break;
            case MosaicEffect mosaic:
                mosaic.TileSize.CurrentValue = new Size(18, 18);
                break;
            case ColorShift colorShift:
                colorShift.RedOffset.CurrentValue = new PixelPoint(12, 0);
                colorShift.BlueOffset.CurrentValue = new PixelPoint(-12, 0);
                break;
            case ShakeEffect shake:
                shake.StrengthX.CurrentValue = 18;
                shake.StrengthY.CurrentValue = 7;
                shake.Speed.CurrentValue = 120;
                break;
            case DisplacementMapEffect displacement:
                if (displacement.Transform.CurrentValue is DisplacementMapTranslateTransform translate)
                {
                    translate.X.CurrentValue = 18;
                    translate.Y.CurrentValue = 10;
                }

                displacement.Signed.CurrentValue = true;
                break;
            case PathFollowEffect pathFollow:
                pathFollow.Progress.CurrentValue = 42;
                pathFollow.FollowRotation.CurrentValue = true;
                break;
            case DelayAnimationEffect delay:
                delay.Delay.CurrentValue = 55;
                delay.Effect.CurrentValue = CreateDropShadow(0, 0, 18, "#9936f0ff");
                break;
            case PixelSortEffect pixelSort:
                pixelSort.Direction.CurrentValue = PixelSortDirection.Horizontal;
                pixelSort.SortKey.CurrentValue = PixelSortKey.Hue;
                pixelSort.ThresholdMin.CurrentValue = 18;
                pixelSort.ThresholdMax.CurrentValue = 82;
                break;
        }
    }

    private static JsonObject CreateEffectPatch(FilterEffectGroup effects)
    {
        return new JsonObject
        {
            ["Elements"] = new JsonArray(new JsonObject
            {
                [nameof(CoreObject.Id)] = "<element-id>",
                [nameof(Element.Objects)] = new JsonArray(new JsonObject
                {
                    [nameof(CoreObject.Id)] = "<drawable-id>",
                    [nameof(Drawable.FilterEffect)] = SerializeExampleObject(effects)
                })
            })
        };
    }

    private static FilterEffectGroup CreateFilterEffectGroup(params FilterEffect[] effects)
    {
        var group = new FilterEffectGroup();
        foreach (FilterEffect effect in effects)
        {
            group.Children.Add(effect);
        }

        return group;
    }

    private static EffectMetadata GetEffectMetadataByName(string typeName)
    {
        KeyValuePair<Type, EffectMetadata> pair = s_effectMetadata
            .FirstOrDefault(item => string.Equals(item.Key.Name, typeName, StringComparison.Ordinal));
        return pair.Value ?? new EffectMetadata(InferEffectTags(typeName), [], RequiresGpu: false);
    }

    private static string ToKebabCase(string name)
    {
        var builder = new StringBuilder(name.Length + 8);
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (char.IsUpper(c) && i > 0 && (char.IsLower(name[i - 1]) || (i + 1 < name.Length && char.IsLower(name[i + 1]))))
            {
                builder.Append('-');
            }

            builder.Append(char.ToLowerInvariant(c));
        }

        return builder.ToString();
    }

    private static string TypeNameToWords(string name)
    {
        return ToKebabCase(name).Replace('-', ' ');
    }

    private static Dictionary<Type, EffectMetadata> CreateEffectMetadata()
    {
        return new Dictionary<Type, EffectMetadata>
        {
            [typeof(Blur)] = Metadata(["soften", "glow", "depth"], ["Use inside FilterEffectGroup before shadow/color effects for soft halos."]),
            [typeof(DropShadow)] = Metadata(["shadow", "glow", "depth"], ["Use zero offset for neon glow, non-zero offset for cast shadows."]),
            [typeof(InnerShadow)] = Metadata(["shadow", "depth", "inset"], []),
            [typeof(FlatShadow)] = Metadata(["shadow", "poster", "depth"], ["Good for flat editorial graphics and bold labels."]),
            [typeof(StrokeEffect)] = Metadata(["outline", "poster", "graphic"], ["Pair with FlatShadow for sticker-like typography or icon treatments."]),
            [typeof(HighContrast)] = Metadata(["color", "grade", "contrast"], []),
            [typeof(HueRotate)] = Metadata(["color", "grade", "palette"], []),
            [typeof(LumaColor)] = Metadata(["color", "luma", "grade"], []),
            [typeof(Saturate)] = Metadata(["color", "grade", "palette"], []),
            [typeof(Threshold)] = Metadata(["color", "graphic", "poster"], []),
            [typeof(Brightness)] = Metadata(["color", "grade", "glow"], []),
            [typeof(Gamma)] = Metadata(["color", "grade"], []),
            [typeof(ColorGrading)] = Metadata(["color", "grade", "cinematic"], []),
            [typeof(Curves)] = Metadata(["color", "grade", "cinematic"], []),
            [typeof(Invert)] = Metadata(["color", "graphic", "negative"], []),
            [typeof(LutEffect)] = Metadata(["color", "grade", "lut"], ["Requires a LUT source to have visible effect."]),
            [typeof(BlendEffect)] = Metadata(["composite", "blend", "layer"], []),
            [typeof(Negaposi)] = Metadata(["color", "negative", "graphic"], []),
            [typeof(ChromaKey)] = Metadata(["keying", "transparent", "video"], []),
            [typeof(ColorKey)] = Metadata(["keying", "transparent", "graphic"], []),
            [typeof(SplitEffect)] = Metadata(["glitch", "split", "stylize"], []),
            [typeof(PartsSplitEffect)] = Metadata(["glitch", "split", "stylize"], []),
            [typeof(TransformEffect)] = Metadata(["motion", "distort", "transform"], []),
            [typeof(MosaicEffect)] = Metadata(["glitch", "pixel", "stylize"], []),
            [typeof(ColorShift)] = Metadata(["glitch", "chromatic", "stylize"], []),
            [typeof(ShakeEffect)] = Metadata(["motion", "glitch", "shake"], ["Time-dependent effect; useful for animated jitter without explicit keyframes."]),
            [typeof(DisplacementMapEffect)] = Metadata(["distort", "map", "motion"], ["Pair with a map source or generated texture for visible displacement."]),
            [typeof(PathFollowEffect)] = Metadata(["motion", "path", "distort"], []),
            [typeof(LayerEffect)] = Metadata(["composite", "layer"], []),
            [typeof(DelayAnimationEffect)] = Metadata(["motion", "trail", "delay"], []),
            [typeof(PixelSortEffect)] = Metadata(["glitch", "pixel", "scanline", "gpu"], ["Requires GPU/Vulkan support; may be inactive on CPU-only rendering."], requiresGpu: true),
            [typeof(CSharpScriptEffect)] = Metadata(["advanced", "script", "programmable"], ["Prefer built-in effects for low-context agents unless script code is explicitly requested."]),
            [typeof(SKSLScriptEffect)] = Metadata(["advanced", "shader", "programmable"], ["Requires shader source. Prefer built-in effects for normal motion graphics."]),
            [typeof(GLSLScriptEffect)] = Metadata(["advanced", "shader", "gpu"], ["Requires GPU shader source and GPU support."], requiresGpu: true),
            [typeof(NodeGraphFilterEffect)] = Metadata(["advanced", "nodegraph", "programmable"], ["Requires a node graph resource to be useful."])
        };
    }

    private static EffectMetadata Metadata(IReadOnlyList<string> tags, IReadOnlyList<string> notes, bool requiresGpu = false)
    {
        return new EffectMetadata(tags.Append("effect").Distinct(StringComparer.Ordinal).ToArray(), notes.ToArray(), requiresGpu);
    }

    private static string[] ExampleCategories(params string[] categories)
    {
        return categories;
    }

    private static string[] ExampleTypes(params Type[] types)
    {
        return types
            .SelectMany(type => new[]
            {
                type.Name,
                type.FullName ?? type.Name,
                IdentityHelper.WriteDiscriminator(type)
            })
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static string[] ExampleTags(params string[] tags)
    {
        return tags;
    }

    private static bool ExampleMatches(ExampleSpec spec, string? typeFilter, string? categoryFilter, string? nameFilter)
    {
        bool typeMatches = string.IsNullOrWhiteSpace(typeFilter)
                           || spec.TypeTokens.Contains(typeFilter, StringComparer.Ordinal);
        bool categoryMatches = string.IsNullOrWhiteSpace(categoryFilter)
                               || spec.Categories.Any(category => MatchesCategory(categoryFilter, category));
        bool nameMatches = string.IsNullOrWhiteSpace(nameFilter)
                           || string.Equals(spec.Example.Name, nameFilter, StringComparison.OrdinalIgnoreCase);

        return typeMatches && categoryMatches && nameMatches;
    }

    private static DeclarativeExample CreateEmptySceneMotionExample()
    {
        Element background = CreateElement(
            "Background plate",
            zIndex: 0,
            new RectShape
            {
                Name = "Midnight gradient",
                Width = { CurrentValue = 1920 },
                Height = { CurrentValue = 1080 },
                Fill = { CurrentValue = CreateLinearGradient("#ff06121f", "#ff123f63") }
            });

        Element ribbon = CreateElement(
            "Animated ribbon",
            zIndex: 4,
            new RectShape
            {
                Name = "Diagonal gradient ribbon",
                Width = { CurrentValue = 1180 },
                Height = { CurrentValue = 86 },
                Fill = { CurrentValue = CreateLinearGradient("#ff20d6ff", "#ffffe28a") },
                Transform =
                {
                    CurrentValue = new TransformGroup
                    {
                        Children =
                        {
                            new TranslateTransform(-120, 38),
                            new RotationTransform(-10)
                        }
                    }
                },
                FilterEffect =
                {
                    CurrentValue = new FilterEffectGroup
                    {
                        Children =
                        {
                            CreateBlur(5),
                            CreateBrightness(112)
                        }
                    }
                }
            });

        Element title = CreateElement(
            "Visible title",
            zIndex: 20,
            new TextBlock
            {
                Name = "Hero title",
                Text = { CurrentValue = "BEUTL MOTION" },
                Size = { CurrentValue = 116 },
                Spacing = { CurrentValue = 8 },
                Fill = { CurrentValue = new SolidColorBrush(Colors.White) },
                Transform =
                {
                    CurrentValue = new TransformGroup
                    {
                        Children =
                        {
                            new TranslateTransform(0, -16)
                        }
                    }
                }
            });

        JsonObject backgroundJson = SerializeExampleElement(background);
        JsonObject ribbonJson = SerializeExampleElement(ribbon);
        JsonObject titleJson = SerializeExampleElement(title);

        JsonObject ribbonObject = GetFirstObjectJson(ribbonJson);
        AddFloatAnimation(ribbonObject, nameof(Drawable.Opacity), (0, 0, typeof(CubicEaseOut)), (1.2, 100, typeof(CubicEaseOut)), (8, 100, typeof(SineEaseInOut)));
        JsonObject ribbonTranslate = GetTransformChildJson(ribbonObject, typeof(TranslateTransform));
        AddFloatAnimation(ribbonTranslate, nameof(TranslateTransform.X), (0, -460, typeof(CubicEaseOut)), (4, 130, typeof(SineEaseInOut)), (8, 520, typeof(SineEaseInOut)));

        JsonObject titleObject = GetFirstObjectJson(titleJson);
        AddFloatAnimation(titleObject, nameof(Drawable.Opacity), (0, 0, typeof(CubicEaseOut)), (0.85, 100, typeof(CubicEaseOut)), (6.8, 100, typeof(SineEaseInOut)), (8, 0, typeof(SineEaseInOut)));
        AddFloatAnimation(titleObject, nameof(TextBlock.Spacing), (0, 22, typeof(CubicEaseOut)), (1.4, 8, typeof(SineEaseInOut)), (8, 14, typeof(SineEaseInOut)));

        return new DeclarativeExample(
            "create-empty-scene-motion-graphics",
            "Patch snippet for an empty scene. It appends visible elements without Id fields so the toolkit mints stable Ids; use the returned apply_edit document or read_document before follow-up edits.",
            new JsonObject
            {
                ["Duration"] = TimeSpan.FromSeconds(8).ToString("c"),
                ["Elements"] = new JsonArray(
                    backgroundJson,
                    ribbonJson,
                    titleJson)
            });
    }

    private static DeclarativeExample CreateOrbitalRadarExample()
    {
        Element background = CreateElement(
            "Orbital radar background",
            zIndex: 0,
            new RectShape
            {
                Name = "Deep teal field",
                Width = { CurrentValue = 1920 },
                Height = { CurrentValue = 1080 },
                Fill = { CurrentValue = CreateLinearGradient("#ff020711", "#ff063835") }
            });

        Element outerRing = CreateElement(
            "Orbital radar outer ring",
            zIndex: 4,
            new EllipseShape
            {
                Name = "Cyan orbit ring",
                Width = { CurrentValue = 720 },
                Height = { CurrentValue = 720 },
                Fill = { CurrentValue = null },
                Pen = { CurrentValue = CreatePen("#ff43e7ff", 7) },
                Transform =
                {
                    CurrentValue = new TransformGroup
                    {
                        Children =
                        {
                            new TranslateTransform(-270, 0),
                            new RotationTransform(0)
                        }
                    }
                },
                FilterEffect =
                {
                    CurrentValue = new FilterEffectGroup
                    {
                        Children =
                        {
                            CreateBlur(1.5f),
                            CreateDropShadow(0, 0, 16, "#aa43e7ff")
                        }
                    }
                }
            });

        Element innerRing = CreateElement(
            "Orbital radar amber ring",
            zIndex: 5,
            new EllipseShape
            {
                Name = "Amber offset ring",
                Width = { CurrentValue = 430 },
                Height = { CurrentValue = 430 },
                Fill = { CurrentValue = null },
                Pen = { CurrentValue = CreatePen("#ffffd36b", 4) },
                Transform =
                {
                    CurrentValue = new TransformGroup
                    {
                        Children =
                        {
                            new TranslateTransform(-270, 0),
                            new RotationTransform(0)
                        }
                    }
                },
                FilterEffect =
                {
                    CurrentValue = new FilterEffectGroup
                    {
                        Children =
                        {
                            CreateBlur(0.8f)
                        }
                    }
                }
            });

        Element signalNode = CreateElement(
            "Orbital radar signal node",
            zIndex: 9,
            new EllipseShape
            {
                Name = "Moving signal node",
                Width = { CurrentValue = 86 },
                Height = { CurrentValue = 86 },
                Fill = { CurrentValue = CreateLinearGradient("#ffff4da3", "#ff36f0ff") },
                Transform =
                {
                    CurrentValue = new TransformGroup
                    {
                        Children =
                        {
                            new TranslateTransform(-620, -230)
                        }
                    }
                },
                FilterEffect =
                {
                    CurrentValue = new FilterEffectGroup
                    {
                        Children =
                        {
                            CreateDropShadow(0, 0, 22, "#cc36f0ff"),
                            CreateBrightness(118)
                        }
                    }
                }
            });

        Element sweep = CreateElement(
            "Orbital radar sweep",
            zIndex: 7,
            new RectShape
            {
                Name = "Thin scanning sweep",
                Width = { CurrentValue = 820 },
                Height = { CurrentValue = 10 },
                Fill = { CurrentValue = CreateLinearGradient("#0036f0ff", "#dd36f0ff") },
                Transform =
                {
                    CurrentValue = new TransformGroup
                    {
                        Children =
                        {
                            new TranslateTransform(-270, 0),
                            new RotationTransform(0)
                        }
                    }
                },
                FilterEffect =
                {
                    CurrentValue = new FilterEffectGroup
                    {
                        Children =
                        {
                            CreateBlur(2)
                        }
                    }
                }
            });

        Element title = CreateElement(
            "Orbital radar title",
            zIndex: 20,
            new TextBlock
            {
                Name = "Orbital title",
                Text = { CurrentValue = "ORBIT MAP" },
                Size = { CurrentValue = 92 },
                Spacing = { CurrentValue = 10 },
                Fill = { CurrentValue = new SolidColorBrush(Colors.White) },
                Transform =
                {
                    CurrentValue = new TransformGroup
                    {
                        Children =
                        {
                            new TranslateTransform(420, -72)
                        }
                    }
                },
                FilterEffect =
                {
                    CurrentValue = new FilterEffectGroup
                    {
                        Children =
                        {
                            CreateDropShadow(10, 16, 12, "#aa000000")
                        }
                    }
                }
            });

        Element subtitle = CreateElement(
            "Orbital radar caption",
            zIndex: 21,
            new TextBlock
            {
                Name = "Orbital caption",
                Text = { CurrentValue = "SIGNAL ROUTES / LIVE DECLARATIVE EDIT" },
                Size = { CurrentValue = 34 },
                Spacing = { CurrentValue = 5 },
                Fill = { CurrentValue = new SolidColorBrush(Color.Parse("#ffc9faff")) },
                Transform =
                {
                    CurrentValue = new TransformGroup
                    {
                        Children =
                        {
                            new TranslateTransform(420, 20)
                        }
                    }
                }
            });

        JsonObject outerRingJson = SerializeExampleElement(outerRing);
        JsonObject innerRingJson = SerializeExampleElement(innerRing);
        JsonObject signalNodeJson = SerializeExampleElement(signalNode);
        JsonObject sweepJson = SerializeExampleElement(sweep);
        JsonObject titleJson = SerializeExampleElement(title);
        JsonObject subtitleJson = SerializeExampleElement(subtitle);

        JsonObject outerObject = GetFirstObjectJson(outerRingJson);
        AddFloatAnimation(outerObject, nameof(Drawable.Opacity), (0, 20, typeof(CubicEaseOut)), (1.4, 100, typeof(CubicEaseOut)), (8, 72, typeof(SineEaseInOut)));
        AddFloatAnimation(outerObject, nameof(EllipseShape.Width), (0, 640, typeof(CubicEaseOut)), (4, 780, typeof(SineEaseInOut)), (8, 700, typeof(SineEaseInOut)));
        AddFloatAnimation(outerObject, nameof(EllipseShape.Height), (0, 640, typeof(CubicEaseOut)), (4, 780, typeof(SineEaseInOut)), (8, 700, typeof(SineEaseInOut)));
        AddFloatAnimation(GetTransformChildJson(outerObject, typeof(RotationTransform)), nameof(RotationTransform.Rotation), (0, 0, typeof(CubicEaseOut)), (8, 360, typeof(SineEaseInOut)));

        JsonObject innerObject = GetFirstObjectJson(innerRingJson);
        AddFloatAnimation(innerObject, nameof(Drawable.Opacity), (0, 0, typeof(CubicEaseOut)), (1, 88, typeof(CubicEaseOut)), (8, 46, typeof(SineEaseInOut)));
        AddFloatAnimation(GetTransformChildJson(innerObject, typeof(RotationTransform)), nameof(RotationTransform.Rotation), (0, 18, typeof(CubicEaseOut)), (8, -210, typeof(SineEaseInOut)));

        JsonObject signalObject = GetFirstObjectJson(signalNodeJson);
        AddFloatAnimation(signalObject, nameof(Drawable.Opacity), (0, 0, typeof(CubicEaseOut)), (0.7, 100, typeof(CubicEaseOut)), (7.5, 100, typeof(SineEaseInOut)), (8, 0, typeof(SineEaseInOut)));
        JsonObject signalTranslate = GetTransformChildJson(signalObject, typeof(TranslateTransform));
        AddFloatAnimation(signalTranslate, nameof(TranslateTransform.X), (0, -620, typeof(CubicEaseOut)), (3.4, -160, typeof(SineEaseInOut)), (8, 140, typeof(SineEaseInOut)));
        AddFloatAnimation(signalTranslate, nameof(TranslateTransform.Y), (0, -230, typeof(CubicEaseOut)), (3.4, 230, typeof(SineEaseInOut)), (8, -80, typeof(SineEaseInOut)));

        JsonObject sweepObject = GetFirstObjectJson(sweepJson);
        AddFloatAnimation(sweepObject, nameof(Drawable.Opacity), (0, 0, typeof(CubicEaseOut)), (1.2, 70, typeof(CubicEaseOut)), (8, 0, typeof(SineEaseInOut)));
        AddFloatAnimation(GetTransformChildJson(sweepObject, typeof(RotationTransform)), nameof(RotationTransform.Rotation), (0, -18, typeof(CubicEaseOut)), (8, 205, typeof(SineEaseInOut)));

        JsonObject titleObject = GetFirstObjectJson(titleJson);
        AddFloatAnimation(titleObject, nameof(Drawable.Opacity), (0, 0, typeof(CubicEaseOut)), (1, 100, typeof(CubicEaseOut)), (7, 100, typeof(SineEaseInOut)), (8, 0, typeof(SineEaseInOut)));
        AddFloatAnimation(titleObject, nameof(TextBlock.Spacing), (0, 22, typeof(CubicEaseOut)), (1.5, 10, typeof(SineEaseInOut)), (8, 16, typeof(SineEaseInOut)));

        JsonObject subtitleObject = GetFirstObjectJson(subtitleJson);
        AddFloatAnimation(subtitleObject, nameof(Drawable.Opacity), (0, 0, typeof(CubicEaseOut)), (1.4, 90, typeof(CubicEaseOut)), (7.2, 90, typeof(SineEaseInOut)), (8, 0, typeof(SineEaseInOut)));

        return new DeclarativeExample(
            "create-empty-scene-orbital-radar",
            "Patch snippet for an empty scene with orbit rings, a moving signal node, scan sweep, glow, pens, gradients, and title typography. Use this when repeated ribbon/title starters would look too similar.",
            new JsonObject
            {
                ["Duration"] = TimeSpan.FromSeconds(8).ToString("c"),
                ["Elements"] = new JsonArray(
                    SerializeExampleElement(background),
                    outerRingJson,
                    innerRingJson,
                    signalNodeJson,
                    sweepJson,
                    titleJson,
                    subtitleJson)
            });
    }

    private static DeclarativeExample CreateSplitScreenTypographyExample()
    {
        Element background = CreateElement(
            "Split typography background",
            zIndex: 0,
            new RectShape
            {
                Name = "Blue violet field",
                Width = { CurrentValue = 1920 },
                Height = { CurrentValue = 1080 },
                Fill = { CurrentValue = CreateLinearGradient("#ff071225", "#ff2e1446") },
                FilterEffect =
                {
                    CurrentValue = new FilterEffectGroup
                    {
                        Children =
                        {
                            CreateSaturate(115),
                            CreateHueRotate(4)
                        }
                    }
                }
            });

        Element panel = CreateElement(
            "Split typography panel",
            zIndex: 3,
            new RoundedRectShape
            {
                Name = "Left editorial panel",
                Width = { CurrentValue = 760 },
                Height = { CurrentValue = 620 },
                CornerRadius = { CurrentValue = new CornerRadius(54) },
                Fill = { CurrentValue = CreateLinearGradient("#eeffffff", "#aa6cf3ff") },
                Transform =
                {
                    CurrentValue = new TransformGroup
                    {
                        Children =
                        {
                            new TranslateTransform(-420, 0)
                        }
                    }
                },
                FilterEffect =
                {
                    CurrentValue = new FilterEffectGroup
                    {
                        Children =
                        {
                            CreateBlur(0.8f),
                            CreateDropShadow(24, 34, 18, "#99000000")
                        }
                    }
                }
            });

        Element headline = CreateElement(
            "Split typography headline",
            zIndex: 12,
            new TextBlock
            {
                Name = "Stacked headline",
                Text = { CurrentValue = "FRAME FLOW" },
                Size = { CurrentValue = 96 },
                Spacing = { CurrentValue = 4 },
                Fill = { CurrentValue = new SolidColorBrush(Color.Parse("#ff081225")) },
                Transform =
                {
                    CurrentValue = new TransformGroup
                    {
                        Children =
                        {
                            new TranslateTransform(-420, -72)
                        }
                    }
                }
            });

        Element caption = CreateElement(
            "Split typography caption",
            zIndex: 13,
            new TextBlock
            {
                Name = "Panel caption",
                Text = { CurrentValue = "KINETIC LAYOUT / GRADIENT BRUSHES / EFFECT CHAIN" },
                Size = { CurrentValue = 28 },
                Spacing = { CurrentValue = 3 },
                Fill = { CurrentValue = new SolidColorBrush(Color.Parse("#ff10223c")) },
                Transform =
                {
                    CurrentValue = new TransformGroup
                    {
                        Children =
                        {
                            new TranslateTransform(-420, 48)
                        }
                    }
                }
            });

        Element wideBlock = CreateElement(
            "Split typography wide block",
            zIndex: 7,
            new RectShape
            {
                Name = "Right cyan block",
                Width = { CurrentValue = 680 },
                Height = { CurrentValue = 96 },
                Fill = { CurrentValue = CreateLinearGradient("#ff34e6ff", "#fffff072") },
                Transform =
                {
                    CurrentValue = new TransformGroup
                    {
                        Children =
                        {
                            new TranslateTransform(520, -200)
                        }
                    }
                },
                FilterEffect =
                {
                    CurrentValue = new FilterEffectGroup
                    {
                        Children =
                        {
                            CreateBrightness(116),
                            CreateDropShadow(18, 20, 14, "#88000000")
                        }
                    }
                }
            });

        Element tallBlock = CreateElement(
            "Split typography vertical block",
            zIndex: 8,
            new RectShape
            {
                Name = "Magenta vertical block",
                Width = { CurrentValue = 92 },
                Height = { CurrentValue = 520 },
                Fill = { CurrentValue = CreateLinearGradient("#ffff4aa8", "#ff6b7cff") },
                Transform =
                {
                    CurrentValue = new TransformGroup
                    {
                        Children =
                        {
                            new TranslateTransform(270, 130)
                        }
                    }
                },
                FilterEffect =
                {
                    CurrentValue = new FilterEffectGroup
                    {
                        Children =
                        {
                            CreateBlur(1.2f)
                        }
                    }
                }
            });

        Element label = CreateElement(
            "Split typography right label",
            zIndex: 18,
            new TextBlock
            {
                Name = "Right label",
                Text = { CurrentValue = "VARIANT 02" },
                Size = { CurrentValue = 54 },
                Spacing = { CurrentValue = 12 },
                Fill = { CurrentValue = new SolidColorBrush(Colors.White) },
                Transform =
                {
                    CurrentValue = new TransformGroup
                    {
                        Children =
                        {
                            new TranslateTransform(520, 96)
                        }
                    }
                },
                FilterEffect =
                {
                    CurrentValue = new FilterEffectGroup
                    {
                        Children =
                        {
                            CreateDropShadow(8, 10, 10, "#aa000000")
                        }
                    }
                }
            });

        JsonObject panelJson = SerializeExampleElement(panel);
        JsonObject headlineJson = SerializeExampleElement(headline);
        JsonObject captionJson = SerializeExampleElement(caption);
        JsonObject wideBlockJson = SerializeExampleElement(wideBlock);
        JsonObject tallBlockJson = SerializeExampleElement(tallBlock);
        JsonObject labelJson = SerializeExampleElement(label);

        JsonObject panelObject = GetFirstObjectJson(panelJson);
        AddFloatAnimation(panelObject, nameof(Drawable.Opacity), (0, 0, typeof(CubicEaseOut)), (0.7, 100, typeof(CubicEaseOut)), (8, 100, typeof(SineEaseInOut)));
        AddFloatAnimation(GetTransformChildJson(panelObject, typeof(TranslateTransform)), nameof(TranslateTransform.X), (0, -620, typeof(CubicEaseOut)), (1.1, -420, typeof(CubicEaseOut)), (8, -380, typeof(SineEaseInOut)));

        JsonObject headlineObject = GetFirstObjectJson(headlineJson);
        AddFloatAnimation(headlineObject, nameof(Drawable.Opacity), (0, 0, typeof(CubicEaseOut)), (1, 100, typeof(CubicEaseOut)), (7.2, 100, typeof(SineEaseInOut)), (8, 0, typeof(SineEaseInOut)));
        AddFloatAnimation(headlineObject, nameof(TextBlock.Spacing), (0, 26, typeof(CubicEaseOut)), (1.6, 4, typeof(SineEaseInOut)), (8, 10, typeof(SineEaseInOut)));

        JsonObject captionObject = GetFirstObjectJson(captionJson);
        AddFloatAnimation(captionObject, nameof(Drawable.Opacity), (0, 0, typeof(CubicEaseOut)), (1.5, 92, typeof(CubicEaseOut)), (8, 92, typeof(SineEaseInOut)));

        JsonObject wideObject = GetFirstObjectJson(wideBlockJson);
        AddFloatAnimation(wideObject, nameof(Drawable.Opacity), (0, 0, typeof(CubicEaseOut)), (0.9, 100, typeof(CubicEaseOut)), (8, 80, typeof(SineEaseInOut)));
        AddFloatAnimation(GetTransformChildJson(wideObject, typeof(TranslateTransform)), nameof(TranslateTransform.X), (0, 880, typeof(CubicEaseOut)), (1.4, 520, typeof(CubicEaseOut)), (8, 460, typeof(SineEaseInOut)));

        JsonObject tallObject = GetFirstObjectJson(tallBlockJson);
        AddFloatAnimation(tallObject, nameof(Drawable.Opacity), (0, 0, typeof(CubicEaseOut)), (1.2, 88, typeof(CubicEaseOut)), (8, 88, typeof(SineEaseInOut)));
        AddFloatAnimation(GetTransformChildJson(tallObject, typeof(TranslateTransform)), nameof(TranslateTransform.Y), (0, 420, typeof(CubicEaseOut)), (1.7, 130, typeof(CubicEaseOut)), (8, 190, typeof(SineEaseInOut)));

        JsonObject labelObject = GetFirstObjectJson(labelJson);
        AddFloatAnimation(labelObject, nameof(Drawable.Opacity), (0, 0, typeof(CubicEaseOut)), (1.8, 100, typeof(CubicEaseOut)), (7.2, 100, typeof(SineEaseInOut)), (8, 0, typeof(SineEaseInOut)));

        return new DeclarativeExample(
            "create-empty-scene-split-screen-typography",
            "Patch snippet for an empty scene with a split-screen editorial layout, animated panels, kinetic typography, blocks, gradients, and layered effects. Use this to avoid repeating the central ribbon composition.",
            new JsonObject
            {
                ["Duration"] = TimeSpan.FromSeconds(8).ToString("c"),
                ["Elements"] = new JsonArray(
                    SerializeExampleElement(background),
                    panelJson,
                    headlineJson,
                    captionJson,
                    wideBlockJson,
                    tallBlockJson,
                    labelJson)
            });
    }

    private static DeclarativeExample CreateBrushAndEffectExample()
    {
        var brush = new LinearGradientBrush
        {
            GradientStops =
            {
                new GradientStop(Color.FromRgb(0x1a, 0xd8, 0xff), 0),
                new GradientStop(Color.FromRgb(0xff, 0x45, 0xb5), 1)
            }
        };

        var blur = new Blur();
        blur.Sigma.CurrentValue = new Size(8, 8);
        var brightness = new Brightness();
        brightness.Amount.CurrentValue = 115;
        var effects = new FilterEffectGroup
        {
            Children =
            {
                blur,
                brightness
            }
        };

        return new DeclarativeExample(
            "apply-gradient-fill-and-effect-chain",
            "Patch snippet for applying a gradient brush and a filter effect chain to a drawable such as RectShape. Omit Id on new nested objects to insert them; include Ids from read_document to update or delete existing GradientStops and effect Children.",
            new JsonObject
            {
                ["Elements"] = new JsonArray(new JsonObject
                {
                    [nameof(CoreObject.Id)] = "<element-id>",
                    [nameof(Element.Objects)] = new JsonArray(new JsonObject
                    {
                        [nameof(CoreObject.Id)] = "<drawable-id>",
                        [nameof(Shape.Fill)] = SerializeExampleObject(brush),
                        [nameof(Drawable.FilterEffect)] = SerializeExampleObject(effects)
                    })
                })
            });
    }

    private static Element CreateElement(string name, int zIndex, EngineObject obj)
    {
        var element = new Element
        {
            Name = name,
            Start = TimeSpan.Zero,
            Length = TimeSpan.FromSeconds(8),
            ZIndex = zIndex
        };
        element.AddObject(obj);
        return element;
    }

    private static LinearGradientBrush CreateLinearGradient(string startColor, string endColor)
    {
        return new LinearGradientBrush
        {
            StartPoint = { CurrentValue = new RelativePoint(0, 0.5f, RelativeUnit.Relative) },
            EndPoint = { CurrentValue = new RelativePoint(1, 0.5f, RelativeUnit.Relative) },
            GradientStops =
            {
                new GradientStop(Color.Parse(startColor), 0),
                new GradientStop(Color.Parse(endColor), 1)
            }
        };
    }

    private static Blur CreateBlur(float sigma)
    {
        var blur = new Blur();
        blur.Sigma.CurrentValue = new Size(sigma, sigma);
        return blur;
    }

    private static Brightness CreateBrightness(float amount)
    {
        var brightness = new Brightness();
        brightness.Amount.CurrentValue = amount;
        return brightness;
    }

    private static Pen CreatePen(string color, float thickness)
    {
        return new Pen
        {
            Brush = { CurrentValue = new SolidColorBrush(Color.Parse(color)) },
            Thickness = { CurrentValue = thickness }
        };
    }

    private static DropShadow CreateDropShadow(float x, float y, float sigma, string color)
    {
        var dropShadow = new DropShadow();
        dropShadow.Position.CurrentValue = new Point(x, y);
        dropShadow.Sigma.CurrentValue = new Size(sigma, sigma);
        dropShadow.Color.CurrentValue = Color.Parse(color);
        return dropShadow;
    }

    private static Saturate CreateSaturate(float amount)
    {
        var saturate = new Saturate();
        saturate.Amount.CurrentValue = amount;
        return saturate;
    }

    private static HueRotate CreateHueRotate(float angle)
    {
        var hueRotate = new HueRotate();
        hueRotate.Angle.CurrentValue = angle;
        return hueRotate;
    }

    private static HighContrast CreateHighContrast(float contrast)
    {
        var highContrast = new HighContrast();
        highContrast.Contrast.CurrentValue = contrast;
        return highContrast;
    }

    private static ColorShift CreateColorShift(int redX, int redY, int blueX, int blueY)
    {
        var colorShift = new ColorShift();
        colorShift.RedOffset.CurrentValue = new PixelPoint(redX, redY);
        colorShift.GreenOffset.CurrentValue = new PixelPoint(0, 0);
        colorShift.BlueOffset.CurrentValue = new PixelPoint(blueX, blueY);
        colorShift.AlphaOffset.CurrentValue = new PixelPoint(0, 0);
        return colorShift;
    }

    private static MosaicEffect CreateMosaic(float tileSize)
    {
        var mosaic = new MosaicEffect();
        mosaic.TileSize.CurrentValue = new Size(tileSize, tileSize);
        return mosaic;
    }

    private static ShakeEffect CreateShake(float strengthX, float strengthY, float speed)
    {
        var shake = new ShakeEffect();
        shake.StrengthX.CurrentValue = strengthX;
        shake.StrengthY.CurrentValue = strengthY;
        shake.Speed.CurrentValue = speed;
        return shake;
    }

    private static StrokeEffect CreateStroke(string color, float thickness)
    {
        var stroke = new StrokeEffect();
        stroke.Pen.CurrentValue = CreatePen(color, thickness);
        return stroke;
    }

    private static FlatShadow CreateFlatShadow(float angle, float length, string color)
    {
        var flatShadow = new FlatShadow();
        flatShadow.Angle.CurrentValue = angle;
        flatShadow.Length.CurrentValue = length;
        flatShadow.Brush.CurrentValue = new SolidColorBrush(Color.Parse(color));
        return flatShadow;
    }

    private static PixelSortEffect CreatePixelSort()
    {
        var pixelSort = new PixelSortEffect();
        pixelSort.Direction.CurrentValue = PixelSortDirection.Horizontal;
        pixelSort.SortKey.CurrentValue = PixelSortKey.Hue;
        pixelSort.ThresholdMin.CurrentValue = 18;
        pixelSort.ThresholdMax.CurrentValue = 82;
        return pixelSort;
    }

    private static JsonObject SerializeExampleElement(Element element)
    {
        JsonObject json = CoreSerializer.SerializeToJsonObject(element);
        RemoveIds(json);
        return json;
    }

    private static JsonObject GetFirstObjectJson(JsonObject element)
    {
        return (JsonObject)((JsonArray)element[nameof(Element.Objects)]!)[0]!;
    }

    private static JsonObject GetTransformChildJson(JsonObject drawable, Type transformType)
    {
        string discriminator = IdentityHelper.WriteDiscriminator(transformType);
        JsonArray children = (JsonArray)drawable[nameof(Drawable.Transform)]![nameof(TransformGroup.Children)]!;
        return children
            .OfType<JsonObject>()
            .Single(child => string.Equals(child["$type"]?.GetValue<string>(), discriminator, StringComparison.Ordinal));
    }

    private static void AddFloatAnimation(JsonObject target, string property, params (double Seconds, float Value, Type Easing)[] keyframes)
    {
        JsonObject animations = target["Animations"] as JsonObject ?? [];
        animations[property] = CreateFloatAnimation(keyframes);
        target["Animations"] = animations;
    }

    private static JsonObject CreateFloatAnimation(params (double Seconds, float Value, Type Easing)[] keyframes)
    {
        string animationType = IdentityHelper.WriteDiscriminator(typeof(KeyFrameAnimation<float>));
        string keyFrameType = IdentityHelper.WriteDiscriminator(typeof(KeyFrame<float>));
        return new JsonObject
        {
            ["$type"] = animationType,
            [nameof(KeyFrameAnimation.KeyFrames)] = new JsonArray(keyframes
                .Select(keyframe => new JsonObject
                {
                    ["$type"] = keyFrameType,
                    [nameof(KeyFrame.KeyTime)] = TimeSpan.FromSeconds(keyframe.Seconds).ToString("c"),
                    [nameof(KeyFrame<float>.Value)] = keyframe.Value,
                    [nameof(KeyFrame.Easing)] = IdentityHelper.WriteDiscriminator(keyframe.Easing)
                })
                .ToArray<JsonNode?>())
        };
    }

    private static JsonObject SerializeExampleObject(ICoreSerializable value)
    {
        JsonObject json = CoreSerializer.SerializeToJsonObject(value);
        RemoveIds(json);
        return json;
    }

    private static void RemoveIds(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            obj.Remove(nameof(CoreObject.Id));
            foreach (JsonNode? child in obj.Select(pair => pair.Value).ToArray())
            {
                RemoveIds(child);
            }
        }
        else if (node is JsonArray array)
        {
            foreach (JsonNode? child in array.ToArray())
            {
                RemoveIds(child);
            }
        }
    }

    private static PropertyDescriptor CreateProperty(IProperty property)
    {
        Attribute[] attributes = property.GetAttributes() ?? [];
        DisplayAttribute? display = attributes.OfType<DisplayAttribute>().FirstOrDefault();
        RangeAttribute? range = attributes.OfType<RangeAttribute>().FirstOrDefault();
        NumberStepAttribute? step = attributes.OfType<NumberStepAttribute>().FirstOrDefault();
        RangeDescriptor? rangeDescriptor = TryCreateRange(range);

        return new PropertyDescriptor(
            property.Name,
            property.ValueType.FullName ?? property.ValueType.Name,
            property.DefaultValue,
            property.IsAnimatable,
            property.SupportsExpression,
            display is null ? null : new DisplayDescriptor(display.GetName(), display.GetDescription(), display.GetGroupName()),
            rangeDescriptor,
            step?.SmallChange,
            FindJsonConverter(property, attributes),
            ElementType: property is IListProperty listProperty
                ? listProperty.ElementType.FullName ?? listProperty.ElementType.Name
                : null);
    }

    private static string? FindJsonConverter(IProperty property, Attribute[] attributes)
    {
        JsonConverterAttribute? converter = attributes
            .OfType<JsonConverterAttribute>()
            .FirstOrDefault();
        converter ??= property.ValueType.GetCustomAttribute<JsonConverterAttribute>();

        Type? converterType = converter?.ConverterType;
        return converterType?.FullName ?? converterType?.Name;
    }

    private static bool Matches(string? typeFilter, Type type, string discriminator)
    {
        return string.IsNullOrWhiteSpace(typeFilter)
               || string.Equals(typeFilter, discriminator, StringComparison.Ordinal)
               || string.Equals(typeFilter, type.FullName, StringComparison.Ordinal)
               || string.Equals(typeFilter, type.Name, StringComparison.Ordinal);
    }

    private static bool MatchesCategory(string? categoryFilter, string category)
    {
        if (string.IsNullOrWhiteSpace(categoryFilter))
        {
            return true;
        }

        string normalizedFilter = NormalizeCategoryToken(categoryFilter);
        return string.Equals(normalizedFilter, NormalizeCategoryToken(category), StringComparison.Ordinal);
    }

    private static string SimplifyCategory(string category)
    {
        int index = category.LastIndexOf('.');
        return index >= 0 ? category[(index + 1)..] : category;
    }

    private static string NormalizeCategoryToken(string category)
    {
        string simplified = SimplifyCategory(category).Replace(" ", string.Empty, StringComparison.Ordinal);
        return simplified.ToLowerInvariant() switch
        {
            "effect" or "filter" or "filtereffect" or "visualeffect" or "videoeffect" => "filtereffect",
            "audio" or "audioeffect" or "soundeffect" => "audioeffect",
            "drawable" or "shape" or "visual" => "drawable",
            "brush" or "fill" or "gradient" => "brush",
            "transform" or "transformation" => "transform",
            "geometry" => "geometry",
            "pen" or "stroke" => "pen",
            "easing" or "ease" => "easing",
            "graphnode" or "node" => "graphnode",
            "engineobject" or "object" => "engineobject",
            _ => simplified.ToLowerInvariant()
        };
    }

    private static RangeDescriptor? TryCreateRange(RangeAttribute? range)
    {
        if (range is null)
        {
            return null;
        }

        return TryToDouble(range.Minimum, out double min) && TryToDouble(range.Maximum, out double max)
            ? new RangeDescriptor(min, max)
            : null;
    }

    private static bool TryToDouble(object value, out double result)
    {
        try
        {
            result = Convert.ToDouble(value, CultureInfo.InvariantCulture);
            return true;
        }
        catch (FormatException)
        {
            result = default;
            return false;
        }
        catch (InvalidCastException)
        {
            result = default;
            return false;
        }
    }

    private sealed record ExampleSpec(
        DeclarativeExample Example,
        IReadOnlyList<string> Categories,
        IReadOnlyList<string> TypeTokens,
        IReadOnlyList<string> Tags);

    private sealed record EffectRecipeSpec(
        EffectRecipeSummary Summary,
        JsonObject Patch);

    private sealed record EffectMetadata(
        IReadOnlyList<string> IntentTags,
        IReadOnlyList<string> Notes,
        bool RequiresGpu);
}
