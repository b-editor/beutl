using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.Animation;
using Beutl.Engine;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Services;

namespace Beutl.AgentToolkit.Schema;

public sealed partial class SchemaGenerator
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

    private static readonly (string[] Keywords, string[] Tags)[] s_inferredEffectTagRules =
    [
        (["blur", "shadow", "stroke"], ["glow", "depth", "outline"]),
        (["color", "hue", "saturate", "brightness", "contrast", "gamma", "threshold", "invert", "curve", "luma"], ["color", "grade"]),
        (["mosaic", "pixel", "shift", "shake", "split"], ["glitch", "stylize"]),
        (["key"], ["keying", "transparent"]),
        (["transform", "displacement", "path", "delay", "layer", "blend"], ["motion", "composite"]),
        (["script", "nodegraph"], ["advanced", "programmable"])
    ];

    public CapabilitySchema Generate(
        string? typeFilter = null,
        string? categoryFilter = null,
        bool includeProperties = true,
        bool includeExamples = true)
    {
        TypeRegistration.EnsureRegistered();
        (typeFilter, categoryFilter) = NormalizeFilters(typeFilter, categoryFilter);

        List<TypeDescriptor> types = [];
        foreach ((string category, Type type) in EnumerateRegisteredTypes()
                     .Where(item => MatchesCategory(categoryFilter, item.Category))
                     .DistinctBy(item => item.Type))
        {
            string discriminator = IdentityHelper.WriteDiscriminator(type);
            if (!Matches(typeFilter, type, discriminator))
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

        // Non-serialized scalar properties (e.g. Hierarchical.HierarchicalParent) never appear in
        // documents and the applier ignores them; advertising them would invite silent no-op edits.
        // Non-serialized lists (Objects, Markers, ...) stay: they are applied by explicit handlers.
        return PropertyRegistry.GetRegistered(type)
            .Where(property =>
                property.GetMetadata<CorePropertyMetadata>(type).ShouldSerialize
                || typeof(Collections.ICoreList).IsAssignableFrom(property.PropertyType))
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

        return engineObject.Properties.Select(property => CreateProperty(type, property)).ToArray();
    }

    private static PropertyDescriptor CreateProperty(Type ownerType, IProperty property)
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
            UsageHint: CreatePropertyUsageHint(ownerType, property.Name, property.ValueType, property.IsAnimatable, Common.ReferenceProperties.Describe(property) is not null));
    }

    private static string? CreatePropertyUsageHint(Type ownerType, string propertyName, Type valueType, bool animatable, bool isReference = false)
    {
        Type type = Nullable.GetUnderlyingType(valueType) ?? valueType;
        List<string> hints = [];
        if (isReference)
        {
            hints.Add(
                $"{propertyName} references an existing {type.Name} in the project and is set through the Expressions form, not as a direct value. Find the target Id via read_document/list tools, then set Expressions.{propertyName} = {{\"ObjectId\": \"<guid>\"}}. A direct value on {propertyName} is rejected.");
        }
        else if (ownerType == typeof(PortalObject) && propertyName == nameof(PortalObject.Count))
        {
            hints.Add("Portal flow intake. A PortalObject placed immediately before a flow operator (DrawableGroup, DrawableDecorator, SoundGroup, Scene3D) in Element.Objects pulls every active timeline Element whose ZIndex lies in the inclusive span portalZIndex+1..portalZIndex+Count out of normal composition and feeds their output into that flow operator (e.g. as DrawableGroup children), while each keeps its own Start/Length. Count is a ZIndex span, not an element count: all active Elements on those rows are pulled, and empty rows contribute nothing. Count=0 pulls no timeline rows; the operator then consumes whatever is already in the Element's flow — only its nested Children when the portal is the Element's first object — and Clear=true explicitly discards earlier same-Element flow first. Keep grouped rows directly above the rig Element, and note that pulled Elements render ungrouped at times when the rig Element is not active. See get_examples insert-camera-rig-portal.");
        }
        else if (ownerType == typeof(PortalObject) && propertyName == nameof(PortalObject.Clear))
        {
            hints.Add("When true, the portal empties the Element's object flow before pulling, so the following flow operator consumes only this portal's intake instead of also consuming earlier objects in the same Element.");
        }

        if (type == typeof(Color))
        {
            hints.Add("Use serialized Beutl color values such as '#ffffb34d' or copy the exact shape returned by read_document/get_schema; do not use palette names such as 'Amber'.");
        }
        else if (type == typeof(Pen))
        {
            hints.Add(ValidationEvaluator.PenValueHint);
        }
        else if (typeof(Geometry).IsAssignableFrom(type))
        {
            hints.Add("Geometry is authored with typed segment objects, not an SVG path string. Use a PathGeometry ($type discriminator) whose Figures hold PathFigure objects, each with a StartPoint ('x, y') plus Segments of LineSegment/CubicBezierSegment/QuadraticBezierSegment/ConicSegment/ArcSegment; set IsClosed=true for filled shapes. Call get_examples for 'insert-new-geometry-shape-path' to copy a working GeometryShape+PathGeometry patch. RectGeometry/EllipseGeometry/RoundedRectGeometry are simpler alternatives for basic shapes. A GeometryShape's drawn center lands at the alignment-resolved center PLUS the path bounds origin, so author path coordinates with the artwork's top-left at (0, 0) (all coordinates non-negative); paths centered on (0, 0) shift up-left by half their size, and scene-absolute coordinates shift by their full offset. If coordinates cannot be normalized, add TranslateTransform(-boundsX, -boundsY) or check measure_object_bounds' geometryBoundsOrigin. RectShape/EllipseShape are unaffected because their geometry bounds start at the origin.");
        }
        else if (!isReference && typeof(EngineObject).IsAssignableFrom(type))
        {
            hints.Add(ValidationEvaluator.EngineObjectValueHint);
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
