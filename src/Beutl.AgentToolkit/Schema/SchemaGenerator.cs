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
    private static readonly Dictionary<string, string[]> s_typeAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["text"] = [nameof(TextBlock)],
        ["label"] = [nameof(TextBlock)],
        ["typography"] = [nameof(TextBlock)],
        ["shape"] = [nameof(RectShape), nameof(RoundedRectShape), nameof(EllipseShape), nameof(GeometryShape)],
        ["rectangle"] = [nameof(RectShape), nameof(RoundedRectShape)],
        ["rect"] = [nameof(RectShape), nameof(RoundedRectShape)],
        ["roundedrectangle"] = [nameof(RoundedRectShape)],
        ["roundedrect"] = [nameof(RoundedRectShape)],
        ["circle"] = [nameof(EllipseShape)],
        ["ellipse"] = [nameof(EllipseShape)],
        ["transform"] = [nameof(TransformGroup), nameof(TranslateTransform), nameof(RotationTransform), nameof(ScaleTransform), nameof(SkewTransform), nameof(MatrixTransform), nameof(Rotation3DTransform)],
        ["transforms"] = [nameof(TransformGroup), nameof(TranslateTransform), nameof(RotationTransform), nameof(ScaleTransform), nameof(SkewTransform), nameof(MatrixTransform), nameof(Rotation3DTransform)],
        ["transformgroup"] = [nameof(TransformGroup)],
        ["grouptransform"] = [nameof(TransformGroup)],
        ["translate"] = [nameof(TranslateTransform)],
        ["translation"] = [nameof(TranslateTransform)],
        ["translatetransform"] = [nameof(TranslateTransform)],
        ["rotate"] = [nameof(RotationTransform)],
        ["rotation"] = [nameof(RotationTransform)],
        ["rotationtransform"] = [nameof(RotationTransform)],
        ["scale"] = [nameof(ScaleTransform)],
        ["scaletransform"] = [nameof(ScaleTransform)],
        ["skew"] = [nameof(SkewTransform)],
        ["skewtransform"] = [nameof(SkewTransform)],
        ["matrix"] = [nameof(MatrixTransform)],
        ["matrixtransform"] = [nameof(MatrixTransform)]
    };

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
        (typeFilter, categoryFilter) = NormalizeFilters(typeFilter, categoryFilter);

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
        (typeFilter, categoryFilter) = NormalizeFilters(typeFilter, categoryFilter);
        return CreateExamples(typeFilter, categoryFilter, nameFilter);
    }

    public IReadOnlyList<DeclarativeExampleSummary> ListExamples(string? typeFilter = null, string? categoryFilter = null)
    {
        TypeRegistration.EnsureRegistered();
        (typeFilter, categoryFilter) = NormalizeFilters(typeFilter, categoryFilter);
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
            .Select(summary => new { Summary = summary, Score = ScoreEffect(summary, intent) })
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Summary.Name, StringComparer.Ordinal)
            .Select(item => item.Summary)
            .ToArray();
    }

    public ShaderCompilationCheck ValidateShader(string effectType, string script)
    {
        TypeRegistration.EnsureRegistered();
        Type? type = ResolveFilterEffectType(effectType);
        if (type is null)
        {
            return new ShaderCompilationCheck(effectType, "unknown_type", $"No registered FilterEffect matches '{effectType}'.");
        }

        if (Activator.CreateInstance(type) is not IScriptCompilableEffect effect)
        {
            return new ShaderCompilationCheck(type.Name, "not_script_effect", $"{type.Name} does not accept a compilable script.");
        }

        ScriptCompilationResult result = effect.ValidateScript(script ?? string.Empty);
        string status = result.Status switch
        {
            ScriptCompilationStatus.Compiled => "compiled",
            ScriptCompilationStatus.Failed => "failed",
            _ => "unavailable",
        };
        return new ShaderCompilationCheck(type.Name, status, result.Error);
    }

    private static Type? ResolveFilterEffectType(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        string trimmed = token.Trim();
        return EnumerateRegisteredTypes()
            .Where(item => MatchesCategory(KnownLibraryItemFormats.FilterEffect, item.Category))
            .Select(item => item.Type)
            .Distinct()
            .FirstOrDefault(type =>
                string.Equals(type.Name, trimmed, StringComparison.OrdinalIgnoreCase)
                || string.Equals(type.FullName, trimmed, StringComparison.Ordinal)
                || string.Equals(IdentityHelper.WriteDiscriminator(type), trimmed, StringComparison.Ordinal));
    }

    public IReadOnlyList<EffectRecipeSummary> ListEffectRecipes(string? intent = null)
    {
        TypeRegistration.EnsureRegistered();
        return s_effectRecipeSpecs.Value
            .Select(spec => new { spec.Summary, Score = ScoreRecipe(spec.Summary, intent) })
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Summary.Name, StringComparer.Ordinal)
            .Select(item => item.Summary)
            .ToArray();
    }

    public EffectRecipe GetEffectRecipe(string? name = null, string? intent = null)
    {
        TypeRegistration.EnsureRegistered();
        EffectRecipeSpec? spec = !string.IsNullOrWhiteSpace(name)
            ? s_effectRecipeSpecs.Value.FirstOrDefault(item => string.Equals(item.Summary.Name, name, StringComparison.OrdinalIgnoreCase))
            : string.IsNullOrWhiteSpace(intent)
                ? s_effectRecipeSpecs.Value.FirstOrDefault()
            : s_effectRecipeSpecs.Value
                .Select(item => new { Spec = item, Score = ScoreRecipe(item.Summary, intent) })
                .Where(item => item.Score > 0)
                .OrderByDescending(item => item.Score)
                .ThenBy(item => item.Spec.Summary.Name, StringComparer.Ordinal)
                .Select(item => item.Spec)
                .FirstOrDefault();

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
            metadata.Notes.ToArray());
    }

    private static EffectMetadata GetEffectMetadata(Type type)
    {
        if (s_effectMetadata.TryGetValue(type, out EffectMetadata? metadata))
        {
            return metadata;
        }

        return new EffectMetadata(InferEffectTags(type.Name), []);
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

    private static int ScoreEffect(EffectSummary summary, string? intent)
    {
        return ScoreSearch(
            intent,
            summary.IntentTags,
            [summary.Name, summary.DisplayName ?? string.Empty, summary.Description ?? string.Empty],
            summary.Notes);
    }

    private static int ScoreRecipe(EffectRecipeSummary summary, string? intent)
    {
        return ScoreSearch(
            intent,
            summary.IntentTags,
            [summary.Name, summary.Description],
            summary.EffectNames.Concat(summary.Notes));
    }

    private static int ScoreSearch(
        string? query,
        IEnumerable<string> primaryTokens,
        IEnumerable<string> names,
        IEnumerable<string> secondaryTokens)
    {
        string[] tokens = SearchTokens(query);
        if (tokens.Length == 0)
        {
            return 1;
        }

        int score = 0;
        foreach (string token in tokens)
        {
            if (primaryTokens.Any(value => string.Equals(value, token, StringComparison.OrdinalIgnoreCase)))
            {
                score += 10;
            }
            else if (primaryTokens.Any(value => value.Contains(token, StringComparison.OrdinalIgnoreCase)))
            {
                score += 6;
            }
            else if (names.Any(value => value.Contains(token, StringComparison.OrdinalIgnoreCase)))
            {
                score += 4;
            }
            else if (secondaryTokens.Any(value => value.Contains(token, StringComparison.OrdinalIgnoreCase)))
            {
                score += 2;
            }
        }

        return score;
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

        List<ExampleSpec> specs =
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
                ExampleTags("starter", "empty-scene", "motion", "ribbon", "typography", "gradient", "effect")),
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
                ExampleTags("starter", "empty-scene", "motion", "orbital", "radar", "rings", "pen", "glow")),
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
                ExampleTags("starter", "empty-scene", "motion", "split-screen", "editorial", "blocks", "typography")),
            new ExampleSpec(
                CreateNewElementSkeletonExample(),
                ExampleCategories(KnownLibraryItemFormats.EngineObject, KnownLibraryItemFormats.Drawable),
                ExampleTypes(typeof(Element), typeof(TextBlock)),
                ExampleTags("targeted", "skeleton", "structure", "element", "discriminator")),
            new ExampleSpec(
                new DeclarativeExample(
                    "animate-float-property-keyframes",
                    "Minimal patch for adding keyframes to an existing float animatable property such as Opacity. Replace the placeholder Ids and property name. UseGlobalClock=false means KeyFrame.KeyTime is local to the owning timeline Element and should stay within Element.Length; set UseGlobalClock=true when the KeyTime values are scene timeline times. For non-float properties, use the matching KeyFrameAnimation<T> and KeyFrame<T> discriminator from a serialized sample.",
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
                                        [nameof(KeyFrameAnimation.UseGlobalClock)] = false,
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
                CreateNewAnimatedTextElementExample(),
                ExampleCategories(KnownLibraryItemFormats.Drawable, KnownLibraryItemFormats.EngineObject, KnownLibraryItemFormats.Easing),
                ExampleTypes(typeof(Element), typeof(TextBlock), typeof(KeyFrameAnimation<float>), typeof(KeyFrame<float>), typeof(LinearEasing), typeof(SineEaseOut)),
                ExampleTags("targeted", "keyframes", "animation", "new-object", "minimal")),
            new ExampleSpec(
                CreateBrushAndEffectExample(),
                ExampleCategories(KnownLibraryItemFormats.Drawable, KnownLibraryItemFormats.EngineObject, KnownLibraryItemFormats.Brush, KnownLibraryItemFormats.FilterEffect),
                ExampleTypes(typeof(LinearGradientBrush), typeof(GradientStop), typeof(FilterEffectGroup), typeof(Blur), typeof(Brightness)),
                ExampleTags("targeted", "gradient", "effect"))
        ];
        specs.AddRange(CreateCompositionTemplateExampleSpecs());
        return specs
            .DistinctBy(spec => spec.Example.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<ExampleSpec> CreateCompositionTemplateExampleSpecs()
    {
        var catalog = new CompositionTemplateCatalog(defaultSeed: "schema-examples");
        foreach (string name in new[]
                 {
                     "kinetic-ribbon-title",
                     "orbital-radar-map",
                     "split-screen-type-system",
                     "liquid-gradient-system",
                     "data-bar-dashboard",
                     "glitch-cutout-collage"
                 })
        {
            CompositionTemplateDetail detail = catalog.Get(name);
            CompositionRender render = catalog.Render(name, seed: $"schema-example:{name}");
            yield return new ExampleSpec(
                new DeclarativeExample(
                    $"create-empty-scene-{name}",
                    $"Composition starter generated from the {name} reusable template. Use only when the user explicitly asks for this template/starter style; otherwise treat it as a structure reference and author a custom patch.",
                    (JsonObject)render.Patch.DeepClone()),
                ExampleCategories(
                    KnownLibraryItemFormats.Drawable,
                    KnownLibraryItemFormats.EngineObject,
                    KnownLibraryItemFormats.Brush,
                    KnownLibraryItemFormats.FilterEffect,
                    KnownLibraryItemFormats.Transform,
                    KnownLibraryItemFormats.Easing,
                    KnownLibraryItemFormats.Pen),
                ExampleTypes(
                    typeof(Element),
                    typeof(RectShape),
                    typeof(EllipseShape),
                    typeof(TextBlock),
                    typeof(LinearGradientBrush),
                    typeof(RadialGradientBrush),
                    typeof(SolidColorBrush),
                    typeof(Pen),
                    typeof(FilterEffectGroup),
                    typeof(Blur),
                    typeof(DropShadow),
                    typeof(Brightness),
                    typeof(Saturate),
                    typeof(HueRotate),
                    typeof(HighContrast),
                    typeof(ColorShift),
                    typeof(MosaicEffect),
                    typeof(ShakeEffect),
                    typeof(TransformGroup),
                    typeof(TranslateTransform),
                    typeof(RotationTransform),
                    typeof(KeyFrameAnimation<float>),
                    typeof(KeyFrame<float>),
                    typeof(CubicEaseOut),
                    typeof(SineEaseInOut)),
                ExampleTags(detail.Tags.Concat(detail.StyleAxes.Values).Concat(new[] { "motion", "composition", "remotion" }).ToArray()));
        }
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
                    CreateDropShadow(0, 0, 14, "#66496a74"),
                    CreateBrightness(106))),
            CreateEffectRecipe(
                "additive-bloom",
                "Additive emissive bloom for a DUPLICATED drawable copy: soft blur + Plus (additive) blend + reduced opacity so the copy adds light over the untouched original for a true glow, not a drop-shadow fake.",
                ["glow", "bloom", "additive", "emissive", "duplicate"],
                CreateFilterEffectGroup(
                    CreateBlur(14),
                    CreateBrightness(115)),
                blendMode: BlendMode.Plus,
                opacity: 60f,
                "Apply to a COPY, never the original: call duplicate_object on the source drawable with wrapInGroup=true to get a fresh objectId inside a DrawableGroup, then apply this patch's <element-id>/<drawable-id> against that new object so the additive layer glows over the untouched original while staying gate-clean.",
                "Lower Opacity (e.g. 35-50) or switch BlendMode to Screen (14) for bright footage that blows out; BlendMode 12 is Plus (additive).",
                "Do not duplicate into two plain drawables: wrapInGroup=true makes the Element contain an IFlowOperator, avoiding the evaluate_edit_quality elementStructure Major issue."),
            CreateEffectRecipe(
                "soft-paper-depth",
                "Subtle paper/editorial depth chain that avoids heavy card shadows.",
                ["paper", "editorial", "subtle", "depth"],
                CreateFilterEffectGroup(
                    CreateDropShadow(4, 6, 5, "#33000000"),
                    CreateBrightness(103))),
            CreateEffectRecipe(
                "restrained-warm-grade",
                "Restrained photographic warmth for calmer palettes without neon saturation.",
                ["color", "grade", "warm", "palette", "restrained"],
                CreateFilterEffectGroup(
                    CreateColorGrading(
                        temperature: 15,
                        saturation: 6,
                        contrast: 4,
                        vibrance: 4))),
            CreateEffectRecipe(
                "fine-film-grain-field",
                "Fine monochrome film-grain overlay for texture when flat vector plates feel sterile.",
                ["grain", "texture", "organic", "field", "subtle"],
                CreateFilterEffectGroup(
                    CreateFilmGrainEffect(),
                    CreateBrightness(102))),
            CreateEffectRecipe(
                "editorial-color-grade",
                "Color grading chain for stronger palette separation without changing geometry.",
                ["color", "grade", "editorial", "palette"],
                CreateFilterEffectGroup(
                    CreateSaturate(114),
                    CreateHueRotate(8),
                    CreateBrightness(105),
                    CreateHighContrast(8))),
            CreateEffectRecipe(
                "organic-shader-field",
                "SKSL shader field for organic heat, ink, glass, smoke, grain, or caustic motion that would look flat as stacked gradients alone.",
                ["shader", "organic", "thermal", "ink", "glass", "field", "motion"],
                CreateFilterEffectGroup(
                    CreateOrganicShaderEffect(),
                    CreateBrightness(106))),
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
                "Vulkan-backed pixel-sort chain for harsher scanline and data-corruption looks; runs via the bundled SwiftShader software fallback when no hardware GPU is present.",
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

    private static EffectRecipeSpec CreateEffectRecipe(
        string name,
        string description,
        IReadOnlyList<string> tags,
        FilterEffectGroup effects,
        BlendMode? blendMode,
        float? opacity,
        params string[] extraNotes)
    {
        string[] effectNames = effects.Children
            .Select(effect => effect.GetType().Name)
            .ToArray();
        string[] notes = effectNames
            .SelectMany(effectName => GetEffectMetadataByName(effectName).Notes)
            .Concat(extraNotes)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return new EffectRecipeSpec(
            new EffectRecipeSummary(name, description, tags.ToArray(), effectNames, notes),
            CreateEffectPatch(effects, blendMode, opacity));
    }

    private static EffectRecipeSpec CreateSingleEffectRecipe(Type type)
    {
        FilterEffect effect = CreateRecipeEffectInstance(type);
        EffectMetadata metadata = GetEffectMetadata(type);
        string displayName = TypeNameToWords(type.Name);
        string name = $"effect-{ToKebabCase(type.Name)}";
        IEnumerable<string> recipeTags = metadata.IntentTags;
        if (type == typeof(SKSLScriptEffect))
        {
            recipeTags = recipeTags
                .Where(tag => !string.Equals(tag, "organic", StringComparison.OrdinalIgnoreCase))
                .Concat(["blank", "scaffold"]);
        }

        string[] tags = recipeTags
            .Append("single-effect")
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        string[] notes = type == typeof(SKSLScriptEffect)
            ? metadata.Notes
                .Append("This recipe is a blank pass-through SKSL scaffold for authoring your own shader; use organic-shader-field for a ready-made organic field and fine-film-grain-field for grain.")
                .Distinct(StringComparer.Ordinal)
                .ToArray()
            : metadata.Notes.ToArray();
        string description = type == typeof(SKSLScriptEffect)
            ? "Single-effect recipe for SKSL script effect. This is a blank pass-through scaffold for authoring your own shader; use organic-shader-field for a ready-made organic field or fine-film-grain-field for grain."
            : $"Single-effect recipe for {displayName}. Use this when you want to intentionally exercise the {type.Name} filter.";
        JsonObject patch = type == typeof(FilterEffectGroup)
            ? CreateEffectPatch((FilterEffectGroup)effect)
            : CreateEffectPatch(CreateFilterEffectGroup(effect));

        return new EffectRecipeSpec(
            new EffectRecipeSummary(
                name,
                description,
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
            case SKSLScriptEffect sksl:
                sksl.Script.CurrentValue = CreateNeutralShaderScript();
                break;
        }
    }

    private static JsonObject CreateEffectPatch(FilterEffectGroup effects)
    {
        return CreateEffectPatch(effects, blendMode: null, opacity: null);
    }

    private static JsonObject CreateEffectPatch(FilterEffectGroup effects, BlendMode? blendMode, float? opacity)
    {
        var drawable = new JsonObject
        {
            [nameof(CoreObject.Id)] = "<drawable-id>",
            [nameof(Drawable.FilterEffect)] = SerializeExampleObject(effects)
        };

        if (blendMode is { } mode)
        {
            drawable[nameof(Drawable.BlendMode)] = (int)mode;
        }

        if (opacity is { } value)
        {
            drawable[nameof(Drawable.Opacity)] = value;
        }

        return new JsonObject
        {
            ["Elements"] = new JsonArray(new JsonObject
            {
                [nameof(CoreObject.Id)] = "<element-id>",
                [nameof(Element.Objects)] = new JsonArray(drawable)
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

    private static SKSLScriptEffect CreateOrganicShaderEffect()
    {
        var effect = new SKSLScriptEffect();
        effect.Script.CurrentValue = CreateOrganicShaderScript();
        return effect;
    }

    private static string CreateOrganicShaderScript()
    {
        return """
               uniform shader src;
               uniform float time;
               uniform float progress;
               uniform float2 iResolution;

               half4 main(float2 fragCoord) {
                   float2 res = float2(max(iResolution.x, 1.0), max(iResolution.y, 1.0));
                   float2 uv = fragCoord / res;
                   float wave = sin(uv.x * 14.0 + time * 1.2) * 0.5 + 0.5;
                   float plume = sin((uv.x + uv.y) * 9.0 - time * 0.9) * 0.5 + 0.5;
                   half4 base = src.eval(fragCoord);
                   half3 field = half3(0.95 * wave, 0.26 + 0.45 * plume, 0.12 + 0.25 * (1.0 - wave));
                   return half4(mix(base.rgb, field, 0.35), base.a);
               }
               """;
    }

    private static string CreateNeutralShaderScript()
    {
        return """
               uniform shader src;

               half4 main(float2 fragCoord) {
                   return src.eval(fragCoord);
               }
               """;
    }

    private static SKSLScriptEffect CreateFilmGrainEffect()
    {
        var effect = new SKSLScriptEffect();
        effect.Script.CurrentValue = CreateFilmGrainScript();
        return effect;
    }

    private static string CreateFilmGrainScript()
    {
        // Must stay distinct from CreateOrganicShaderScript: monochrome low-amplitude grain, not the colored field.
        return """
               uniform shader src;
               uniform float time;
               uniform float progress;
               uniform float2 iResolution;

               half4 main(float2 fragCoord) {
                   half4 base = src.eval(fragCoord);
                   float grain = fract(sin(dot(fragCoord, float2(12.9898, 78.233)) + time * 1.7) * 43758.5453);
                   float amt = (grain - 0.5) * 0.035;
                   return half4(base.rgb + half3(amt, amt, amt), base.a);
               }
               """;
    }

    private static EffectMetadata GetEffectMetadataByName(string typeName)
    {
        KeyValuePair<Type, EffectMetadata> pair = s_effectMetadata
            .FirstOrDefault(item => string.Equals(item.Key.Name, typeName, StringComparison.Ordinal));
        return pair.Value ?? new EffectMetadata(InferEffectTags(typeName), []);
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
            [typeof(PixelSortEffect)] = Metadata(["glitch", "pixel", "scanline", "gpu"], ["Runs on the Vulkan shader backend, which falls back to the bundled SwiftShader software rasterizer when no hardware GPU is present, so it stays active; the software path is slower."]),
            [typeof(CSharpScriptEffect)] = Metadata(["advanced", "script", "programmable"], ["Prefer built-in effects for low-context agents unless script code is explicitly requested."]),
            [typeof(SKSLScriptEffect)] = Metadata(["advanced", "shader", "programmable", "organic", "procedural"], ["Requires shader source. Prefer for organic heat, ink, glass, smoke, grain, caustic, or procedural fields when blurred gradients look flat; call validate_shader to compile-check the script before apply_edit, since a compile error makes the effect a no-op: the source passes through unchanged."]),
            [typeof(GLSLScriptEffect)] = Metadata(["advanced", "shader", "gpu"], ["Needs GLSL shader source; runs on the Vulkan shader backend (hardware GPU, MoltenVK, or the bundled SwiftShader software fallback), so it does not require a dedicated GPU."]),
            [typeof(NodeGraphFilterEffect)] = Metadata(["advanced", "nodegraph", "programmable"], ["Requires a node graph resource to be useful."])
        };
    }

    private static EffectMetadata Metadata(IReadOnlyList<string> tags, IReadOnlyList<string> notes)
    {
        return new EffectMetadata(tags.Append("effect").Distinct(StringComparer.Ordinal).ToArray(), notes.ToArray());
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
        bool typeMatches = ExampleTypeMatches(spec, typeFilter);
        bool categoryMatches = string.IsNullOrWhiteSpace(categoryFilter)
                               || spec.Categories.Any(category => MatchesCategory(categoryFilter, category))
                               || SearchTokens(categoryFilter).Any(token =>
                                   spec.Tags.Any(tag => tag.Contains(token, StringComparison.OrdinalIgnoreCase))
                                   || spec.Example.Name.Contains(token, StringComparison.OrdinalIgnoreCase)
                                   || spec.Example.Description.Contains(token, StringComparison.OrdinalIgnoreCase));
        bool nameMatches = string.IsNullOrWhiteSpace(nameFilter)
                           || string.Equals(spec.Example.Name, nameFilter, StringComparison.OrdinalIgnoreCase);

        return typeMatches && categoryMatches && nameMatches;
    }

    private static bool ExampleTypeMatches(ExampleSpec spec, string? typeFilter)
    {
        if (string.IsNullOrWhiteSpace(typeFilter))
        {
            return true;
        }

        string trimmed = typeFilter.Trim();
        return spec.TypeTokens.Contains(trimmed, StringComparer.Ordinal)
               || (s_typeAliases.TryGetValue(trimmed, out string[]? aliases)
                   && aliases.Any(alias => spec.TypeTokens.Contains(alias, StringComparer.Ordinal)));
    }

    private static string[] SearchTokens(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        return query
            .Split([' ', '-', '_', '/', ',', ';', ':', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => token.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static DeclarativeExample CreateNewElementSkeletonExample()
    {
        string elementType = IdentityHelper.WriteDiscriminator(typeof(Element));
        string textType = IdentityHelper.WriteDiscriminator(typeof(TextBlock));

        return new DeclarativeExample(
            "insert-new-element-skeleton",
            "Minimal structure-only patch for inserting one new timeline Element with one new TextBlock object. Use this to copy the required Element/Object $type shape without cloning a full-scene starter. Omit Id for genuinely new Elements and Objects; keep Id only when modifying an existing parent.",
            new JsonObject
            {
                ["Elements"] = new JsonArray(new JsonObject
                {
                    ["$type"] = elementType,
                    [nameof(CoreObject.Name)] = "new-text-element",
                    [nameof(Element.Start)] = TimeSpan.Zero.ToString("c"),
                    [nameof(Element.Length)] = TimeSpan.FromSeconds(2).ToString("c"),
                    [nameof(Element.ZIndex)] = 10,
                    [nameof(Element.Objects)] = new JsonArray(new JsonObject
                    {
                        ["$type"] = textType,
                        [nameof(CoreObject.Name)] = "new-text",
                        [nameof(TextBlock.Text)] = "Title"
                    })
                })
            });
    }

    private static DeclarativeExample CreateNewAnimatedTextElementExample()
    {
        string elementType = IdentityHelper.WriteDiscriminator(typeof(Element));
        string textType = IdentityHelper.WriteDiscriminator(typeof(TextBlock));
        JsonObject opacityAnimation = CreateFloatAnimation(
            (0, 0, typeof(LinearEasing)),
            (0.35, 100, typeof(SineEaseOut)),
            (1.65, 100, typeof(LinearEasing)),
            (2, 0, typeof(LinearEasing)));
        opacityAnimation[nameof(KeyFrameAnimation.UseGlobalClock)] = false;

        return new DeclarativeExample(
            "insert-new-animated-text-keyframes",
            "Minimal patch for inserting one new TextBlock object with a valid KeyFrameAnimation<float> on Opacity. New Elements and Objects omit Id; use schema-returned discriminators and keep UseGlobalClock=false key times inside Element.Length.",
            new JsonObject
            {
                ["Elements"] = new JsonArray(new JsonObject
                {
                    ["$type"] = elementType,
                    [nameof(CoreObject.Name)] = "animated-text-element",
                    [nameof(Element.Start)] = TimeSpan.Zero.ToString("c"),
                    [nameof(Element.Length)] = TimeSpan.FromSeconds(2).ToString("c"),
                    [nameof(Element.ZIndex)] = 20,
                    [nameof(Element.Objects)] = new JsonArray(new JsonObject
                    {
                        ["$type"] = textType,
                        [nameof(CoreObject.Name)] = "animated-text",
                        [nameof(TextBlock.Text)] = "Animated",
                        ["Animations"] = new JsonObject
                        {
                            ["Opacity"] = opacityAnimation
                        }
                    })
                })
            });
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
                Text = { CurrentValue = "Beutl motion" },
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
            "Patch snippet for an empty scene. Use as a schema reference or explicit starter; for original creative briefs, adapt the structure instead of copying it unchanged. It appends visible elements without Id fields so the toolkit mints stable Ids.",
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
                Text = { CurrentValue = "Orbit map" },
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
                Text = { CurrentValue = "Signal route notes" },
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
            "Patch snippet for an empty scene with orbit rings, a moving signal node, scan sweep, glow, pens, gradients, and title typography. Use only when the user explicitly asks for an orbit/radar style; otherwise treat it as a shape/effect reference, not a full-scene starter.",
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
                Text = { CurrentValue = "Frame flow" },
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
                Text = { CurrentValue = "Kinetic layout notes" },
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
                Text = { CurrentValue = "Variant 02" },
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
            "Patch snippet for an empty scene with a split-screen editorial layout, animated panels, kinetic typography, blocks, gradients, and layered effects. Use as an explicit starter or adapt the parts into an original brief-driven composition.",
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

    private static ColorGrading CreateColorGrading(
        float temperature,
        float saturation = 0,
        float contrast = 0,
        float vibrance = 0,
        float tint = 0)
    {
        var colorGrading = new ColorGrading();
        colorGrading.Temperature.CurrentValue = temperature;
        colorGrading.Saturation.CurrentValue = saturation;
        colorGrading.Contrast.CurrentValue = contrast;
        colorGrading.Vibrance.CurrentValue = vibrance;
        colorGrading.Tint.CurrentValue = tint;
        return colorGrading;
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
                : null,
            EnumValues: EnumJsonValueNormalizer.GetEnumNames(property.ValueType),
            UsageHint: CreatePropertyUsageHint(property.ValueType, property.IsAnimatable));
    }

    private static string? CreatePropertyUsageHint(Type valueType, bool animatable)
    {
        Type type = Nullable.GetUnderlyingType(valueType) ?? valueType;
        List<string> hints = [];
        if (type == typeof(Color))
        {
            hints.Add("Use serialized Beutl color values such as '#ffffb34d' or copy the exact shape returned by read_document/get_schema; do not use palette names such as 'Amber'.");
        }
        else if (type == typeof(Pen))
        {
            hints.Add("Pen is a typed EngineObject value. Use the Pen shape returned by get_schema/read_document, including its '$type' discriminator and PascalCase properties such as Brush and Thickness, or omit Pen when no stroke is needed.");
        }
        else if (typeof(EngineObject).IsAssignableFrom(type))
        {
            hints.Add("Use a concrete '$type' discriminator returned by get_schema for this EngineObject value and only the returned PascalCase property names.");
        }

        if (animatable)
        {
            string animationDiscriminator = IdentityHelper.WriteDiscriminator(typeof(KeyFrameAnimation<float>));
            string keyFrameDiscriminator = IdentityHelper.WriteDiscriminator(typeof(KeyFrame<float>));
            hints.Add(
                "Animate through Animations.<Property>.KeyFrames. Use the angle-bracket $type form shown here, NOT the reflection 'Name`1[[...]]' form: for a float property, Animations.<Property> = { \"$type\": \""
                + animationDiscriminator
                + "\", \"KeyFrames\": [ { \"$type\": \""
                + keyFrameDiscriminator
                + "\", \"KeyTime\": \"00:00:00\", \"Value\": 0, \"Easing\": \"[Beutl.Engine]Beutl.Animation.Easings:CubicEaseOut\" } ] }. Substitute this property's value type for the <...> generic argument. UseGlobalClock=false uses Element-local KeyTime values, and UseGlobalClock=true uses scene timeline KeyTime values.");
        }

        return hints.Count == 0 ? null : string.Join(" ", hints);
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
               || string.Equals(typeFilter, type.Name, StringComparison.Ordinal)
               || (s_typeAliases.TryGetValue(typeFilter.Trim(), out string[]? aliases)
                   && aliases.Contains(type.Name, StringComparer.Ordinal));
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

    private static (string? TypeFilter, string? CategoryFilter) NormalizeFilters(string? typeFilter, string? categoryFilter)
    {
        if (string.IsNullOrWhiteSpace(typeFilter) && IsTextCategoryAlias(categoryFilter))
        {
            return (categoryFilter, null);
        }

        return (typeFilter, categoryFilter);
    }

    private static bool IsTextCategoryAlias(string? categoryFilter)
    {
        if (string.IsNullOrWhiteSpace(categoryFilter))
        {
            return false;
        }

        string trimmed = categoryFilter.Trim();
        return string.Equals(trimmed, "text", StringComparison.OrdinalIgnoreCase)
               || string.Equals(trimmed, "typography", StringComparison.OrdinalIgnoreCase)
               || string.Equals(trimmed, "label", StringComparison.OrdinalIgnoreCase);
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
        IReadOnlyList<string> Notes);
}
