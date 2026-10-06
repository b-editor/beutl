using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Services;

namespace Beutl.AgentToolkit.Schema;

public sealed partial class SchemaGenerator
{
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
                CreateCameraRigPushInExample(),
                ExampleCategories(
                    KnownLibraryItemFormats.Drawable,
                    KnownLibraryItemFormats.EngineObject,
                    KnownLibraryItemFormats.Transform,
                    KnownLibraryItemFormats.Easing),
                ExampleTypes(
                    typeof(Element),
                    typeof(DrawableGroup),
                    typeof(PortalObject),
                    typeof(TransformGroup),
                    typeof(TranslateTransform),
                    typeof(ScaleTransform),
                    typeof(KeyFrameAnimation<float>),
                    typeof(KeyFrame<float>),
                    typeof(SineEaseInOut)),
                ExampleTags("targeted", "camera", "camera-rig", "push-in", "keyframes", "animation", "group")),
            new ExampleSpec(
                CreateCameraRigPortalExample(),
                ExampleCategories(
                    KnownLibraryItemFormats.Drawable,
                    KnownLibraryItemFormats.EngineObject,
                    KnownLibraryItemFormats.Transform,
                    KnownLibraryItemFormats.Easing),
                ExampleTypes(
                    typeof(Element),
                    typeof(DrawableGroup),
                    typeof(PortalObject),
                    typeof(TransformGroup),
                    typeof(TranslateTransform),
                    typeof(ScaleTransform),
                    typeof(KeyFrameAnimation<float>),
                    typeof(KeyFrame<float>),
                    typeof(SineEaseInOut)),
                ExampleTags("targeted", "camera", "camera-rig", "portal", "flow", "timeline", "keyframes", "animation", "group")),
            new ExampleSpec(
                CreateBrushAndEffectExample(),
                ExampleCategories(KnownLibraryItemFormats.Drawable, KnownLibraryItemFormats.EngineObject, KnownLibraryItemFormats.Brush, KnownLibraryItemFormats.FilterEffect),
                ExampleTypes(typeof(LinearGradientBrush), typeof(GradientStop), typeof(FilterEffectGroup), typeof(Blur), typeof(Brightness)),
                ExampleTags("targeted", "gradient", "effect")),
            new ExampleSpec(
                CreateGeometryShapePathExample(),
                ExampleCategories(KnownLibraryItemFormats.Drawable, KnownLibraryItemFormats.EngineObject, KnownLibraryItemFormats.Brush),
                ExampleTypes(typeof(GeometryShape), typeof(PathGeometry), typeof(PathFigure), typeof(LineSegment), typeof(SolidColorBrush)),
                ExampleTags("targeted", "geometry", "path", "vector", "shape"))
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
}
