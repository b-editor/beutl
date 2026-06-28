using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Beutl.AgentToolkit.Common;
using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
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
                            new RotationTransform(-10),
                            new TranslateTransform(-120, 38)
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
                            new RotationTransform(0),
                            new TranslateTransform(-270, 0)
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
                            new RotationTransform(0),
                            new TranslateTransform(-270, 0)
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
                            new RotationTransform(0),
                            new TranslateTransform(-270, 0)
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
}
