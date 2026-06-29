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
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Serialization;

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
            Assert.That(result.Value!.RecommendedCalls, Has.Some.Contains("attach_active_editor for an open editor scene"));
            Assert.That(result.Value.RecommendedCalls, Has.Some.Contains("create_project or open_project"));
            Assert.That(result.Value.RecommendedCalls, Has.Some.Contains("file-backed session"));
            Assert.That(result.Value.RecommendedCalls, Has.Some.Contains("measure_object_bounds"));
            Assert.That(result.Value.RecommendedCalls, Has.Some.Contains("list_creative_directions"));
            Assert.That(result.Value.RecommendedCalls, Has.Some.Contains("custom declarative patch"));
            Assert.That(result.Value.RecommendedCalls, Has.Some.Contains("avoid overused orbit/radar/map/signal/dashboard motifs"));
            Assert.That(result.Value.RecommendedCalls, Has.Some.Contains("objective, audience"));
            Assert.That(result.Value.RecommendedCalls, Has.Some.Contains("one primary focal point"));
            Assert.That(result.Value.RecommendedCalls, Has.Some.Contains("read time"));
            Assert.That(result.Value.RecommendedCalls, Has.Some.Contains("effect purpose"));
            Assert.That(result.Value.RecommendedCalls, Has.Some.Contains("list_compositions"));
            Assert.That(result.Value.RecommendedCalls, Has.Some.Contains("explicitly asks for a reusable template"));
            Assert.That(result.Value.RecommendedCalls, Has.Some.Contains("specific returned name"));
            Assert.That(result.Value.RecommendedCalls, Has.None.Contains("first returned composition"));
            Assert.That(result.Value.RecommendedCalls, Has.Some.Contains("planId"));
            Assert.That(result.Value.RecommendedCalls, Has.Some.Contains("list_effects"));
            Assert.That(result.Value.RecommendedCalls, Has.Some.Contains("list_effect_recipes"));
            Assert.That(result.Value.RecommendedCalls, Has.Some.Contains("full-scene starters are hidden by default"));
            Assert.That(result.Value.RecommendedCalls, Has.Some.Contains("[Beutl.ProjectSystem]:Element"));
            Assert.That(result.Value.RecommendedCalls, Has.Some.Contains("insert-new-element-skeleton"));
            Assert.That(result.Value.RecommendedCalls, Has.Some.Contains("SKSLScriptEffect"));
            Assert.That(result.Value.RecommendedCalls, Has.Some.Contains("UseGlobalClock=false uses Element-local KeyTime values"));
            Assert.That(result.Value.RecommendedCalls, Has.Some.Contains("evaluate_motion_variation"));
            Assert.That(result.Value.CategoryAliases["visualEffect"], Is.EqualTo("FilterEffect"));
            Assert.That(result.Value.RawHttpNote, Does.Contain("Server-Sent Events"));
            Assert.That(result.Value.RawHttpNote, Does.Contain("content[0].text"));
        });
    }

    [Test]
    public void Get_schema_accepts_common_low_context_type_aliases()
    {
        var tools = new QueryTools(new AgentSessionManager());

        ToolResult<CapabilitySchema> shape = tools.GetSchema(type: "Shape", includeProperties: false);
        ToolResult<CapabilitySchema> text = tools.GetSchema(type: "Text", includeProperties: false);
        ToolResult<CapabilitySchema> textCategory = tools.GetSchema(category: "text", includeProperties: false);
        ToolResult<CapabilitySchema> transform = tools.GetSchema(type: "Transform", includeProperties: false);

        Assert.Multiple(() =>
        {
            Assert.That(shape.IsSuccess, Is.True, shape.Error?.Message);
            Assert.That(shape.Value!.Types.Select(type => type.Type), Does.Contain(typeof(RectShape).FullName));
            Assert.That(shape.Value.Types.Select(type => type.Type), Does.Contain(typeof(EllipseShape).FullName));
            Assert.That(text.IsSuccess, Is.True, text.Error?.Message);
            Assert.That(text.Value!.Types.Select(type => type.Type), Is.EquivalentTo(new[] { typeof(TextBlock).FullName }));
            Assert.That(textCategory.IsSuccess, Is.True, textCategory.Error?.Message);
            Assert.That(textCategory.Value!.Types.Select(type => type.Type), Is.EquivalentTo(new[] { typeof(TextBlock).FullName }));
            Assert.That(transform.IsSuccess, Is.True, transform.Error?.Message);
            Assert.That(transform.Value!.Types.Select(type => type.Type), Does.Contain(typeof(TransformGroup).FullName));
            Assert.That(transform.Value.Types.Select(type => type.Type), Does.Contain(typeof(TranslateTransform).FullName));
            Assert.That(transform.Value.Types.Select(type => type.Type), Does.Contain(typeof(RotationTransform).FullName));
            Assert.That(transform.Value.Types.Select(type => type.Type), Does.Contain(typeof(ScaleTransform).FullName));
        });
    }

    [Test]
    public void Get_schema_explains_timeline_element_container_requests()
    {
        var tools = new QueryTools(new AgentSessionManager());

        ToolResult<CapabilitySchema> result = tools.GetSchema(type: "Element", includeProperties: false);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error!.Code, Is.EqualTo(ErrorCode.UnknownType));
            Assert.That(result.Error.Hint, Does.Contain("[Beutl.ProjectSystem]:Element"));
            Assert.That(result.Error.Hint, Does.Contain("Objects"));
            Assert.That(result.Error.Hint, Does.Contain("insert-new-element-skeleton"));
        });
    }

    [Test]
    public void Creative_directions_discourage_reused_orbit_radar_motifs()
    {
        var tools = new QueryTools(new AgentSessionManager());

        ToolResult<CreativeDirectionResponse> result = tools.ListCreativeDirections("make a short motion graphic");

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
            Assert.That(result.Value!.DirectionAxes, Has.Count.GreaterThanOrEqualTo(6));
            Assert.That(result.Value.InspirationSeeds, Has.Count.GreaterThanOrEqualTo(8));
            Assert.That(result.Value.InspirationSeeds.Select(seed => seed.Name), Does.Not.Contain("Projected ink fold"));
            Assert.That(result.Value.InspirationSeeds.Select(seed => seed.Category), Does.Contain("material"));
            Assert.That(result.Value.InspirationSeeds.Select(seed => seed.Category), Does.Contain("motion"));
            Assert.That(result.Value.InspirationSeeds.Select(seed => seed.Category), Does.Contain("composition"));
            Assert.That(result.Value.InspirationSeeds.Select(seed => seed.Category), Does.Contain("typography"));
            Assert.That(result.Value.InspirationSeeds.Select(seed => seed.Category), Does.Contain("procedural surface"));
            Assert.That(result.Value.InspirationSeeds.SelectMany(seed => seed.UsefulTools), Does.Contain("SKSLScriptEffect"));
            Assert.That(result.Value.CombinationRules, Has.Some.Contains("Synthesize a new pitch"));
            Assert.That(result.Value.CombinationRules, Has.Some.Contains("direction contract"));
            Assert.That(result.Value.OriginalityConstraints, Has.Some.Contains("Do not implement any returned seed as a complete scene"));
            Assert.That(result.Value.OriginalityConstraints, Has.Some.Contains("Do not use returned seed names"));
            Assert.That(result.Value.VariationPrompts, Has.Some.Contains("Invert the seed relationship"));
            Assert.That(result.Value.OverusedMotifs, Has.Some.Contains("orbit rings"));
            Assert.That(result.Value.OverusedMotifs, Has.Some.Contains("radar sweeps"));
            Assert.That(result.Value.OverusedMotifs, Has.Some.Contains("dark teal background with cyan/magenta neon"));
            Assert.That(result.Value.WorkflowHints, Has.Some.Contains("Do not default to the first returned seed"));
            Assert.That(result.Value.WorkflowHints, Has.Some.Contains("synthesize a new pitch"));
            Assert.That(result.Value.WorkflowHints, Has.Some.Contains("evaluate_motion_variation"));
            Assert.That(result.Value.WorkflowHints, Has.Some.Contains("SKSLScriptEffect"));
            Assert.That(result.Value.WorkflowHints, Has.Some.Contains("one primary focal point"));
            Assert.That(result.Value.WorkflowHints, Has.Some.Contains("effect chain"));
            Assert.That(result.Value.WorkflowHints, Has.Some.Contains("UseGlobalClock=false uses Element-local KeyTime values"));
            Assert.That(result.Value.DirectionAxes, Has.Some.Contains("procedural surface"));
            Assert.That(result.Value.SelectionHint, Does.Contain("combine at least two inspiration seeds"));
            Assert.That(result.Value.SelectionHint, Does.Contain("make a short motion graphic"));
            Assert.That(result.Value.SelectionTrace, Is.Not.Null);
            Assert.That(result.Value.SelectionTrace!.RequestIndex, Is.EqualTo(0));
            Assert.That(result.Value.SelectionTrace.ReturnedSeedOrder, Is.EqualTo(result.Value.InspirationSeeds.Select(seed => seed.Name)));
            Assert.That(result.Value.SelectionTrace.RecordHint, Does.Contain("returnedSeedOrder"));
        });
    }

    [Test]
    public void Creative_directions_rotate_leading_seed_between_calls()
    {
        var tools = new QueryTools(new AgentSessionManager());

        ToolResult<CreativeDirectionResponse> first = tools.ListCreativeDirections("abstract motion graphic");
        ToolResult<CreativeDirectionResponse> second = tools.ListCreativeDirections("abstract motion graphic");

        Assert.Multiple(() =>
        {
            Assert.That(first.IsSuccess, Is.True, first.Error?.Message);
            Assert.That(second.IsSuccess, Is.True, second.Error?.Message);
            Assert.That(first.Value!.InspirationSeeds[0].Name, Is.Not.EqualTo(second.Value!.InspirationSeeds[0].Name));
            Assert.That(first.Value.SelectionTrace!.RequestIndex, Is.EqualTo(0));
            Assert.That(second.Value!.SelectionTrace!.RequestIndex, Is.EqualTo(1));
            Assert.That(second.Value.SelectionTrace.AppliedOffset, Is.EqualTo((first.Value.SelectionTrace.AppliedOffset + 1) % first.Value.InspirationSeeds.Count));
        });
    }

    [Test]
    public void Get_schema_omits_examples_by_default()
    {
        var tools = new QueryTools(new AgentSessionManager());

        ToolResult<CapabilitySchema> defaultSchema = tools.GetSchema();
        ToolResult<CapabilitySchema> schemaWithExamples = tools.GetSchema(includeExamples: true);

        Assert.Multiple(() =>
        {
            Assert.That(defaultSchema.IsSuccess, Is.True, defaultSchema.Error?.Message);
            Assert.That(defaultSchema.Value!.Types, Is.Not.Empty);
            Assert.That(defaultSchema.Value.Examples, Is.Empty);
            Assert.That(schemaWithExamples.IsSuccess, Is.True, schemaWithExamples.Error?.Message);
            Assert.That(schemaWithExamples.Value!.Examples.Select(example => example.Name), Does.Contain("create-empty-scene-motion-graphics"));
        });
    }

    [Test]
    public void Examples_can_be_listed_and_fetched_by_name()
    {
        var tools = new QueryTools(new AgentSessionManager());

        ToolResult<ListExamplesResponse> defaultList = tools.ListExamples(type: nameof(TextBlock));
        ToolResult<ListExamplesResponse> list = tools.ListExamples(type: nameof(TextBlock), includeStarters: true);
        ToolResult<ListExamplesResponse> structureList = tools.ListExamples(category: "structure");
        ToolResult<ListExamplesResponse> defaultMotionList = tools.ListExamples(category: "motion");
        ToolResult<ListExamplesResponse> motionList = tools.ListExamples(category: "motion", includeStarters: true);
        string selectedName = list.Value!.Examples
            .Single(example => example.Name == "create-empty-scene-split-screen-typography")
            .Name;
        ToolResult<GetExamplesResponse> skeleton = tools.GetExamples(name: "insert-new-element-skeleton");
        ToolResult<GetExamplesResponse> defaultExamples = tools.GetExamples(category: "motion");
        ToolResult<GetExamplesResponse> selected = tools.GetExamples(name: selectedName);

        Assert.Multiple(() =>
        {
            Assert.That(defaultList.IsSuccess, Is.True, defaultList.Error?.Message);
            Assert.That(defaultList.Value!.Examples.Count(example => example.Tags.Contains("empty-scene")), Is.Zero);
            Assert.That(defaultList.Value.SelectionHint, Does.Contain("hidden by default"));
            Assert.That(list.IsSuccess, Is.True, list.Error?.Message);
            Assert.That(list.Value!.Examples.Count(example => example.Tags.Contains("empty-scene")), Is.GreaterThanOrEqualTo(3));
            Assert.That(list.Value.SelectionHint, Does.Contain("includeStarters=true"));
            Assert.That(structureList.IsSuccess, Is.True, structureList.Error?.Message);
            Assert.That(structureList.Value!.Examples.Select(example => example.Name), Does.Contain("insert-new-element-skeleton"));
            Assert.That(skeleton.IsSuccess, Is.True, skeleton.Error?.Message);
            Assert.That(skeleton.Value!.Examples, Has.Count.EqualTo(1));
            Assert.That(skeleton.Value.Examples.Single().Patch.ToJsonString(), Does.Contain("[Beutl.ProjectSystem]:Element"));
            Assert.That(skeleton.Value.Examples.Single().Patch.ToJsonString(), Does.Contain("\"$type\""));
            Assert.That(defaultMotionList.IsSuccess, Is.True, defaultMotionList.Error?.Message);
            Assert.That(defaultMotionList.Value!.Examples, Is.Empty);
            Assert.That(defaultExamples.IsSuccess, Is.True, defaultExamples.Error?.Message);
            Assert.That(defaultExamples.Value!.Examples, Is.Empty);
            Assert.That(defaultExamples.Value.SelectionHint, Does.Contain("Full-scene starters are hidden by default"));
            Assert.That(motionList.IsSuccess, Is.True, motionList.Error?.Message);
            Assert.That(motionList.Value!.Examples.Select(example => example.Name), Does.Contain("create-empty-scene-liquid-gradient-system"));
            Assert.That(motionList.Value.Examples.Select(example => example.Name), Does.Contain("create-empty-scene-data-bar-dashboard"));
            Assert.That(motionList.Value.Examples.Select(example => example.Name), Does.Contain("create-empty-scene-glitch-cutout-collage"));
            Assert.That(selected.IsSuccess, Is.True, selected.Error?.Message);
            Assert.That(selected.Value!.Examples, Has.Count.EqualTo(1));
            Assert.That(selected.Value.Examples.Single().Name, Is.EqualTo(selectedName));
            Assert.That(selected.Value.Examples.Single().Patch.ToJsonString(), Does.Contain("Frame flow"));
        });
    }

    [Test]
    public void Composition_tools_list_detail_and_render_seeded_patch()
    {
        var tools = new QueryTools(new AgentSessionManager());
        var inputProps = new JsonObject
        {
            ["title"] = "Patch title",
            ["durationSeconds"] = 5,
            ["fps"] = 20
        };

        ToolResult<ListCompositionsResponse> list = tools.ListCompositions(seed: "tool-seed");
        ToolResult<ListCompositionsResponse> motionList = tools.ListCompositions(tag: "motion", seed: "tool-seed");
        ToolResult<GetCompositionResponse> detail = tools.GetComposition("split-screen-type-system");
        ToolResult<RenderCompositionPatchResponse> render = tools.RenderCompositionPatch(
            name: "split-screen-type-system",
            inputProps: inputProps,
            seed: "tool-seed",
            avoidRecent: false);

        Assert.Multiple(() =>
        {
            Assert.That(list.IsSuccess, Is.True, list.Error?.Message);
            Assert.That(list.Value!.Seed, Is.EqualTo("tool-seed"));
            Assert.That(list.Value.Compositions.Select(composition => composition.Name), Does.Contain("split-screen-type-system"));
            Assert.That(list.Value.SelectionHint, Does.Contain("avoidRecent"));
            Assert.That(list.Value.RecentlyUsedCompositions, Is.Empty);
            Assert.That(list.Value.PreAttachPreviewedCompositions, Is.Empty);
            Assert.That(list.Value.PreviewOnly, Is.False);
            Assert.That(motionList.IsSuccess, Is.True, motionList.Error?.Message);
            Assert.That(motionList.Value!.Compositions, Has.Count.GreaterThanOrEqualTo(6));
            Assert.That(detail.IsSuccess, Is.True, detail.Error?.Message);
            Assert.That(detail.Value!.Composition.DefaultProps["title"]!.GetValue<string>(), Is.EqualTo("Frame flow"));
            Assert.That(detail.Value.Composition.Sequences.Any(sequence => sequence.Name == "typography"), Is.True);
            Assert.That(render.IsSuccess, Is.True, render.Error?.Message);
            Assert.That(render.Value!.Composition.Seed, Is.EqualTo("tool-seed"));
            Assert.That(render.Value.Composition.Metadata.DurationInFrames, Is.EqualTo(100));
            Assert.That(render.Value.Composition.ResolvedProps["title"]!.GetValue<string>(), Is.EqualTo("Patch title"));
            Assert.That(render.Value.Composition.Patch.ToJsonString(), Does.Contain("Patch title"));
            Assert.That(render.Value.UsageHint, Does.Contain("apply_edit"));
        });
    }

    [Test]
    public void Composition_tools_require_explicit_template_name()
    {
        var scene = new Scene(1920, 1080, "Scene");
        using var session = new AgentToolkitTestSession(scene);
        var manager = new AgentSessionManager();
        manager.UseSource(new AgentToolkitTestSessionSource(session));
        var queryTools = new QueryTools(manager);
        var editTools = new EditTools(manager);

        ToolResult<RenderCompositionPatchResponse> render = queryTools.RenderCompositionPatch();
        ToolResult<PlanCompositionResponse> plan = editTools.PlanComposition();
        ToolResult<ApplyCompositionResponse> apply = editTools.ApplyComposition();

        Assert.Multiple(() =>
        {
            Assert.That(render.IsSuccess, Is.False);
            Assert.That(render.Error!.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(render.Error.Hint, Does.Contain("original creative briefs"));
            Assert.That(plan.IsSuccess, Is.False);
            Assert.That(plan.Error!.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(plan.Error.Hint, Does.Contain("original creative briefs"));
            Assert.That(apply.IsSuccess, Is.False);
            Assert.That(apply.Error!.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(apply.Error.Hint, Does.Contain("original creative briefs"));
        });
    }

    [Test]
    public void Effect_tools_list_and_fetch_recipes()
    {
        var tools = new QueryTools(new AgentSessionManager());

        ToolResult<ListEffectsResponse> effects = tools.ListEffects(intent: "glitch");
        ToolResult<ListEffectRecipesResponse> recipes = tools.ListEffectRecipes(intent: "glitch");
        ToolResult<ListEffectRecipesResponse> glowMotionRecipes = tools.ListEffectRecipes(intent: "glow motion");
        ToolResult<ListEffectRecipesResponse> shaderRecipes = tools.ListEffectRecipes(intent: "shader organic");
        ToolResult<GetEffectRecipeResponse> recipe = tools.GetEffectRecipe(name: "effect-color-shift");
        ToolResult<GetEffectRecipeResponse> shaderRecipe = tools.GetEffectRecipe(name: "organic-shader-field");

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
            Assert.That(shaderRecipes.IsSuccess, Is.True, shaderRecipes.Error?.Message);
            Assert.That(shaderRecipes.Value!.Recipes.Select(item => item.Name), Does.Contain("organic-shader-field"));
            Assert.That(recipe.IsSuccess, Is.True, recipe.Error?.Message);
            Assert.That(recipe.Value!.Recipe.EffectNames, Does.Contain("ColorShift"));
            Assert.That(recipe.Value.Recipe.Patch.ToJsonString(), Does.Contain("ColorShift"));
            Assert.That(recipe.Value.UsageHint, Does.Contain("apply_edit"));
            Assert.That(shaderRecipe.IsSuccess, Is.True, shaderRecipe.Error?.Message);
            Assert.That(shaderRecipe.Value!.Recipe.EffectNames, Does.Contain("SKSLScriptEffect"));
            Assert.That(shaderRecipe.Value.Recipe.Patch.ToJsonString(), Does.Contain("uniform shader src"));
            Assert.That(shaderRecipe.Value.Recipe.Notes, Has.Some.Contains("render_still"));
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
    public void Named_composition_must_match_first_candidate_by_default()
    {
        var scene = new Scene(1920, 1080, "Scene");
        using var session = new AgentToolkitTestSession(scene);
        var manager = new AgentSessionManager();
        manager.UseSource(new AgentToolkitTestSessionSource(session));
        var queryTools = new QueryTools(manager);
        var editTools = new EditTools(manager);

        ToolResult<ListCompositionsResponse> list = queryTools.ListCompositions();
        string firstName = list.Value!.Compositions.First().Name;
        string nonFirstName = list.Value.Compositions.Skip(1).First().Name;

        ToolResult<PlanCompositionResponse> rejected = editTools.PlanComposition(name: nonFirstName);
        ToolResult<PlanCompositionResponse> seededRejected = editTools.PlanComposition(name: nonFirstName, seed: list.Value.Seed);
        ToolResult<PlanCompositionResponse> accepted = editTools.PlanComposition(name: firstName);
        ToolResult<PlanCompositionResponse> deliberate = editTools.PlanComposition(name: nonFirstName, avoidRecent: false);

        Assert.Multiple(() =>
        {
            Assert.That(list.IsSuccess, Is.True, list.Error?.Message);
            Assert.That(rejected.IsSuccess, Is.False);
            Assert.That(rejected.Error!.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(rejected.Error.Hint, Does.Contain(firstName));
            Assert.That(seededRejected.IsSuccess, Is.False);
            Assert.That(seededRejected.Error!.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(seededRejected.Error.Hint, Does.Contain(firstName));
            Assert.That(accepted.IsSuccess, Is.True, accepted.Error?.Message);
            Assert.That(accepted.Value!.Composition.Name, Is.EqualTo(firstName));
            Assert.That(deliberate.IsSuccess, Is.True, deliberate.Error?.Message);
            Assert.That(deliberate.Value!.Composition.Name, Is.EqualTo(nonFirstName));
        });
    }

    [Test]
    public void Pre_attach_composition_preview_is_deprioritized_after_attach()
    {
        var manager = new AgentSessionManager();
        var queryTools = new QueryTools(manager);
        var editTools = new EditTools(manager);

        ToolResult<ListCompositionsResponse> previewList = queryTools.ListCompositions();
        string previewedName = previewList.Value!.Compositions.First().Name;

        var scene = new Scene(1920, 1080, "Scene");
        using var session = new AgentToolkitTestSession(scene);
        manager.UseSource(new AgentToolkitTestSessionSource(session));

        ToolResult<ListCompositionsResponse> attachedList = queryTools.ListCompositions();
        ToolResult<PlanCompositionResponse> rejected = editTools.PlanComposition(name: previewedName);
        ToolResult<PlanCompositionResponse> deliberate = editTools.PlanComposition(name: previewedName, avoidRecent: false);

        Assert.Multiple(() =>
        {
            Assert.That(previewList.IsSuccess, Is.True, previewList.Error?.Message);
            Assert.That(previewList.Value!.PreviewOnly, Is.True);
            Assert.That(previewList.Value.PreAttachPreviewedCompositions, Does.Contain(previewedName));
            Assert.That(previewList.Value.SelectionHint, Does.Contain("pre-attach preview"));
            Assert.That(attachedList.IsSuccess, Is.True, attachedList.Error?.Message);
            Assert.That(attachedList.Value!.PreviewOnly, Is.False);
            Assert.That(attachedList.Value.PreAttachPreviewedCompositions, Does.Contain(previewedName));
            Assert.That(attachedList.Value.Compositions.First().Name, Is.Not.EqualTo(previewedName));
            Assert.That(attachedList.Value.Compositions.Last().Name, Is.EqualTo(previewedName));
            Assert.That(rejected.IsSuccess, Is.False);
            Assert.That(rejected.Error!.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(rejected.Error.Hint, Does.Contain("non-avoided"));
            Assert.That(deliberate.IsSuccess, Is.True, deliberate.Error?.Message);
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
    public void Composition_recent_applies_across_new_scene_roots()
    {
        using var firstSession = new AgentToolkitTestSession(new Scene(1920, 1080, "First"));
        using var secondSession = new AgentToolkitTestSession(new Scene(1920, 1080, "Second"));
        var manager = new AgentSessionManager();
        var queryTools = new QueryTools(manager);
        var editTools = new EditTools(manager);

        manager.UseSource(new AgentToolkitTestSessionSource(firstSession));
        ToolResult<ApplyCompositionResponse> firstApply = editTools.ApplyComposition(
            name: "orbital-radar-map",
            seed: "global-recent",
            avoidRecent: false);

        manager.UseSource(new AgentToolkitTestSessionSource(secondSession));
        ToolResult<ListCompositionsResponse> secondList = queryTools.ListCompositions(seed: "global-recent");
        ToolResult<PlanCompositionResponse> repeated = editTools.PlanComposition(
            name: "orbital-radar-map",
            seed: "global-recent");

        Assert.Multiple(() =>
        {
            Assert.That(firstApply.IsSuccess, Is.True, firstApply.Error?.Message);
            Assert.That(secondList.IsSuccess, Is.True, secondList.Error?.Message);
            Assert.That(secondList.Value!.RecentlyUsedCompositions, Does.Contain("orbital-radar-map"));
            Assert.That(secondList.Value.Compositions.Last().Name, Is.EqualTo("orbital-radar-map"));
            Assert.That(repeated.IsSuccess, Is.False);
            Assert.That(repeated.Error!.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(repeated.Error.Hint, Does.Contain("non-avoided"));
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
        ToolResult<ApplyEditResponse> apply = editTools.ApplyEdit(
            patch: example.Value!.Examples.Single().Patch,
            schemaVersion: SchemaVersion.Current);
        ToolResult<ListCompositionsResponse> compositions = queryTools.ListCompositions(seed: "fallback-recent");
        ToolResult<PlanCompositionResponse> repeated = editTools.PlanComposition(name: "orbital-radar-map", seed: "fallback-recent");
        ToolResult<ListExamplesResponse> examples = queryTools.ListExamples(category: "motion", includeStarters: true);
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
            Assert.That(repeated.IsSuccess, Is.False);
            Assert.That(repeated.Error!.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(repeated.Error.Hint, Does.Contain("non-avoided"));
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

    [Test]
    public void Measure_object_bounds_reports_center_aligned_scene_bounds()
    {
        var scene = new Scene(1920, 1080, "Measure")
        {
            Duration = TimeSpan.FromSeconds(5),
            Uri = new Uri(Path.Combine(TestContext.CurrentContext.WorkDirectory, $"{Guid.NewGuid():N}.scene"))
        };
        var titleElement = new Element
        {
            Name = "Title element",
            Length = TimeSpan.FromSeconds(5),
            ZIndex = 2,
            Uri = new Uri(Path.Combine(TestContext.CurrentContext.WorkDirectory, $"{Guid.NewGuid():N}.belm"))
        };
        var title = new TextBlock
        {
            Name = "Centered title",
            Text = { CurrentValue = "Center" },
            Size = { CurrentValue = 100 }
        };
        titleElement.AddObject(title);
        scene.Children.Add(titleElement);

        var plateElement = new Element
        {
            Name = "Plate element",
            Length = TimeSpan.FromSeconds(5),
            ZIndex = 1,
            Uri = new Uri(Path.Combine(TestContext.CurrentContext.WorkDirectory, $"{Guid.NewGuid():N}.belm"))
        };
        var plate = new RoundedRectShape
        {
            Name = "Offset plate",
            Width = { CurrentValue = 200 },
            Height = { CurrentValue = 80 },
            Transform = { CurrentValue = new TranslateTransform(120, -40) }
        };
        plateElement.AddObject(plate);
        scene.Children.Add(plateElement);

        using var session = new AgentToolkitTestSession(scene);
        var manager = new AgentSessionManager();
        manager.UseSource(new AgentToolkitTestSessionSource(session));
        var tools = new QueryTools(manager);

        ToolResult<ObjectBoundsMeasurementResponse> all = tools.MeasureObjectBounds(timeSeconds: 0);
        ToolResult<ObjectBoundsMeasurementResponse> plateOnly = tools.MeasureObjectBounds(plate.Id.ToString(), timeSeconds: 0);
        ToolResult<ObjectBoundsMeasurementResponse> late = tools.MeasureObjectBounds(timeSeconds: 6);
        ObjectBoundsMeasurement titleBounds = all.Value!.Objects.Single(item => item.ObjectId == title.Id.ToString());
        ObjectBoundsMeasurement plateBounds = all.Value.Objects.Single(item => item.ObjectId == plate.Id.ToString());

        Assert.Multiple(() =>
        {
            Assert.That(all.IsSuccess, Is.True, all.Error?.Message);
            Assert.That(all.Value!.Objects, Has.Count.EqualTo(2));
            Assert.That(all.Value.FrameCenter.X, Is.EqualTo(960));
            Assert.That(all.Value.FrameCenter.Y, Is.EqualTo(540));
            Assert.That(all.Value.TimeFiltered, Is.True);
            Assert.That(titleBounds.MeasurementKind, Is.EqualTo("render-node-operation-bounds"));
            Assert.That(titleBounds.LocalBounds.Width, Is.GreaterThan(0));
            Assert.That(titleBounds.LocalBounds.Height, Is.GreaterThan(0));
            Assert.That(titleBounds.TransformedBounds.Left, Is.LessThan(960));
            Assert.That(titleBounds.TransformedBounds.Right, Is.GreaterThan(960));
            Assert.That(titleBounds.TransformedBounds.Top, Is.LessThan(540));
            Assert.That(titleBounds.TransformedBounds.Bottom, Is.GreaterThan(540));
            Assert.That(plateBounds.MeasurementKind, Is.EqualTo("render-node-operation-bounds"));
            Assert.That(plateBounds.LocalBounds.Width, Is.EqualTo(200).Within(0.01));
            Assert.That(plateBounds.LocalBounds.Height, Is.EqualTo(80).Within(0.01));
            Assert.That(plateBounds.UserTranslate!.X, Is.EqualTo(120).Within(0.01));
            Assert.That(plateBounds.UserTranslate.Y, Is.EqualTo(-40).Within(0.01));
            Assert.That(plateBounds.TransformedBounds.Left, Is.EqualTo(980).Within(0.01));
            Assert.That(plateBounds.TransformedBounds.Top, Is.EqualTo(460).Within(0.01));
            Assert.That(plateBounds.Center.X, Is.EqualTo(1080).Within(0.01));
            Assert.That(plateBounds.Center.Y, Is.EqualTo(500).Within(0.01));
            Assert.That(plateOnly.IsSuccess, Is.True, plateOnly.Error?.Message);
            Assert.That(plateOnly.Value!.Objects.Single().ObjectId, Is.EqualTo(plate.Id.ToString()));
            Assert.That(late.IsSuccess, Is.True, late.Error?.Message);
            Assert.That(late.Value!.Objects, Is.Empty);
        });
    }

    [Test]
    public void Measure_object_bounds_includes_filter_effect_render_node_bounds()
    {
        var scene = new Scene(200, 200, "Measure effect")
        {
            Duration = TimeSpan.FromSeconds(5),
            Uri = new Uri(Path.Combine(TestContext.CurrentContext.WorkDirectory, $"{Guid.NewGuid():N}.scene"))
        };
        var element = new Element
        {
            Name = "Plate element",
            Length = TimeSpan.FromSeconds(5),
            Uri = new Uri(Path.Combine(TestContext.CurrentContext.WorkDirectory, $"{Guid.NewGuid():N}.belm"))
        };
        var plate = new RoundedRectShape
        {
            Name = "Shadowed plate",
            Width = { CurrentValue = 100 },
            Height = { CurrentValue = 50 },
            FilterEffect =
            {
                CurrentValue = new DropShadow
                {
                    Position = { CurrentValue = new Point(10, 5) },
                    Sigma = { CurrentValue = new Size(3, 4) },
                    Color = { CurrentValue = Colors.Black }
                }
            }
        };
        element.AddObject(plate);
        scene.Children.Add(element);

        using var session = new AgentToolkitTestSession(scene);
        var manager = new AgentSessionManager();
        manager.UseSource(new AgentToolkitTestSessionSource(session));
        var tools = new QueryTools(manager);

        ToolResult<ObjectBoundsMeasurementResponse> result = tools.MeasureObjectBounds(plate.Id.ToString(), timeSeconds: 0);
        ObjectBoundsMeasurement bounds = result.Value!.Objects.Single();

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
            Assert.That(bounds.MeasurementKind, Is.EqualTo("render-node-operation-bounds"));
            Assert.That(bounds.TransformedBounds.Left, Is.EqualTo(50).Within(0.01));
            Assert.That(bounds.TransformedBounds.Top, Is.LessThan(75));
            Assert.That(bounds.TransformedBounds.Right, Is.GreaterThan(150));
            Assert.That(bounds.TransformedBounds.Bottom, Is.GreaterThan(125));
            Assert.That(bounds.LocalBounds.Width, Is.EqualTo(bounds.TransformedBounds.Width).Within(0.01));
            Assert.That(bounds.LocalBounds.Height, Is.EqualTo(bounds.TransformedBounds.Height).Within(0.01));
            Assert.That(bounds.Note, Does.Contain("DrawableRenderNode"));
        });
    }

    [Test]
    public void Read_document_summary_exposes_fallback_objects()
    {
        var scene = new Scene(1280, 720, "Fallback summary")
        {
            Duration = TimeSpan.FromSeconds(2),
            Uri = new Uri(Path.Combine(TestContext.CurrentContext.WorkDirectory, $"{Guid.NewGuid():N}.scene"))
        };
        var element = new Element
        {
            Name = "Fallback element",
            Length = TimeSpan.FromSeconds(2),
            Uri = new Uri(Path.Combine(TestContext.CurrentContext.WorkDirectory, $"{Guid.NewGuid():N}.belm"))
        };
        var fallback = new FallbackEngineObject
        {
            Name = "Fallback rect",
            Reason = FallbackReason.DeserializationFailed,
            ErrorMessage = "Width: The JSON value could not be converted.",
            Json = new JsonObject
            {
                ["$type"] = IdentityHelper.WriteDiscriminator(typeof(RectShape))
            }
        };
        element.AddObject(fallback);
        scene.Children.Add(element);

        using var session = new AgentToolkitTestSession(scene);
        var manager = new AgentSessionManager();
        manager.UseSource(new AgentToolkitTestSessionSource(session));
        var tools = new QueryTools(manager);

        ToolResult<DocumentSummaryResponse> result = tools.ReadDocumentSummary();
        ObjectSummary objectSummary = result.Value!.Elements.Single().Objects.Single();

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
            Assert.That(objectSummary.IsFallback, Is.True);
            Assert.That(objectSummary.FallbackReason, Is.EqualTo(nameof(FallbackReason.DeserializationFailed)));
            Assert.That(objectSummary.FallbackTypeName, Is.EqualTo(IdentityHelper.WriteDiscriminator(typeof(RectShape))));
            Assert.That(objectSummary.FallbackMessage, Does.Contain("Width"));
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
