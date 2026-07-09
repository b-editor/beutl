using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Schema;
using Beutl.Animation.Easings;
using Beutl.Audio.Effects;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.NodeGraph;
using Beutl.Services;

namespace Beutl.AgentToolkit.Tests.Schema;

public sealed class SchemaGenerationTests
{
    [Test]
    public void Schema_contains_builtin_parameters_and_fake_extension_type()
    {
        LibraryService.Current.Register<FakeExtensionDrawable>("Fake.Extension.Drawable", "Fake Extension");

        var generator = new SchemaGenerator();
        CapabilitySchema schema = generator.Generate();

        TypeDescriptor textBlock = schema.Types.Single(type => type.Type == typeof(TextBlock).FullName);
        PropertyDescriptor size = textBlock.Properties.Single(property => property.Name == nameof(TextBlock.Size));
        PropertyDescriptor blendMode = textBlock.Properties.Single(property => property.Name == nameof(TextBlock.BlendMode));
        PropertyDescriptor fontWeight = textBlock.Properties.Single(property => property.Name == nameof(TextBlock.FontWeight));
        TypeDescriptor linearGradient = schema.Types.Single(type => type.Type == typeof(LinearGradientBrush).FullName);
        PropertyDescriptor gradientStops = linearGradient.Properties.Single(property => property.Name == nameof(GradientBrush.GradientStops));
        TypeDescriptor filterGroup = schema.Types.Single(type => type.Type == typeof(FilterEffectGroup).FullName);
        PropertyDescriptor effectChildren = filterGroup.Properties.Single(property => property.Name == nameof(FilterEffectGroup.Children));
        TypeDescriptor audioEffectGroup = schema.Types.Single(type => type.Type == typeof(AudioEffectGroup).FullName);
        PropertyDescriptor audioEffectChildren = audioEffectGroup.Properties.Single(property => property.Name == nameof(AudioEffectGroup.Children));
        TypeDescriptor transformGroup = schema.Types.Single(type => type.Type == typeof(TransformGroup).FullName);
        PropertyDescriptor transformChildren = transformGroup.Properties.Single(property => property.Name == nameof(TransformGroup.Children));
        TypeDescriptor rectGeometry = schema.Types.Single(type => type.Type == typeof(RectGeometry).FullName);
        TypeDescriptor pen = schema.Types.Single(type => type.Type == typeof(Pen).FullName);
        TypeDescriptor gradientStop = schema.Types.Single(type => type.Type == typeof(GradientStop).FullName);
        PropertyDescriptor gradientStopColor = gradientStop.Properties.Single(property => property.Name == nameof(GradientStop.Color));
        PropertyDescriptor penBrush = pen.Properties.Single(property => property.Name == nameof(Pen.Brush));
        TypeDescriptor linearEasing = schema.Types.Single(type => type.Type == typeof(LinearEasing).FullName);
        TypeDescriptor nodeGraphDrawable = schema.Types.Single(type => type.Type == typeof(NodeGraphDrawable).FullName);
        TypeDescriptor nodeGraphFilterEffect = schema.Types.Single(type => type.Type == typeof(NodeGraphFilterEffect).FullName);
        TypeDescriptor graphModel = schema.Types.Single(type => type.Type == typeof(GraphModel).FullName);
        TypeDescriptor fake = schema.Types.Single(type => type.Type == typeof(FakeExtensionDrawable).FullName);
        string emptySceneExample = schema.Examples.Single(example => example.Name == "create-empty-scene-motion-graphics").Patch.ToJsonString();
        string brushEffectExample = schema.Examples.Single(example => example.Name == "apply-gradient-fill-and-effect-chain").Patch.ToJsonString();
        string newAnimatedTextExample = schema.Examples.Single(example => example.Name == "insert-new-animated-text-keyframes").Patch.ToJsonString();
        string geometryShapeExample = schema.Examples.Single(example => example.Name == "insert-new-geometry-shape-path").Patch.ToJsonString();
        IReadOnlyList<DeclarativeExampleSummary> geometryExampleSummaries = generator.ListExamples(typeFilter: "GeometryShape");
        CapabilitySchema audioSchema = generator.Generate(categoryFilter: "AudioEffect");
        CapabilitySchema brushSchema = generator.Generate(categoryFilter: "Brush");
        CapabilitySchema textBlockSchema = generator.Generate(typeFilter: nameof(TextBlock));
        CapabilitySchema freshTextBlockSchema = generator.Generate(typeFilter: nameof(TextBlock));
        CapabilitySchema compactVisualEffectSchema = generator.Generate(categoryFilter: "visualEffect", includeProperties: false, includeExamples: false);
        IReadOnlyList<DeclarativeExample> fillExamples = generator.GenerateExamples(categoryFilter: "fill");
        IReadOnlyList<DeclarativeExample> motionExamples = generator.GenerateExamples(categoryFilter: "motion");
        IReadOnlyList<DeclarativeExampleSummary> exampleSummaries = generator.ListExamples(typeFilter: nameof(TextBlock));
        IReadOnlyList<DeclarativeExample> namedExamples = generator.GenerateExamples(nameFilter: "create-empty-scene-orbital-radar");
        textBlockSchema.Examples.Single(example => example.Name == "create-empty-scene-motion-graphics").Patch["Duration"] = "00:00:01";

        Assert.Multiple(() =>
        {
            Assert.That(schema.SchemaVersion, Is.EqualTo(Beutl.AgentToolkit.Common.SchemaVersion.Current));
            Assert.That(textBlock.BaseFields.Any(field => field.Name == nameof(CoreObject.Id)), Is.True);
            Assert.That(size.ValueType, Is.EqualTo(typeof(float).FullName));
            Assert.That(size.Range, Is.Not.Null);
            Assert.That(size.Default, Is.EqualTo(12f));
            Assert.That(size.Animatable, Is.True);
            Assert.That(blendMode.ValueType, Is.EqualTo(typeof(BlendMode).FullName));
            Assert.That(blendMode.EnumValues, Does.Contain(nameof(BlendMode.SrcOver)));
            Assert.That(blendMode.EnumValues, Does.Contain(nameof(BlendMode.Plus)));
            Assert.That(fontWeight.ValueType, Is.EqualTo(typeof(FontWeight).FullName));
            Assert.That(fontWeight.EnumValues, Does.Contain(nameof(FontWeight.Regular)));
            Assert.That(fontWeight.EnumValues, Does.Contain(nameof(FontWeight.Bold)));
            Assert.That(linearGradient.Category, Is.EqualTo(KnownLibraryItemFormats.Brush));
            Assert.That(gradientStops.ElementType, Is.EqualTo(typeof(GradientStop).FullName));
            Assert.That(effectChildren.ElementType, Is.EqualTo(typeof(FilterEffect).FullName));
            Assert.That(audioEffectGroup.Category, Is.EqualTo(KnownLibraryItemFormats.AudioEffect));
            Assert.That(audioEffectChildren.ElementType, Is.EqualTo(typeof(AudioEffect).FullName));
            Assert.That(transformGroup.Category, Is.EqualTo(KnownLibraryItemFormats.Transform));
            Assert.That(transformChildren.ElementType, Is.EqualTo(typeof(Transform).FullName));
            Assert.That(rectGeometry.Category, Is.EqualTo(KnownLibraryItemFormats.Geometry));
            Assert.That(pen.Category, Is.EqualTo(KnownLibraryItemFormats.Pen));
            Assert.That(gradientStopColor.Converter, Does.Contain("ColorJsonConverter"));
            Assert.That(gradientStopColor.UsageHint, Does.Contain("#ffffb34d"));
            Assert.That(gradientStopColor.UsageHint, Does.Contain("Amber"));
            Assert.That(penBrush.UsageHint, Does.Contain("$type"));
            Assert.That(penBrush.UsageHint, Does.Contain("get_schema"));
            Assert.That(linearEasing.Category, Is.EqualTo(KnownLibraryItemFormats.Easing));
            Assert.That(nodeGraphDrawable.Category, Is.EqualTo(KnownLibraryItemFormats.Drawable));
            Assert.That(nodeGraphFilterEffect.Category, Is.EqualTo(KnownLibraryItemFormats.FilterEffect));
            Assert.That(graphModel.Category, Is.EqualTo(KnownLibraryItemFormats.EngineObject));
            Assert.That(fake.Category, Is.EqualTo("Fake.Extension.Drawable"));
            Assert.That(fake.Properties.Single(property => property.Name == nameof(FakeExtensionDrawable.Amount)).Range, Is.Not.Null);
            Assert.That(schema.Examples.Single(example => example.Name == "animate-float-property-keyframes").Patch.ToJsonString(), Does.Contain("KeyFrames"));
            Assert.That(schema.Examples.Single(example => example.Name == "animate-float-property-keyframes").Patch.ToJsonString(), Does.Contain("KeyFrameAnimation"));
            Assert.That(newAnimatedTextExample, Does.Contain("TextBlock"));
            Assert.That(newAnimatedTextExample, Does.Contain("KeyFrameAnimation"));
            Assert.That(newAnimatedTextExample, Does.Contain("UseGlobalClock"));
            Assert.That(emptySceneExample, Does.Contain("TextBlock"));
            Assert.That(emptySceneExample, Does.Contain("Beutl motion"));
            Assert.That(emptySceneExample, Does.Contain("LinearGradientBrush"));
            Assert.That(emptySceneExample, Does.Contain("FilterEffectGroup"));
            Assert.That(brushEffectExample, Does.Contain("LinearGradientBrush"));
            Assert.That(brushEffectExample, Does.Contain("GradientStops"));
            Assert.That(brushEffectExample, Does.Contain("FilterEffect"));
            Assert.That(brushEffectExample, Does.Contain("Blur"));
            Assert.That(geometryShapeExample, Does.Contain("GeometryShape"));
            Assert.That(geometryShapeExample, Does.Contain("PathGeometry"));
            Assert.That(geometryShapeExample, Does.Contain("PathFigure"));
            Assert.That(geometryShapeExample, Does.Contain("LineSegment"));
            Assert.That(geometryShapeExample, Does.Contain("Figures"));
            Assert.That(geometryShapeExample, Does.Contain("Segments"));
            Assert.That(geometryExampleSummaries.Select(example => example.Name), Does.Contain("insert-new-geometry-shape-path"));
            Assert.That(audioSchema.Types.Any(type => type.Type == typeof(DelayEffect).FullName), Is.True);
            Assert.That(audioSchema.Examples, Is.Empty);
            Assert.That(brushSchema.Examples.Select(example => example.Name), Does.Contain("create-empty-scene-motion-graphics"));
            Assert.That(brushSchema.Examples.Select(example => example.Name), Does.Contain("create-empty-scene-orbital-radar"));
            Assert.That(brushSchema.Examples.Select(example => example.Name), Does.Contain("create-empty-scene-split-screen-typography"));
            Assert.That(brushSchema.Examples.Select(example => example.Name), Does.Contain("apply-gradient-fill-and-effect-chain"));
            Assert.That(textBlockSchema.Examples.Select(example => example.Name), Does.Contain("create-empty-scene-motion-graphics"));
            Assert.That(textBlockSchema.Examples.Select(example => example.Name), Does.Contain("create-empty-scene-orbital-radar"));
            Assert.That(textBlockSchema.Examples.Select(example => example.Name), Does.Contain("create-empty-scene-split-screen-typography"));
            Assert.That(textBlockSchema.Examples.Select(example => example.Name), Does.Contain("insert-new-animated-text-keyframes"));
            Assert.That(freshTextBlockSchema.Examples.Single(example => example.Name == "create-empty-scene-motion-graphics").Patch["Duration"]!.GetValue<string>(), Is.EqualTo("00:00:08"));
            Assert.That(compactVisualEffectSchema.Types.Any(type => type.Type == typeof(FilterEffectGroup).FullName), Is.True);
            Assert.That(compactVisualEffectSchema.Types.All(type => type.Properties.Count == 0), Is.True);
            Assert.That(compactVisualEffectSchema.Examples, Is.Empty);
            Assert.That(fillExamples.Select(example => example.Name), Does.Contain("create-empty-scene-orbital-radar"));
            Assert.That(fillExamples.Select(example => example.Name), Does.Contain("create-empty-scene-split-screen-typography"));
            Assert.That(fillExamples.Select(example => example.Name), Does.Contain("apply-gradient-fill-and-effect-chain"));
            Assert.That(motionExamples.Select(example => example.Name), Does.Contain("create-empty-scene-liquid-gradient-system"));
            Assert.That(motionExamples.Select(example => example.Name), Does.Contain("create-empty-scene-data-bar-dashboard"));
            Assert.That(motionExamples.Select(example => example.Name), Does.Contain("create-empty-scene-glitch-cutout-collage"));
            Assert.That(exampleSummaries.Count(example => example.Tags.Contains("empty-scene")), Is.GreaterThanOrEqualTo(3));
            Assert.That(exampleSummaries.Single(example => example.Name == "create-empty-scene-orbital-radar").Tags, Does.Contain("orbital"));
            Assert.That(namedExamples, Has.Count.EqualTo(1));
            Assert.That(namedExamples.Single().Patch.ToJsonString(), Does.Contain("Orbit map"));
        });
    }

    [Test]
    public void Composition_templates_expose_remotion_style_contract()
    {
        var catalog = new CompositionTemplateCatalog();
        CompositionTemplateList firstList = catalog.List(seed: "alpha");
        CompositionTemplateList sameList = catalog.List(seed: "alpha");
        CompositionTemplateDetail detail = catalog.Get("orbital-radar-map");
        var inputProps = new JsonObject
        {
            ["title"] = "Custom orbit",
            ["durationSeconds"] = 6,
            ["fps"] = 24,
            ["density"] = 1.25
        };

        CompositionRender firstRender = catalog.Render("orbital-radar-map", inputProps: inputProps, seed: "orbit-seed");
        CompositionRender sameRender = catalog.Render("orbital-radar-map", inputProps: inputProps, seed: "orbit-seed");
        CompositionRender differentRender = catalog.Render("orbital-radar-map", inputProps: inputProps, seed: "orbit-seed-2");
        CompositionRender noiseRender = catalog.Render("kinetic-ribbon-title", seed: "noise-seed");
        CompositionRender liquidRender = catalog.Render("liquid-gradient-system", seed: "liquid-seed");
        CompositionRender dataRender = catalog.Render("data-bar-dashboard", seed: "data-seed");
        CompositionRender glitchRender = catalog.Render("glitch-cutout-collage", seed: "glitch-seed");

        Assert.Multiple(() =>
        {
            Assert.That(firstList.Seed, Is.EqualTo("alpha"));
            Assert.That(firstList.Compositions.Select(composition => composition.Name), Is.EqualTo(sameList.Compositions.Select(composition => composition.Name)));
            Assert.That(firstList.Compositions, Has.Count.GreaterThanOrEqualTo(6));
            Assert.That(firstList.Compositions.SelectMany(composition => composition.Tags), Does.Contain("liquid"));
            Assert.That(firstList.Compositions.SelectMany(composition => composition.Tags), Does.Contain("dashboard"));
            Assert.That(firstList.Compositions.SelectMany(composition => composition.Tags), Does.Contain("glitch"));
            Assert.That(detail.DefaultProps["title"]!.GetValue<string>(), Is.EqualTo("Orbit map"));
            Assert.That(detail.Props.Select(prop => prop.Name), Does.Contain("durationSeconds"));
            Assert.That(detail.Sequences.Any(sequence => sequence.Name == "body"), Is.True);
            Assert.That(detail.Transitions.Any(transition => transition.Type.Contains("opacity", StringComparison.Ordinal)), Is.True);
            Assert.That(firstRender.Metadata.DurationInFrames, Is.EqualTo(144));
            Assert.That(firstRender.ResolvedProps["title"]!.GetValue<string>(), Is.EqualTo("Custom orbit"));
            Assert.That(firstRender.Sequences, Has.Count.GreaterThanOrEqualTo(4));
            Assert.That(firstRender.Transitions, Has.Count.GreaterThanOrEqualTo(2));
            Assert.That(JsonNode.DeepEquals(firstRender.Patch, sameRender.Patch), Is.True);
            Assert.That(JsonNode.DeepEquals(firstRender.Patch, differentRender.Patch), Is.False);
            Assert.That(firstRender.Patch.ToJsonString(), Does.Contain("Custom orbit"));
            Assert.That(firstRender.Patch.ToJsonString(), Does.Contain("KeyFrames"));
            Assert.That(firstRender.Patch.ToJsonString(), Does.Contain("FilterEffectGroup"));
            Assert.That(noiseRender.Patch.ToJsonString(), Does.Contain("Deterministic noise dot"));
            Assert.That(liquidRender.Patch.ToJsonString(), Does.Contain("Seeded liquid blob"));
            Assert.That(dataRender.Patch.ToJsonString(), Does.Contain("Seeded metric bar"));
            Assert.That(glitchRender.Patch.ToJsonString(), Does.Contain("Seeded glitch slice"));
            Assert.That(TransformGroupsUseCanonicalOrder(firstRender.Patch), Is.True);
            Assert.That(TransformGroupsUseCanonicalOrder(catalog.Render("kinetic-ribbon-title", seed: "ribbon-seed").Patch), Is.True);
            Assert.That(TransformGroupsUseCanonicalOrder(liquidRender.Patch), Is.True);
            Assert.That(TransformGroupsUseCanonicalOrder(dataRender.Patch), Is.True);
            Assert.That(TransformGroupsUseCanonicalOrder(glitchRender.Patch), Is.True);
            Assert.That(TransformGroupsUseCanonicalOrder(new SchemaGenerator()
                .GenerateExamples(nameFilter: "create-empty-scene-orbital-radar")
                .Single()
                .Patch), Is.True);
        });
    }

    [Test]
    public void Animatable_property_usage_hint_shows_concrete_angle_bracket_keyframe_discriminator()
    {
        var generator = new SchemaGenerator();
        CapabilitySchema schema = generator.Generate(typeFilter: nameof(TextBlock));
        TypeDescriptor textBlock = schema.Types.Single(type => type.Type == typeof(TextBlock).FullName);
        PropertyDescriptor size = textBlock.Properties.Single(property => property.Name == nameof(TextBlock.Size));

        Assert.Multiple(() =>
        {
            Assert.That(size.Animatable, Is.True);
            Assert.That(size.UsageHint, Does.Contain("KeyFrameAnimation<"));
            Assert.That(size.UsageHint, Does.Contain("KeyFrame<"));
            Assert.That(size.UsageHint, Does.Not.Contain("KeyFrameAnimation`1"));
        });
    }

    [Test]
    public void Grain_and_organic_effect_recipes_use_distinct_shader_scripts()
    {
        var generator = new SchemaGenerator();
        string grain = generator.GetEffectRecipe("fine-film-grain-field").Patch.ToJsonString();
        string organic = generator.GetEffectRecipe("organic-shader-field").Patch.ToJsonString();

        Assert.Multiple(() =>
        {
            Assert.That(grain, Is.Not.EqualTo(organic));
            Assert.That(grain, Does.Contain("43758.5453"));
            Assert.That(grain, Does.Not.Contain("sin(uv.x * 14.0"));
            Assert.That(organic, Does.Contain("sin(uv.x * 14.0"));
        });
    }

    [Test]
    public void Organic_shader_recipe_modulates_source_color_and_compiles()
    {
        var generator = new SchemaGenerator();
        EffectRecipe organicRecipe = generator.GetEffectRecipe("organic-shader-field");
        string script = FindRequiredStringProperty(organicRecipe.Patch, nameof(SKSLScriptEffect.Script));

        Assert.Multiple(() =>
        {
            Assert.That(script, Does.Contain("sin(uv.x * 14.0"));
            Assert.That(script, Does.Contain("src.eval(fragCoord)"));
            Assert.That(script, Does.Contain("base.rgb"));
            Assert.That(script, Does.Contain("mix(base.rgb, field, 0.2)"));
            Assert.That(script, Does.Not.Contain("0.95 * wave"));
            Assert.That(script, Does.Not.Contain("0.35"));
        });

        using SKSLShader shader = CompileSkslOrSkip(script);

        Assert.That(shader.Effect.Children.Contains("src"), Is.True);
    }

    [Test]
    public void Single_sksl_effect_recipe_uses_neutral_pass_through_scaffold()
    {
        var generator = new SchemaGenerator();
        EffectRecipe scaffoldRecipe = generator.GetEffectRecipe("effect-sksl-script-effect");
        string scaffold = scaffoldRecipe.Patch.ToJsonString();
        string organic = generator.GetEffectRecipe("organic-shader-field").Patch.ToJsonString();
        string script = FindRequiredStringProperty(scaffoldRecipe.Patch, nameof(SKSLScriptEffect.Script));

        using SKSLShader shader = CompileSkslOrSkip(script);

        Assert.Multiple(() =>
        {
            Assert.That(scaffold, Is.Not.EqualTo(organic));
            Assert.That(scaffold, Does.Not.Contain("sin(uv.x * 14.0"));
            Assert.That(script, Does.Contain("src.eval(fragCoord)"));
            Assert.That(script, Does.Not.Contain("sin(uv.x * 14.0"));
            Assert.That(shader.Effect.Children.Contains("src"), Is.True);
            Assert.That(scaffoldRecipe.Description, Does.Contain("blank pass-through scaffold"));
            Assert.That(scaffoldRecipe.Description, Does.Contain("organic-shader-field"));
            Assert.That(scaffoldRecipe.Description, Does.Contain("fine-film-grain-field"));
            Assert.That(scaffoldRecipe.Notes, Has.Some.Contains("blank pass-through SKSL scaffold"));
            Assert.That(scaffoldRecipe.Notes, Has.Some.Contains("compile error makes the effect a no-op"));
            Assert.That(scaffoldRecipe.IntentTags, Does.Not.Contain("organic"));
            Assert.That(generator.GetEffectRecipe(intent: "organic shader").Name, Is.EqualTo("organic-shader-field"));
        });
    }

    [Test]
    public void Effect_recipe_polish_defaults_and_notes_are_agent_safe()
    {
        var generator = new SchemaGenerator();
        EffectRecipe dropShadowRecipe = generator.GetEffectRecipe("effect-drop-shadow");
        JsonObject dropShadowEffect = FindRequiredEffectObject(dropShadowRecipe.Patch, typeof(DropShadow));
        (float shadowX, float shadowY) = FindRequiredPointProperty(dropShadowEffect, nameof(DropShadow.Position));
        EffectRecipe invertRecipe = generator.GetEffectRecipe("effect-invert");
        float invertAmount = FindRequiredFloatProperty(invertRecipe.Patch, nameof(Invert.Amount));
        EffectRecipe outlineRecipe = generator.GetEffectRecipe("graphic-outline");
        EffectRecipe filmGrainRecipe = generator.GetEffectRecipe("fine-film-grain-field");
        EffectRecipe organicRecipe = generator.GetEffectRecipe("organic-shader-field");
        EffectRecipe scaffoldRecipe = generator.GetEffectRecipe("effect-sksl-script-effect");

        Assert.Multiple(() =>
        {
            Assert.That(Math.Abs(shadowX) + Math.Abs(shadowY), Is.GreaterThan(0f));
            Assert.That(invertAmount, Is.EqualTo(80f));
            Assert.That(outlineRecipe.Notes.Any(note =>
                note.Contains("stylized accent", StringComparison.OrdinalIgnoreCase)
                && note.Contains("override", StringComparison.OrdinalIgnoreCase)), Is.True);
            Assert.That(filmGrainRecipe.Notes.Any(ContainsShaderSourceGuidance), Is.False);
            Assert.That(organicRecipe.Notes.Any(ContainsShaderSourceGuidance), Is.False);
            Assert.That(scaffoldRecipe.Notes.Any(ContainsShaderSourceGuidance), Is.True);
        });
    }

    [Test]
    public void Additive_bloom_recipe_sets_plus_blend_opacity_and_guides_duplicate_object()
    {
        var generator = new SchemaGenerator();
        EffectRecipe bloom = generator.GetEffectRecipe("additive-bloom");
        string bloomJson = bloom.Patch.ToJsonString();
        string glowJson = generator.GetEffectRecipe("glow-depth").Patch.ToJsonString();

        Assert.Multiple(() =>
        {
            Assert.That(bloomJson, Is.Not.EqualTo(glowJson));
            Assert.That(bloomJson, Does.Contain("\"BlendMode\":12"));
            Assert.That(bloomJson, Does.Contain("\"Opacity\":60"));
            Assert.That(bloomJson, Does.Contain("Blur"));
            Assert.That(bloom.Notes.Any(note => note.Contains("duplicate_object", StringComparison.Ordinal)), Is.True);
        });
    }

    [Test]
    public void Glow_depth_recipe_uses_drop_shadow_glow_without_source_blur()
    {
        var generator = new SchemaGenerator();
        EffectRecipe glow = generator.GetEffectRecipe("glow-depth");
        string glowJson = glow.Patch.ToJsonString();
        string blurDiscriminator = IdentityHelper.WriteDiscriminator(typeof(Blur));
        string dropShadowDiscriminator = IdentityHelper.WriteDiscriminator(typeof(DropShadow));

        Assert.Multiple(() =>
        {
            Assert.That(ContainsDiscriminator(glow.Patch, blurDiscriminator), Is.False);
            Assert.That(glowJson, Does.Not.Contain("#66496a74"));
            Assert.That(ContainsDiscriminator(glow.Patch, dropShadowDiscriminator), Is.True);
            Assert.That(glow.Description, Does.Contain("keeps the source sharp"));
            Assert.That(glow.Description, Does.Contain("palette-neutral default glow color"));
            Assert.That(glow.Description, Does.Contain("additive-bloom"));
        });
    }

    [Test]
    public void New_curated_effect_recipes_cover_screen_multiply_and_lite_chromatic_gaps()
    {
        var generator = new SchemaGenerator();
        string bloomJson = generator.GetEffectRecipe("additive-bloom").Patch.ToJsonString();
        string screenJson = generator.GetEffectRecipe("screen-light-leak").Patch.ToJsonString();
        string multiplyJson = generator.GetEffectRecipe("multiply-contrast-glaze").Patch.ToJsonString();
        string chromaticJson = generator.GetEffectRecipe("chromatic-aberration-lite").Patch.ToJsonString();

        Assert.Multiple(() =>
        {
            Assert.That(screenJson, Does.Contain("\"BlendMode\":14"));
            Assert.That(screenJson, Does.Contain("\"Opacity\":50"));
            Assert.That(multiplyJson, Does.Contain("\"BlendMode\":24"));
            Assert.That(multiplyJson, Does.Contain("\"Opacity\":50"));
            Assert.That(chromaticJson, Does.Contain("ColorShift"));
            Assert.That(chromaticJson, Does.Not.Contain("BlendMode"));
            Assert.That(screenJson, Is.Not.EqualTo(bloomJson));
            Assert.That(multiplyJson, Is.Not.EqualTo(bloomJson));
            Assert.That(chromaticJson, Is.Not.EqualTo(bloomJson));
        });
    }

    [Test]
    public void Transition_recipe_catalog_lists_semantic_transition_templates()
    {
        var generator = new SchemaGenerator();
        IReadOnlyList<EffectRecipeSummary> recipes = generator.ListEffectRecipes("transition");
        string[] names =
        [
            "transition-overlap-dissolve-transform-continuation",
            "transition-directional-sweep-wipe",
            "transition-mask-reveal",
            "transition-dip-to-color",
            "transition-match-move-cut"
        ];

        Assert.Multiple(() =>
        {
            foreach (string name in names)
            {
                Assert.That(recipes.Select(recipe => recipe.Name), Does.Contain(name));
                EffectRecipe recipe = generator.GetEffectRecipe(name);
                Assert.That(recipe.Semantic, Is.Not.Null.And.Not.Empty);
                Assert.That(recipe.IntentTags, Does.Contain("transition"));
                Assert.That(recipe.Patch["Elements"], Is.Not.Null);
            }

            Assert.That(generator.GetEffectRecipe("transition-overlap-dissolve-transform-continuation").Semantic, Is.EqualTo("time passage / soft topic shift"));
            Assert.That(generator.GetEffectRecipe("transition-directional-sweep-wipe").Semantic, Is.EqualTo("location or topic change"));
            Assert.That(generator.GetEffectRecipe("transition-mask-reveal").Semantic, Is.EqualTo("introduction / unveiling"));
            Assert.That(generator.GetEffectRecipe("transition-dip-to-color").Semantic, Is.EqualTo("chapter break"));
            Assert.That(generator.GetEffectRecipe("transition-match-move-cut").Semantic, Is.EqualTo("conceptual rhyme"));
        });
    }

    [Test]
    public void Restrained_warm_grade_uses_positive_temperature_color_grading()
    {
        var generator = new SchemaGenerator();
        EffectRecipe warm = generator.GetEffectRecipe("restrained-warm-grade");
        string warmJson = warm.Patch.ToJsonString();
        string editorialJson = generator.GetEffectRecipe("editorial-color-grade").Patch.ToJsonString();
        float temperature = FindRequiredFloatProperty(warm.Patch, nameof(ColorGrading.Temperature));

        Assert.Multiple(() =>
        {
            Assert.That(warmJson, Does.Contain(nameof(ColorGrading)));
            Assert.That(warmJson, Does.Contain(nameof(ColorGrading.Temperature)));
            Assert.That(warmJson, Does.Not.Contain(nameof(HueRotate)));
            Assert.That(temperature, Is.GreaterThan(0));
            Assert.That(warmJson, Is.Not.EqualTo(editorialJson));
        });
    }

    [Test]
    public void Starter_examples_do_not_ship_long_all_caps_display_text()
    {
        var generator = new SchemaGenerator();
        string[] starterJson = generator
            .GenerateExamples()
            .Where(example => example.Name.StartsWith("create-empty-scene-", StringComparison.Ordinal))
            .Select(example => example.Patch.ToJsonString())
            .ToArray();
        var catalog = new CompositionTemplateCatalog();
        string[] defaultTitles = catalog
            .List(seed: "quality-check")
            .Compositions
            .Select(composition => catalog.Get(composition.Name).DefaultProps["title"]!.GetValue<string>())
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(starterJson.Any(ContainsLongAllCapsText), Is.False);
            Assert.That(defaultTitles.Any(LooksLikeLongAllCaps), Is.False);
        });
    }

    [Test]
    public void Composition_templates_vary_seedless_session_order_and_orbital_layouts()
    {
        var sessionCatalog = new CompositionTemplateCatalog(defaultSeed: "session-a");
        CompositionTemplateList firstList = sessionCatalog.List();
        CompositionTemplateList sameList = sessionCatalog.List();
        string[] firstOrder = firstList.Compositions.Select(composition => composition.Name).ToArray();
        string[] sameOrder = sameList.Compositions.Select(composition => composition.Name).ToArray();
        CompositionTemplateList avoidedList = sessionCatalog.List(deprioritizedNames: [firstOrder[0]]);

        string[] firstChoices = Enumerable.Range(0, 24)
            .Select(index => new CompositionTemplateCatalog(defaultSeed: $"session-{index}")
                .List()
                .Compositions
                .First()
                .Name)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        float[] titlePositions = Enumerable.Range(0, 36)
            .Select(index => FindObjectTranslateX(
                new CompositionTemplateCatalog().Render("orbital-radar-map", seed: $"orbit-layout-{index}").Patch,
                "Technical title"))
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(firstList.Seed, Is.EqualTo("session-a"));
            Assert.That(sameList.Seed, Is.EqualTo("session-a"));
            Assert.That(firstOrder, Is.EqualTo(sameOrder));
            Assert.That(avoidedList.Compositions.Last().Name, Is.EqualTo(firstOrder[0]));
            Assert.That(firstChoices, Has.Length.GreaterThan(1));
            Assert.That(firstChoices, Has.Length.GreaterThanOrEqualTo(4));
            Assert.That(titlePositions.Min(), Is.LessThan(-200));
            Assert.That(titlePositions.Min(), Is.GreaterThan(-650));
            Assert.That(titlePositions.Max(), Is.GreaterThan(200));
        });
    }

    [Test]
    public void Effect_catalog_exposes_recipe_for_every_registered_filter_effect()
    {
        TypeRegistration.EnsureRegistered();
        var generator = new SchemaGenerator();
        string[] registeredEffectNames = LibraryService.Current
            .GetTypesFromFormat(KnownLibraryItemFormats.FilterEffect)
            .Select(type => type.Name)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        IReadOnlyList<EffectSummary> effects = generator.ListEffects();
        IReadOnlyList<EffectRecipeSummary> recipes = generator.ListEffectRecipes();
        EffectRecipe glitchRecipe = generator.GetEffectRecipe("digital-glitch");

        Assert.Multiple(() =>
        {
            Assert.That(effects.Select(effect => effect.Name), Is.SupersetOf(registeredEffectNames));
            Assert.That(recipes.SelectMany(recipe => recipe.EffectNames).Distinct(StringComparer.Ordinal), Is.SupersetOf(registeredEffectNames));
            Assert.That(recipes.Count(recipe => recipe.Name.StartsWith("effect-", StringComparison.Ordinal)), Is.GreaterThanOrEqualTo(registeredEffectNames.Length));
            Assert.That(recipes.Single(recipe => recipe.Name == "effect-pixel-sort-effect").Notes, Has.Some.Contains("GPU"));
            Assert.That(glitchRecipe.Patch.ToJsonString(), Does.Contain("ColorShift"));
            Assert.That(glitchRecipe.Patch.ToJsonString(), Does.Contain("MosaicEffect"));
            Assert.That(glitchRecipe.Patch.ToJsonString(), Does.Contain("ShakeEffect"));
        });
    }

    private static bool TransformGroupsUseCanonicalOrder(JsonNode? node)
    {
        bool valid = true;
        Visit(node, current =>
        {
            if (current is not JsonObject obj
                || obj["Children"] is not JsonArray children)
            {
                return;
            }

            string[] types = children
                .OfType<JsonObject>()
                .Select(child => child["$type"]?.GetValue<string>() ?? string.Empty)
                .ToArray();
            int translateIndex = Array.FindIndex(types, type => type.Contains("TranslateTransform", StringComparison.Ordinal));
            int rotationIndex = Array.FindIndex(types, type => type.Contains("RotationTransform", StringComparison.Ordinal));
            if (translateIndex >= 0 && rotationIndex >= 0 && translateIndex > rotationIndex)
            {
                valid = false;
            }
        });
        return valid;
    }

    private static bool ContainsDiscriminator(JsonObject node, string discriminator)
    {
        bool result = false;
        Visit(node, current =>
        {
            if (result
                || current is not JsonObject obj
                || obj["$type"] is not JsonValue typeNode
                || !typeNode.TryGetValue(out string? currentDiscriminator))
            {
                return;
            }

            result = string.Equals(currentDiscriminator, discriminator, StringComparison.Ordinal);
        });

        return result;
    }

    private static JsonObject FindRequiredEffectObject(JsonObject node, Type effectType)
    {
        string discriminator = IdentityHelper.WriteDiscriminator(effectType);
        JsonObject? result = null;
        Visit(node, current =>
        {
            if (result is not null
                || current is not JsonObject obj
                || obj["$type"] is not JsonValue typeNode
                || !typeNode.TryGetValue(out string? currentDiscriminator)
                || !string.Equals(currentDiscriminator, discriminator, StringComparison.Ordinal))
            {
                return;
            }

            result = obj;
        });

        Assert.That(result, Is.Not.Null, $"Effect '{effectType.Name}' was not found.");
        return result!;
    }

    private static (float X, float Y) FindRequiredPointProperty(JsonObject node, string propertyName)
    {
        JsonNode? value = node[propertyName];
        Assert.That(value, Is.Not.Null, $"Point property '{propertyName}' was not found.");
        JsonNode? pointNode = value is JsonObject ? value : JsonNode.Parse(value!.ToJsonString());
        switch (pointNode)
        {
            case JsonObject point:
                return (
                    FindRequiredFloatProperty(point, nameof(Point.X)),
                    FindRequiredFloatProperty(point, nameof(Point.Y)));
            case JsonArray pointArray when pointArray.Count >= 2:
                return (
                    pointArray[0]!.GetValue<float>(),
                    pointArray[1]!.GetValue<float>());
            case JsonValue pointValue when pointValue.TryGetValue(out string? text):
                return ParsePointText(text, propertyName);
        }

        Assert.Fail($"Point property '{propertyName}' had unsupported JSON '{value!.ToJsonString()}'.");
        return default;
    }

    private static (float X, float Y) ParsePointText(string text, string propertyName)
    {
        string[] parts = text.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length >= 2
            && float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
            && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y))
        {
            return (x, y);
        }

        Assert.Fail($"Point property '{propertyName}' had unsupported value '{text}'.");
        return default;
    }

    private static bool ContainsShaderSourceGuidance(string note)
    {
        return note.Contains("Requires shader source", StringComparison.OrdinalIgnoreCase)
               || note.Contains("validate_shader", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsLongAllCapsText(string json)
    {
        return json
            .Split(['"', '\\', '/', ':', ',', '{', '}', '[', ']'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(LooksLikeLongAllCaps);
    }

    private static bool LooksLikeLongAllCaps(string text)
    {
        int letters = text.Count(char.IsLetter);
        return letters >= 7
               && text.Count(char.IsUpper) / (double)letters >= 0.85
               && text.Count(char.IsLower) == 0;
    }

    private static float FindObjectTranslateX(JsonObject patch, string objectName)
    {
        foreach (JsonObject element in patch["Elements"]!.AsArray().OfType<JsonObject>())
        {
            foreach (JsonObject obj in element["Objects"]!.AsArray().OfType<JsonObject>())
            {
                if (!string.Equals(obj["Name"]?.GetValue<string>(), objectName, StringComparison.Ordinal))
                {
                    continue;
                }

                JsonObject translate = obj["Transform"]!["Children"]!
                    .AsArray()
                    .OfType<JsonObject>()
                    .First(child => child["$type"]?.GetValue<string>().Contains("TranslateTransform", StringComparison.Ordinal) == true);
                return translate["X"]!.GetValue<float>();
            }
        }

        Assert.Fail($"Object '{objectName}' was not found.");
        return 0;
    }

    private static string FindRequiredStringProperty(JsonObject node, string propertyName)
    {
        string? result = null;
        Visit(node, current =>
        {
            if (result is not null
                || current is not JsonObject obj
                || obj[propertyName] is not JsonValue value
                || !value.TryGetValue(out string? text))
            {
                return;
            }

            result = text;
        });

        Assert.That(result, Is.Not.Null, $"Property '{propertyName}' was not found.");
        return result!;
    }

    private static float FindRequiredFloatProperty(JsonObject node, string propertyName)
    {
        float? result = null;
        Visit(node, current =>
        {
            if (result is not null
                || current is not JsonObject obj
                || obj[propertyName] is not JsonValue value
                || !value.TryGetValue(out float number))
            {
                return;
            }

            result = number;
        });

        Assert.That(result, Is.Not.Null, $"Property '{propertyName}' was not found.");
        return result!.Value;
    }

    private static SKSLShader CompileSkslOrSkip(string script)
    {
        if (SKSLShader.TryCreate(script, out SKSLShader? shader, out string? errorText))
        {
            return shader!;
        }

        if (IsNativeSkiaUnavailable(errorText))
        {
            Assert.Ignore($"SKSL compilation is unavailable in this environment: {errorText}");
        }

        Assert.Fail($"SKSL script should compile: {errorText}");
        return null!;
    }

    private static bool IsNativeSkiaUnavailable(string? errorText)
    {
        return !string.IsNullOrWhiteSpace(errorText)
               && (errorText.Contains("Unable to load shared library", StringComparison.OrdinalIgnoreCase)
                   || errorText.Contains("DllNotFoundException", StringComparison.OrdinalIgnoreCase)
                   || errorText.Contains("libSkiaSharp", StringComparison.OrdinalIgnoreCase)
                   || errorText.Contains("dlopen", StringComparison.OrdinalIgnoreCase));
    }

    private static void Visit(JsonNode? node, Action<JsonNode> visitor)
    {
        if (node is null)
        {
            return;
        }

        visitor(node);
        switch (node)
        {
            case JsonObject obj:
                foreach (JsonNode? child in obj.Select(pair => pair.Value))
                {
                    Visit(child, visitor);
                }

                break;
            case JsonArray array:
                foreach (JsonNode? child in array)
                {
                    Visit(child, visitor);
                }

                break;
        }
    }

    private sealed class FakeExtensionDrawable : EngineObject
    {
        public FakeExtensionDrawable()
        {
            ScanProperties<FakeExtensionDrawable>();
        }

        [System.ComponentModel.DataAnnotations.Range(0, 1)]
        public IProperty<float> Amount { get; } = Property.CreateAnimatable(0.5f);
    }
}
