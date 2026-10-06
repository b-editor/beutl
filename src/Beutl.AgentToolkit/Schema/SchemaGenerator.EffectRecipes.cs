using System.Text;
using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Services;
using static Beutl.AgentToolkit.Schema.SamplePatchBuilder;

namespace Beutl.AgentToolkit.Schema;

public sealed partial class SchemaGenerator
{
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
                "No matching recipe is available; this does not establish that the intended expression is unsupported. Compose supported building blocks using targeted get_schema calls, or use list_effect_recipes with an intent filter if another example would help."));
        }

        return new EffectRecipe(
            spec.Summary.Name,
            spec.Summary.Description,
            spec.Summary.IntentTags.ToArray(),
            spec.Summary.EffectNames.ToArray(),
            spec.Summary.Notes.ToArray(),
            (JsonObject)spec.Patch.DeepClone(),
            spec.Summary.Semantic);
    }

    private static EffectRecipeSpec[] CreateEffectRecipeSpecs()
    {
        EffectRecipeSpec[] curated =
        [
            CreateEffectRecipe(
                "glow-depth",
                "Cheap single-target inline luminous glow for text, nodes, panels, and highlight shapes; keeps the source sharp while a palette-neutral default glow color can be tinted to the accent. Use additive-bloom for a premium duplicated emissive bloom.",
                ["glow", "depth", "title", "node"],
                CreateFilterEffectGroup(
                    CreateDropShadow(0, 0, 16, "#b0ffffff"),
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
                "Apply to a COPY, never the original: call duplicate_object on the source drawable with wrapInGroup=true to get a fresh objectId inside a DrawableGroup, then apply this patch's <element-id>/<drawable-id> against that new object so the additive layer glows over the untouched original.",
                "Lower Opacity (e.g. 35-50) or switch BlendMode to Screen (14) for bright footage that blows out; BlendMode 12 is Plus (additive).",
                "Do not duplicate into two plain drawables: wrapInGroup=true makes the Element contain an IFlowOperator, so both drawables flow through the group."),
            CreateEffectRecipe(
                "screen-light-leak",
                "Soft Screen light-leak and lens-wash glow for a duplicated drawable copy: gentler than additive Plus and self-limiting on bright footage.",
                ["glow", "light-leak", "screen", "atmospheric", "duplicate"],
                CreateFilterEffectGroup(
                    CreateBlur(18),
                    CreateBrightness(120)),
                blendMode: BlendMode.Screen,
                opacity: 50f,
                "Apply to a COPY, never the original: call duplicate_object on the source drawable with wrapInGroup=true and apply this patch to the returned copy to preserve the source drawable.",
                "BlendMode 14 is Screen; it cannot exceed white, making it the safe choice for bright content that would blow out under Plus."),
            CreateEffectRecipe(
                "multiply-contrast-glaze",
                "Multiply glaze for a duplicated drawable copy: a darkened blurred self-composite that deepens shadows and contrast for a moody grade.",
                ["grade", "contrast", "multiply", "moody", "duplicate"],
                CreateFilterEffectGroup(
                    CreateBlur(10),
                    CreateBrightness(80)),
                blendMode: BlendMode.Multiply,
                opacity: 50f,
                "Apply to a COPY, never the original: call duplicate_object on the source drawable with wrapInGroup=true and apply this patch to the returned copy to preserve the source drawable.",
                "This is the self-composite darkening glaze part; a true edge vignette also needs the drawable's own dark radial fill."),
            CreateEffectRecipe(
                "chromatic-aberration-lite",
                "Subtle chromatic aberration and lens character: a gentle red/blue split, unlike the aggressive ColorShift inside digital-glitch.",
                ["chromatic", "aberration", "lens", "subtle", "stylize"],
                CreateFilterEffectGroup(
                    CreateColorShift(3, 0, -3, 0))),
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
                    CreateFlatShadow(138, 34, "#aa05121f")),
                blendMode: null,
                opacity: null,
                "The neon cyan stroke (#ff36f0ff) is a STYLIZED ACCENT default; tint or override it to match the palette's accent."),
            CreateEffectRecipe(
                "pixel-sort-distortion",
                "Vulkan-backed pixel-sort chain for harsher scanline and data-corruption looks; runs via the bundled SwiftShader software fallback when no hardware GPU is present.",
                ["glitch", "pixel", "scanline", "gpu"],
                CreateFilterEffectGroup(
                    CreatePixelSort(),
                    CreateColorShift(8, 0, -8, 0))),
            CreateTransitionRecipe(
                "transition-overlap-dissolve-transform-continuation",
                "Overlap dissolve template with outgoing/incoming opacity ramps plus a shared slow transform continuation across the boundary.",
                "time passage / soft topic shift",
                ["transition", "dissolve", "opacity", "continuity", "time-passage", "soft-topic-shift"],
                ["OpacityKeyframes", nameof(TranslateTransform)],
                CreateOverlapDissolveTransitionPatch(),
                "Overlap outgoing and incoming Elements by about 8-16 frames; fade one down while the other fades up.",
                "Keep one background or subject transform drifting through the overlap so the dissolve reads as continuity instead of a simple crossfade."),
            CreateTransitionRecipe(
                "transition-directional-sweep-wipe",
                "Directional sweep or wipe template using a transform-animated bridge plate that crosses the cut boundary.",
                "location or topic change",
                ["transition", "sweep", "wipe", "directional", "topic-change", "location-change"],
                [nameof(RectShape), nameof(TranslateTransform), "OpacityKeyframes"],
                CreateDirectionalSweepTransitionPatch(),
                "Place the sweep Element over both adjacent shots and animate its TranslateTransform along the chosen screen direction.",
                "Use one direction consistently for a sequence unless the brief records a deliberate directional grammar."),
            CreateTransitionRecipe(
                "transition-mask-reveal",
                "Mask-reveal template for introducing or unveiling a new subject with a moving reveal plate or clipping edge.",
                "introduction / unveiling",
                ["transition", "mask", "reveal", "introduction", "unveiling"],
                [nameof(RectShape), nameof(TranslateTransform), "OpacityKeyframes"],
                CreateMaskRevealTransitionPatch(),
                "Use this when the incoming subject should feel discovered, introduced, or unveiled rather than simply cut in.",
                "Pair the reveal with a named mask/reveal Element and align its Start/Length to the incoming shot."),
            CreateTransitionRecipe(
                "transition-dip-to-color",
                "Dip-to-color template using a full-frame plate whose opacity peaks at the cut boundary.",
                "chapter break",
                ["transition", "dip", "dip-to-color", "chapter-break", "full-frame-plate"],
                [nameof(RectShape), "OpacityKeyframes"],
                CreateDipToColorTransitionPatch(),
                "Use a neutral, brand, or palette-derived color plate and keep the peak brief: usually 2-6 frames at full opacity.",
                "Best for chapter breaks, section dividers, or intentional breath points; avoid mixing it randomly with dissolves and wipes."),
            CreateTransitionRecipe(
                "transition-match-move-cut",
                "Match-move cut template that aligns outgoing and incoming focal elements by position, scale, or motion direction at the boundary.",
                "conceptual rhyme",
                ["transition", "match-cut", "match-move", "conceptual-rhyme", "continuity"],
                [nameof(TranslateTransform), nameof(ScaleTransform)],
                CreateMatchMoveCutTransitionPatch(),
                "Align the outgoing and incoming objects' focal position and motion vector at the boundary, then let the incoming object continue the move.",
                "Use this for visual rhymes, object-to-object associations, or conceptual continuity between shots.")
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
        return CreateEffectRecipe(name, description, tags, effects, blendMode: null, opacity: null);
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
        bool containsPrebuiltSkslScript = ContainsPrebuiltSkslScript(effects);
        string[] notes = effectNames
            .SelectMany(effectName => GetEffectMetadataByName(effectName).Notes)
            .Where(note => !containsPrebuiltSkslScript || !IsShaderSourceRequirementNote(note))
            .Concat(extraNotes)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return new EffectRecipeSpec(
            new EffectRecipeSummary(name, description, tags.ToArray(), effectNames, notes),
            CreateEffectPatch(effects, blendMode, opacity));
    }

    private static EffectRecipeSpec CreateTransitionRecipe(
        string name,
        string description,
        string semantic,
        IReadOnlyList<string> tags,
        IReadOnlyList<string> effectNames,
        JsonObject patch,
        params string[] notes)
    {
        return new EffectRecipeSpec(
            new EffectRecipeSummary(
                name,
                description,
                tags
                    .Append("transition")
                    .Distinct(StringComparer.Ordinal)
                    .ToArray(),
                effectNames.ToArray(),
                notes.Distinct(StringComparer.Ordinal).ToArray(),
                semantic),
            patch);
    }

    private static JsonObject CreateOverlapDissolveTransitionPatch()
    {
        return CreateTransitionPatch(
            new JsonObject
            {
                [nameof(CoreObject.Id)] = "<outgoing-drawable-id>",
                ["Animations"] = new JsonObject
                {
                    [nameof(Drawable.Opacity)] = CreateFloatAnimationWithLocalClock(
                        (0, 100, typeof(SineEaseInOut)),
                        (0.28, 0, typeof(SineEaseInOut)))
                }
            },
            new JsonObject
            {
                [nameof(CoreObject.Id)] = "<incoming-drawable-id>",
                ["Animations"] = new JsonObject
                {
                    [nameof(Drawable.Opacity)] = CreateFloatAnimationWithLocalClock(
                        (0, 0, typeof(SineEaseInOut)),
                        (0.28, 100, typeof(SineEaseInOut)))
                }
            });
    }

    private static JsonObject CreateDirectionalSweepTransitionPatch()
    {
        return CreateTransitionPatch(new JsonObject
        {
            [nameof(CoreObject.Id)] = "<sweep-plate-or-mask-id>",
            ["Name"] = "[role:decorative] [role:motion] directional transition sweep",
            ["Animations"] = new JsonObject
            {
                [nameof(Drawable.Opacity)] = CreateFloatAnimationWithLocalClock(
                    (0, 0, typeof(CubicEaseOut)),
                    (0.10, 100, typeof(CubicEaseOut)),
                    (0.34, 100, typeof(SineEaseInOut)),
                    (0.44, 0, typeof(SineEaseInOut)))
            },
            ["PlacementGuidance"] = "Animate the object's TranslateTransform from just outside the outgoing frame to just outside the incoming frame across the cut."
        });
    }

    private static JsonObject CreateMaskRevealTransitionPatch()
    {
        return CreateTransitionPatch(new JsonObject
        {
            [nameof(CoreObject.Id)] = "<incoming-reveal-mask-or-plate-id>",
            ["Name"] = "[role:decorative] [role:motion] mask reveal transition",
            ["Animations"] = new JsonObject
            {
                [nameof(Drawable.Opacity)] = CreateFloatAnimationWithLocalClock(
                    (0, 0, typeof(CubicEaseOut)),
                    (0.16, 100, typeof(CubicEaseOut)),
                    (0.44, 100, typeof(SineEaseInOut)))
            },
            ["PlacementGuidance"] = "Move a clipping edge, mask plate, or reveal shape across the incoming subject while the incoming Element begins."
        });
    }

    private static JsonObject CreateDipToColorTransitionPatch()
    {
        return CreateTransitionPatch(new JsonObject
        {
            [nameof(CoreObject.Id)] = "<full-frame-dip-plate-id>",
            ["Name"] = "[role:background] dip-to-color transition plate",
            ["Animations"] = new JsonObject
            {
                [nameof(Drawable.Opacity)] = CreateFloatAnimationWithLocalClock(
                    (0, 0, typeof(SineEaseInOut)),
                    (0.16, 100, typeof(SineEaseInOut)),
                    (0.32, 0, typeof(SineEaseInOut)))
            },
            ["PlacementGuidance"] = "Make this a full-frame RectShape spanning both adjacent shots, centered so opacity peaks exactly on the boundary."
        });
    }

    private static JsonObject CreateMatchMoveCutTransitionPatch()
    {
        return CreateTransitionPatch(
            new JsonObject
            {
                [nameof(CoreObject.Id)] = "<outgoing-focal-drawable-id>",
                ["PlacementGuidance"] = "End this object's transform at the same screen position, scale, and direction as the incoming focal object."
            },
            new JsonObject
            {
                [nameof(CoreObject.Id)] = "<incoming-focal-drawable-id>",
                ["PlacementGuidance"] = "Start this object's transform at the outgoing object's boundary pose, then continue the motion vector after the cut."
            });
    }

    private static JsonObject CreateTransitionPatch(params JsonObject[] objects)
    {
        return new JsonObject
        {
            ["Elements"] = new JsonArray(objects
                .Select((obj, index) => new JsonObject
                {
                    [nameof(CoreObject.Id)] = $"<transition-element-id-{index + 1}>",
                    [nameof(Element.Objects)] = new JsonArray(obj)
                })
                .ToArray<JsonNode?>())
        };
    }

    private static JsonObject CreateFloatAnimationWithLocalClock(params (double Seconds, float Value, Type Easing)[] keyframes)
    {
        JsonObject animation = CreateFloatAnimation(keyframes);
        animation[nameof(KeyFrameAnimation.UseGlobalClock)] = false;
        return animation;
    }

    private static bool ContainsPrebuiltSkslScript(FilterEffectGroup effects)
    {
        return effects.Children
            .OfType<SKSLScriptEffect>()
            .Any(effect => !string.IsNullOrWhiteSpace(effect.Script.CurrentValue));
    }

    private static bool IsShaderSourceRequirementNote(string note)
    {
        return note.Contains("Requires shader source", StringComparison.OrdinalIgnoreCase)
               || note.Contains("validate_shader", StringComparison.OrdinalIgnoreCase);
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
                .Append("Colour space: the shader runs in LINEAR, premultiplied-alpha space. src.eval() returns linear premultiplied values and main must return the same; the sRGB encode happens after the effect. An sRGB constant pasted straight into the shader therefore reads far brighter than intended — convert it (roughly pow(c, 2.2)) before mixing, and un-premultiply before any operation that assumes straight alpha.")
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
                dropShadow.Position.CurrentValue = new Point(8, 10);
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
                invert.Amount.CurrentValue = 80;
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
            [nameof(Drawable.FilterEffect)] = SerializeWithoutIds(effects)
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
                   half luminance = dot(base.rgb, half3(0.2126, 0.7152, 0.0722));
                   half contrast = half(0.90 + wave * 0.22);
                   half lift = half((plume - 0.5) * 0.10);
                   half3 modulated = (base.rgb - half3(luminance)) * contrast + half3(luminance + lift);
                   half3 field = clamp(modulated * half3(1.05, 1.0, 0.95), half3(0.0), half3(1.0));
                   return half4(mix(base.rgb, field, 0.2), base.a);
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

    private static ColorShift CreateColorShift(int redX, int redY, int blueX, int blueY)
    {
        var colorShift = new ColorShift();
        colorShift.RedOffset.CurrentValue = new PixelPoint(redX, redY);
        colorShift.GreenOffset.CurrentValue = new PixelPoint(0, 0);
        colorShift.BlueOffset.CurrentValue = new PixelPoint(blueX, blueY);
        colorShift.AlphaOffset.CurrentValue = new PixelPoint(0, 0);
        return colorShift;
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
}
