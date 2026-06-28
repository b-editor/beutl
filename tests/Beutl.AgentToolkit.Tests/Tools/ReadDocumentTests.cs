using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Documents;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.AgentToolkit.Schema;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Tests.Helpers;
using Beutl.AgentToolkit.Tools;
using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Editor;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.ProjectSystem;

namespace Beutl.AgentToolkit.Tests.Tools;

public sealed class ReadDocumentTests
{
    [Test]
    public void Get_started_returns_low_context_entrypoints()
    {
        var tools = new QueryTools(new AgentSessionManager());

        ToolResult<GettingStartedResponse> result = tools.GetStarted();

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
            Assert.That(result.Value!.RecommendedCalls, Does.Contain("In live mode, call attach_active_editor before scene tools."));
            Assert.That(result.Value.RecommendedCalls, Has.Some.Contains("list_compositions"));
            Assert.That(result.Value.RecommendedCalls, Has.Some.Contains("plan_composition/apply_composition"));
            Assert.That(result.Value.RecommendedCalls, Has.Some.Contains("planId"));
            Assert.That(result.Value.RecommendedCalls, Has.Some.Contains("try the next list_compositions"));
            Assert.That(result.Value.RecommendedCalls, Has.Some.Contains("list_effects"));
            Assert.That(result.Value.RecommendedCalls, Has.Some.Contains("list_effect_recipes"));
            Assert.That(result.Value.RecommendedCalls, Has.Some.Contains("list_examples"));
            Assert.That(result.Value.CategoryAliases["visualEffect"], Is.EqualTo("FilterEffect"));
            Assert.That(result.Value.RawHttpNote, Does.Contain("Server-Sent Events"));
            Assert.That(result.Value.RawHttpNote, Does.Contain("content[0].text"));
        });
    }

    [Test]
    public void Examples_can_be_listed_and_fetched_by_name()
    {
        var tools = new QueryTools(new AgentSessionManager());

        ToolResult<ListExamplesResponse> list = tools.ListExamples(type: nameof(TextBlock));
        ToolResult<ListExamplesResponse> motionList = tools.ListExamples(category: "motion");
        string selectedName = list.Value!.Examples
            .Single(example => example.Name == "create-empty-scene-split-screen-typography")
            .Name;
        ToolResult<GetExamplesResponse> selected = tools.GetExamples(name: selectedName);

        Assert.Multiple(() =>
        {
            Assert.That(list.IsSuccess, Is.True, list.Error?.Message);
            Assert.That(list.Value!.Examples.Count(example => example.Tags.Contains("empty-scene")), Is.GreaterThanOrEqualTo(3));
            Assert.That(list.Value.SelectionHint, Does.Contain("shuffled"));
            Assert.That(motionList.IsSuccess, Is.True, motionList.Error?.Message);
            Assert.That(motionList.Value!.Examples.Select(example => example.Name), Does.Contain("create-empty-scene-liquid-gradient-system"));
            Assert.That(motionList.Value.Examples.Select(example => example.Name), Does.Contain("create-empty-scene-data-bar-dashboard"));
            Assert.That(motionList.Value.Examples.Select(example => example.Name), Does.Contain("create-empty-scene-glitch-cutout-collage"));
            Assert.That(selected.IsSuccess, Is.True, selected.Error?.Message);
            Assert.That(selected.Value!.Examples, Has.Count.EqualTo(1));
            Assert.That(selected.Value.Examples.Single().Name, Is.EqualTo(selectedName));
            Assert.That(selected.Value.Examples.Single().Patch.ToJsonString(), Does.Contain("FRAME FLOW"));
        });
    }

    [Test]
    public void Composition_tools_list_detail_and_render_seeded_patch()
    {
        var tools = new QueryTools(new AgentSessionManager());
        var inputProps = new JsonObject
        {
            ["title"] = "PATCH TITLE",
            ["durationSeconds"] = 5,
            ["fps"] = 20
        };

        ToolResult<ListCompositionsResponse> list = tools.ListCompositions(seed: "tool-seed");
        ToolResult<ListCompositionsResponse> motionList = tools.ListCompositions(tag: "motion", seed: "tool-seed");
        ToolResult<GetCompositionResponse> detail = tools.GetComposition("split-screen-type-system");
        ToolResult<RenderCompositionPatchResponse> render = tools.RenderCompositionPatch(
            name: "split-screen-type-system",
            inputProps: inputProps,
            seed: "tool-seed");

        Assert.Multiple(() =>
        {
            Assert.That(list.IsSuccess, Is.True, list.Error?.Message);
            Assert.That(list.Value!.Seed, Is.EqualTo("tool-seed"));
            Assert.That(list.Value.Compositions.Select(composition => composition.Name), Does.Contain("split-screen-type-system"));
            Assert.That(list.Value.SelectionHint, Does.Contain("avoidRecent"));
            Assert.That(list.Value.RecentlyUsedCompositions, Is.Empty);
            Assert.That(motionList.IsSuccess, Is.True, motionList.Error?.Message);
            Assert.That(motionList.Value!.Compositions, Has.Count.GreaterThanOrEqualTo(6));
            Assert.That(detail.IsSuccess, Is.True, detail.Error?.Message);
            Assert.That(detail.Value!.Composition.DefaultProps["title"]!.GetValue<string>(), Is.EqualTo("FRAME FLOW"));
            Assert.That(detail.Value.Composition.Sequences.Any(sequence => sequence.Name == "typography"), Is.True);
            Assert.That(render.IsSuccess, Is.True, render.Error?.Message);
            Assert.That(render.Value!.Composition.Seed, Is.EqualTo("tool-seed"));
            Assert.That(render.Value.Composition.Metadata.DurationInFrames, Is.EqualTo(100));
            Assert.That(render.Value.Composition.ResolvedProps["title"]!.GetValue<string>(), Is.EqualTo("PATCH TITLE"));
            Assert.That(render.Value.Composition.Patch.ToJsonString(), Does.Contain("PATCH TITLE"));
            Assert.That(render.Value.UsageHint, Does.Contain("plan_edit"));
        });
    }

    [Test]
    public void Effect_tools_list_and_fetch_recipes()
    {
        var tools = new QueryTools(new AgentSessionManager());

        ToolResult<ListEffectsResponse> effects = tools.ListEffects(intent: "glitch");
        ToolResult<ListEffectRecipesResponse> recipes = tools.ListEffectRecipes(intent: "glitch");
        ToolResult<ListEffectRecipesResponse> glowMotionRecipes = tools.ListEffectRecipes(intent: "glow motion");
        ToolResult<GetEffectRecipeResponse> recipe = tools.GetEffectRecipe(name: "effect-color-shift");

        Assert.Multiple(() =>
        {
            Assert.That(effects.IsSuccess, Is.True, effects.Error?.Message);
            Assert.That(effects.Value!.Effects.Select(effect => effect.Name), Does.Contain("ColorShift"));
            Assert.That(effects.Value.Effects.Select(effect => effect.Name), Does.Contain("PixelSortEffect"));
            Assert.That(effects.Value.Effects.Single(effect => effect.Name == "PixelSortEffect").RequiresGpu, Is.True);
            Assert.That(recipes.IsSuccess, Is.True, recipes.Error?.Message);
            Assert.That(recipes.Value!.Recipes.Select(item => item.Name), Does.Contain("digital-glitch"));
            Assert.That(recipes.Value.Recipes.Select(item => item.Name), Does.Contain("effect-color-shift"));
            Assert.That(glowMotionRecipes.IsSuccess, Is.True, glowMotionRecipes.Error?.Message);
            Assert.That(glowMotionRecipes.Value!.Recipes.Select(item => item.Name), Does.Contain("glow-depth"));
            Assert.That(glowMotionRecipes.Value.Recipes.Select(item => item.Name), Does.Contain("digital-glitch"));
            Assert.That(recipe.IsSuccess, Is.True, recipe.Error?.Message);
            Assert.That(recipe.Value!.Recipe.EffectNames, Does.Contain("ColorShift"));
            Assert.That(recipe.Value.Recipe.Patch.ToJsonString(), Does.Contain("ColorShift"));
            Assert.That(recipe.Value.UsageHint, Does.Contain("plan_edit"));
        });
    }

    [Test]
    public void Composition_tools_reuse_session_seed_when_seed_is_omitted()
    {
        var scene = new Scene(1920, 1080, "Scene");
        using var session = new AgentToolkitTestSession(scene);
        var manager = new AgentSessionManager();
        manager.UseSource(new AgentToolkitTestSessionSource(session));
        var queryTools = new QueryTools(manager);
        var editTools = new EditTools(manager);

        ToolResult<ListCompositionsResponse> firstList = queryTools.ListCompositions();
        ToolResult<ListCompositionsResponse> sameList = queryTools.ListCompositions();
        string selectedName = firstList.Value!.Compositions.First().Name;
        ToolResult<PlanCompositionResponse> plan = editTools.PlanComposition(name: selectedName);
        ToolResult<ApplyCompositionResponse> apply = editTools.ApplyComposition(planId: plan.Value!.PlanId);
        ToolResult<ListCompositionsResponse> afterApplyList = queryTools.ListCompositions();

        Assert.Multiple(() =>
        {
            Assert.That(firstList.IsSuccess, Is.True, firstList.Error?.Message);
            Assert.That(sameList.IsSuccess, Is.True, sameList.Error?.Message);
            Assert.That(plan.IsSuccess, Is.True, plan.Error?.Message);
            Assert.That(apply.IsSuccess, Is.True, apply.Error?.Message);
            Assert.That(afterApplyList.IsSuccess, Is.True, afterApplyList.Error?.Message);
            Assert.That(firstList.Value!.Seed, Does.StartWith("session:"));
            Assert.That(sameList.Value!.Seed, Is.EqualTo(firstList.Value.Seed));
            Assert.That(sameList.Value.Compositions.Select(composition => composition.Name), Is.EqualTo(firstList.Value.Compositions.Select(composition => composition.Name)));
            Assert.That(plan.Value!.Composition.Seed, Is.EqualTo(firstList.Value.Seed));
            Assert.That(plan.Value.Composition.Name, Is.EqualTo(selectedName));
            Assert.That(plan.Value.DetailedPlan, Is.Null);
            Assert.That(plan.Value.Plan.Valid, Is.True);
            Assert.That(apply.Value!.AppliedPlanId, Is.EqualTo(plan.Value.PlanId));
            Assert.That(afterApplyList.Value!.RecentlyUsedCompositions, Does.Contain(selectedName));
            Assert.That(afterApplyList.Value.Compositions.First().Name, Is.Not.EqualTo(selectedName));
            Assert.That(afterApplyList.Value.Compositions.Last().Name, Is.EqualTo(selectedName));
        });
    }

    [Test]
    public void Composition_recent_survives_volatile_live_session_ids()
    {
        var scene = new Scene(1920, 1080, "Scene");
        using var source = new VolatileLiveSessionSource(scene);
        var manager = new AgentSessionManager();
        manager.UseSource(source);
        var queryTools = new QueryTools(manager);
        var editTools = new EditTools(manager);

        ToolResult<ListCompositionsResponse> firstList = queryTools.ListCompositions();
        string selectedName = firstList.Value!.Compositions.First().Name;
        ToolResult<PlanCompositionResponse> plan = editTools.PlanComposition(name: selectedName);
        ToolResult<ApplyCompositionResponse> apply = editTools.ApplyComposition(planId: plan.Value!.PlanId);
        ToolResult<ListCompositionsResponse> afterApplyList = queryTools.ListCompositions();

        Assert.Multiple(() =>
        {
            Assert.That(firstList.IsSuccess, Is.True, firstList.Error?.Message);
            Assert.That(plan.IsSuccess, Is.True, plan.Error?.Message);
            Assert.That(apply.IsSuccess, Is.True, apply.Error?.Message);
            Assert.That(afterApplyList.IsSuccess, Is.True, afterApplyList.Error?.Message);
            Assert.That(afterApplyList.Value!.RecentlyUsedCompositions, Does.Contain(selectedName));
            Assert.That(afterApplyList.Value.Compositions.Last().Name, Is.EqualTo(selectedName));
        });
    }

    [Test]
    public void Apply_edit_fallback_records_recent_style_and_deprioritizes_matching_examples()
    {
        var scene = new Scene(1920, 1080, "Scene");
        using var session = new AgentToolkitTestSession(scene);
        var manager = new AgentSessionManager();
        manager.UseSource(new AgentToolkitTestSessionSource(session));
        var queryTools = new QueryTools(manager);
        var editTools = new EditTools(manager);

        ToolResult<GetExamplesResponse> example = queryTools.GetExamples(name: "create-empty-scene-orbital-radar");
        ToolResult<ReconcileResult> apply = editTools.ApplyEdit(
            patch: example.Value!.Examples.Single().Patch,
            schemaVersion: SchemaVersion.Current);
        ToolResult<ListCompositionsResponse> compositions = queryTools.ListCompositions(seed: "fallback-recent");
        ToolResult<ListExamplesResponse> examples = queryTools.ListExamples(category: "motion");
        string[] exampleNames = examples.Value!.Examples.Select(item => item.Name).ToArray();
        int orbitalIndex = Array.FindIndex(exampleNames, name => name == "create-empty-scene-orbital-radar");
        int nonOrbitalIndex = Array.FindIndex(exampleNames, name => CompositionTemplateCatalog.TryInferTemplateNameFromExampleName(name) != "orbital-radar-map");

        Assert.Multiple(() =>
        {
            Assert.That(example.IsSuccess, Is.True, example.Error?.Message);
            Assert.That(apply.IsSuccess, Is.True, apply.Error?.Message);
            Assert.That(compositions.IsSuccess, Is.True, compositions.Error?.Message);
            Assert.That(compositions.Value!.RecentlyUsedCompositions, Does.Contain("orbital-radar-map"));
            Assert.That(compositions.Value.Compositions.Last().Name, Is.EqualTo("orbital-radar-map"));
            Assert.That(examples.IsSuccess, Is.True, examples.Error?.Message);
            Assert.That(orbitalIndex, Is.GreaterThan(nonOrbitalIndex));
        });
    }

    [Test]
    public void Read_document_returns_full_document_and_scoped_subtree()
    {
        var scene = new Scene(1920, 1080, "Scene");
        string dir = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        scene.Uri = new Uri(Path.Combine(dir, "Scene.scene"));
        var element = new Element
        {
            Start = TimeSpan.FromSeconds(1),
            Length = TimeSpan.FromSeconds(2)
        };
        element.AddObject(new TextBlock { Text = { CurrentValue = "Scoped" } });
        element.Uri = new Uri(Path.Combine(TestContext.CurrentContext.WorkDirectory, $"{Guid.NewGuid():N}.belm"));
        scene.Children.Add(element);

        using var session = new AgentToolkitTestSession(scene);
        var manager = new AgentSessionManager();
        manager.UseSource(new AgentToolkitTestSessionSource(session));
        var tools = new QueryTools(manager);

        var full = tools.ReadDocument();
        var scoped = tools.ReadDocument(element.Id.ToString());

        Assert.Multiple(() =>
        {
            Assert.That(full.IsSuccess, Is.True);
            Assert.That(full.Value!.Document["Elements"]!.AsArray(), Has.Count.EqualTo(1));
            Assert.That(scoped.IsSuccess, Is.True);
            Assert.That(scoped.Value!.Document["Id"]!.GetValue<string>(), Is.EqualTo(element.Id.ToString()));
            Assert.That(scoped.Value.SchemaVersion, Is.EqualTo(SchemaVersion.Current));
        });
    }

    [Test]
    public void Read_document_unknown_root_id_returns_stale_handle()
    {
        using var session = new AgentToolkitTestSession(new Scene());
        var manager = new AgentSessionManager();
        manager.UseSource(new AgentToolkitTestSessionSource(session));
        var tools = new QueryTools(manager);

        var result = tools.ReadDocument(Guid.NewGuid().ToString());

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error!.Code, Is.EqualTo(ErrorCode.StaleHandle));
        });
    }

    [Test]
    public void Read_document_summary_returns_compact_scene_shape()
    {
        var scene = new Scene(1280, 720, "Summary")
        {
            Duration = TimeSpan.FromSeconds(8),
            Uri = new Uri(Path.Combine(TestContext.CurrentContext.WorkDirectory, $"{Guid.NewGuid():N}.scene"))
        };
        var element = new Element
        {
            Name = "Title element",
            Start = TimeSpan.FromSeconds(1),
            Length = TimeSpan.FromSeconds(4),
            ZIndex = 3,
            Uri = new Uri(Path.Combine(TestContext.CurrentContext.WorkDirectory, $"{Guid.NewGuid():N}.belm"))
        };
        var text = new TextBlock
        {
            Name = "Title",
            Text = { CurrentValue = "Summary" },
            Fill = { CurrentValue = new SolidColorBrush(Colors.White) },
            FilterEffect = { CurrentValue = new FilterEffectGroup { Children = { new Blur() } } },
            Transform =
            {
                CurrentValue = new TransformGroup
                {
                    Children =
                    {
                        new TranslateTransform(20, 30),
                        new RotationTransform(12)
                    }
                }
            }
        };
        text.Opacity.Animation = new KeyFrameAnimation<float>();
        ((TranslateTransform)((TransformGroup)text.Transform.CurrentValue!).Children[0]).X.Animation = CreateFloatAnimation();
        ((RotationTransform)((TransformGroup)text.Transform.CurrentValue!).Children[1]).Rotation.Animation = CreateFloatAnimation();
        element.AddObject(text);
        scene.Children.Add(element);

        using var session = new AgentToolkitTestSession(scene);
        var manager = new AgentSessionManager();
        manager.UseSource(new AgentToolkitTestSessionSource(session));
        var tools = new QueryTools(manager);

        ToolResult<DocumentSummaryResponse> result = tools.ReadDocumentSummary();
        ElementSummary elementSummary = result.Value!.Elements.Single();
        ObjectSummary objectSummary = elementSummary.Objects.Single();

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
            Assert.That(result.Value!.Width, Is.EqualTo(1280));
            Assert.That(result.Value.Height, Is.EqualTo(720));
            Assert.That(result.Value.Duration, Is.EqualTo("00:00:08"));
            Assert.That(result.Value.ElementCount, Is.EqualTo(1));
            Assert.That(elementSummary.Name, Is.EqualTo("Title element"));
            Assert.That(elementSummary.Start, Is.EqualTo("00:00:01"));
            Assert.That(elementSummary.Length, Is.EqualTo("00:00:04"));
            Assert.That(objectSummary.Discriminator, Is.EqualTo("[Beutl.Engine]Beutl.Graphics.Shapes:TextBlock"));
            Assert.That(objectSummary.AnimatedProperties, Does.Contain(nameof(TextBlock.Opacity)));
            Assert.That(objectSummary.BrushProperties, Does.Contain(nameof(TextBlock.Fill)));
            Assert.That(objectSummary.EffectProperties, Does.Contain(nameof(TextBlock.FilterEffect)));
            Assert.That(objectSummary.NestedAnimatedProperties, Does.Contain("Transform.Children[0].X"));
            Assert.That(objectSummary.NestedAnimatedProperties, Does.Contain("Transform.Children[1].Rotation"));
        });
    }

    private sealed class VolatileLiveSessionSource : ISessionSource, IDisposable
    {
        private readonly AgentToolkitTestSession _inner;

        public VolatileLiveSessionSource(Scene scene)
        {
            _inner = new AgentToolkitTestSession(scene);
        }

        public EditingSessionSource Source => EditingSessionSource.LiveEditor;

        public IEditingSession? CurrentSession => new VolatileLiveSession(_inner);

        public void Dispose()
        {
            _inner.Dispose();
        }
    }

    private sealed class VolatileLiveSession(AgentToolkitTestSession inner) : IEditingSession
    {
        public string SessionId { get; } = Guid.NewGuid().ToString("N");

        public EditingSessionSource Source => EditingSessionSource.LiveEditor;

        public CoreObject Root => inner.Root;

        public HistoryManager History => inner.History;

        public DocumentAdapter Documents => inner.Documents;

        public bool IsDirty => inner.IsDirty;
    }

    private static KeyFrameAnimation<float> CreateFloatAnimation()
    {
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(
            new KeyFrame<float>
            {
                KeyTime = TimeSpan.Zero,
                Value = 0,
                Easing = new LinearEasing()
            },
            out _);
        animation.KeyFrames.Add(
            new KeyFrame<float>
            {
                KeyTime = TimeSpan.FromSeconds(1),
                Value = 1,
                Easing = new SineEaseOut()
            },
            out _);
        return animation;
    }
}
