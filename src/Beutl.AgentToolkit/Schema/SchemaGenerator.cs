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

    public IReadOnlyList<DeclarativeExample> GenerateExamples(string? typeFilter = null, string? categoryFilter = null)
    {
        TypeRegistration.EnsureRegistered();
        return CreateExamples(typeFilter, categoryFilter);
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

    private static IReadOnlyList<DeclarativeExample> CreateExamples(string? typeFilter, string? categoryFilter)
    {
        ExampleSpec[] examples = s_exampleSpecs.Value;
        if (string.IsNullOrWhiteSpace(typeFilter) && string.IsNullOrWhiteSpace(categoryFilter))
        {
            return examples.Select(CloneExample).ToArray();
        }

        return examples
            .Where(spec => ExampleMatches(spec, typeFilter, categoryFilter))
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
                    typeof(SineEaseInOut))),
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
                ExampleTypes(typeof(KeyFrameAnimation<float>), typeof(KeyFrame<float>), typeof(LinearEasing), typeof(SineEaseOut))),
            new ExampleSpec(
                CreateBrushAndEffectExample(),
                ExampleCategories(KnownLibraryItemFormats.Drawable, KnownLibraryItemFormats.EngineObject, KnownLibraryItemFormats.Brush, KnownLibraryItemFormats.FilterEffect),
                ExampleTypes(typeof(LinearGradientBrush), typeof(GradientStop), typeof(FilterEffectGroup), typeof(Blur), typeof(Brightness)))
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

    private static bool ExampleMatches(ExampleSpec spec, string? typeFilter, string? categoryFilter)
    {
        bool typeMatches = string.IsNullOrWhiteSpace(typeFilter)
                           || spec.TypeTokens.Contains(typeFilter, StringComparer.Ordinal);
        bool categoryMatches = string.IsNullOrWhiteSpace(categoryFilter)
                               || spec.Categories.Any(category => MatchesCategory(categoryFilter, category));

        return typeMatches && categoryMatches;
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
        IReadOnlyList<string> TypeTokens);
}
