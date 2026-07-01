using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
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
