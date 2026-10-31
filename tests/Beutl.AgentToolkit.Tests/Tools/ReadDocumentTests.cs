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
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes;
using Beutl.NodeGraph.Nodes.Utilities;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.AgentToolkit.Tests.Tools;

public sealed class ReadDocumentTests
{
    [Test]
    public void Any_named_composition_can_be_applied_repeatedly_after_listing()
    {
        using var session = new AgentToolkitTestSession(new Scene(640, 360, "Scene"));
        var manager = new AgentSessionManager();
        var query = new QueryTools(manager);
        string name = query.ListCompositions().Value!.Compositions[1].Name;
        manager.UseSource(new AgentToolkitTestSessionSource(session));
        var edit = new EditTools(manager);

        ToolResult<ApplyCompositionResponse> first = edit.ApplyComposition(name: name);
        ToolResult<ApplyCompositionResponse> second = edit.ApplyComposition(name: name);
        ToolResult<PlanCompositionResponse> plan = edit.PlanComposition(name: name);
        Assert.Multiple(() =>
        {
            Assert.That(first.IsSuccess, Is.True, first.Error?.Message);
            Assert.That(second.IsSuccess, Is.True, second.Error?.Message);
            Assert.That(plan.IsSuccess, Is.True, plan.Error?.Message);
        });
    }

    [Test]
    public void Get_started_returns_editing_entrypoints_without_a_session()
    {
        var tools = new QueryTools(new AgentSessionManager());
        ToolResult<GettingStartedResponse> result = tools.GetStarted();

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
            Assert.That(result.Value!.SchemaVersion, Is.EqualTo(SchemaVersion.Current));
            Assert.That(result.Value.Essentials, Is.Not.Empty);
            Assert.That(result.Value.CategoryAliases["visualEffect"], Is.EqualTo("FilterEffect"));
            Assert.That(result.Value.RawHttpNote, Does.Contain("Server-Sent Events"));
            Assert.That(result.Value.RawHttpNote, Does.Contain("content[0].text"));
        });
    }

    [Test]
    public void Measure_object_bounds_reports_an_off_frame_drawable_where_it_actually_is()
    {
        string dir = TestWorkspace.CreateDirectory();
        var scene = new Scene(320, 180, "off-frame-measure")
        {
            Duration = { Value = MediaTime.FromSeconds(2) },
            Uri = new Uri(Path.Combine(dir, "Scene.scene"))
        };
        var plate = new RectShape
        {
            Name = { Value = "OffFrame" },
            Width = { Value = 40 },
            Height = { Value = 20 },
            Fill = { Value = Brushes.White },
            AlignmentX = { Value = AlignmentX.Left },
            AlignmentY = { Value = AlignmentY.Top },
            Transform = { Value = new TranslateTransform(600, 400) },
        };
        var element = new Element
        {
            Length = { Value = MediaTime.FromSeconds(2) },
            Uri = new Uri(Path.Combine(dir, "element.belm"))
        };
        element.AddObject(plate);
        scene.Children.Add(element);
        using var session = new AgentToolkitTestSession(scene);
        AgentSessionManager manager = session.CreateManager();
        var tools = new QueryTools(manager);

        ToolResult<ObjectBoundsMeasurementResponse> result = tools.MeasureObjectBounds(
            objectId: plate.Id.Value.ToString(),
            timeSeconds: 0.0);

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        ObjectBoundsMeasurement measured = result.Value!.Objects.Single();
        Assert.Multiple(() =>
        {
            Assert.That(measured.TransformedBounds.Width, Is.EqualTo(40).Within(0.5));
            Assert.That(measured.TransformedBounds.Height, Is.EqualTo(20).Within(0.5));
            Assert.That(measured.TransformedBounds.Left, Is.EqualTo(600).Within(0.5));
            Assert.That(measured.TransformedBounds.Top, Is.EqualTo(400).Within(0.5));
        });
    }

    [Test]
    public void Measure_object_bounds_reports_a_nested_drawable_as_unsupported_even_with_a_time_filter()
    {
        string dir = TestWorkspace.CreateDirectory();
        var scene = new Scene(320, 180, "nested-measure")
        {
            Duration = { Value = MediaTime.FromSeconds(2) },
            Uri = new Uri(Path.Combine(dir, "Scene.scene"))
        };
        var group = new DrawableGroup { Name = { Value = "wrapper" } };
        var nested = new TextBlock { Name = { Value = "nested" }, Text = { Value = "Hi" } };
        group.Children.Add(nested);
        var element = new Element
        {
            Length = { Value = MediaTime.FromSeconds(2) },
            Uri = new Uri(Path.Combine(dir, "element.belm"))
        };
        element.AddObject(group);
        scene.Children.Add(element);
        using var session = new AgentToolkitTestSession(scene);
        AgentSessionManager manager = session.CreateManager();
        var tools = new QueryTools(manager);

        // A time filter must not turn "found but nested (unsupported)" into an empty, successful result.
        ToolResult<ObjectBoundsMeasurementResponse> result = tools.MeasureObjectBounds(
            objectId: nested.Id.Value.ToString(),
            timeSeconds: 0.0);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error!.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(result.Error.Message, Does.Contain("not a direct object"));
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
            Assert.That(schemaWithExamples.Value!.Examples.Select(example => example.Name), Does.Contain("create-empty-scene-kinetic-ribbon-title"));
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
            .Single(example => example.Name == "create-empty-scene-split-screen-type-system")
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
            seed: "tool-seed");

        Assert.Multiple(() =>
        {
            Assert.That(list.IsSuccess, Is.True, list.Error?.Message);
            Assert.That(list.Value!.Seed, Is.EqualTo("tool-seed"));
            Assert.That(list.Value.Compositions.Select(composition => composition.Name), Does.Contain("split-screen-type-system"));
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
        AgentSessionManager manager = session.CreateManager();
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
            Assert.That(
                effects.Value.Effects.Single(effect => effect.Name == "PixelSortEffect").Notes,
                Has.Some.Contains("SwiftShader"),
                "PixelSort should advertise the SwiftShader software fallback, not a hard GPU requirement.");
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
            Assert.That(shaderRecipe.Value.Recipe.Notes.Any(note =>
                note.Contains("Requires shader source", StringComparison.OrdinalIgnoreCase)
                || note.Contains("validate_shader", StringComparison.OrdinalIgnoreCase)), Is.False);
        });
    }

    [Test]
    public void Validate_shader_reports_compile_status()
    {
        var tools = new QueryTools(new AgentSessionManager());
        const string validSksl = "half4 main(float2 fragCoord) { return half4(1); }";

        ToolResult<ValidateShaderResponse> ok = tools.ValidateShader("SKSLScriptEffect", validSksl);
        ToolResult<ValidateShaderResponse> broken = tools.ValidateShader("SKSLScriptEffect", "half4 main(float2 p) { return notAColor; }");
        ToolResult<ValidateShaderResponse> unknown = tools.ValidateShader("NotARealEffect", validSksl);
        ToolResult<ValidateShaderResponse> notScript = tools.ValidateShader("Blur", validSksl);

        Assert.Multiple(() =>
        {
            Assert.That(ok.IsSuccess, Is.True, ok.Error?.Message);
            Assert.That(ok.Value!.Status, Is.EqualTo("compiled"));
            Assert.That(ok.Value.Error, Is.Null);

            Assert.That(broken.Value!.Status, Is.EqualTo("failed"));
            Assert.That(broken.Value.Error, Is.Not.Null.And.Not.Empty);

            Assert.That(unknown.Value!.Status, Is.EqualTo("unknown_type"));
            Assert.That(notScript.Value!.Status, Is.EqualTo("not_script_effect"));
        });
    }

    [Test]
    public void Validate_shader_checks_glsl_against_the_effect_pass()
    {
        var tools = new QueryTools(new AgentSessionManager());
        string validGlsl = new GLSLScriptEffect().FragmentShader.Value;
        string unboundSampler = validGlsl
            .Replace("void main() {", "layout(set = 0, binding = 1) uniform sampler2D mask;\n\nvoid main() {")
            .Replace("outColor = c;", "outColor = c * texture(mask, fragCoord);");
        Assert.That(unboundSampler, Does.Contain("texture(mask, fragCoord)"));

        ToolResult<ValidateShaderResponse> ok = tools.ValidateShader("GLSLScriptEffect", validGlsl);
        ToolResult<ValidateShaderResponse> unbound = tools.ValidateShader("GLSLScriptEffect", unboundSampler);

        Assert.Multiple(() =>
        {
            Assert.That(ok.IsSuccess, Is.True, ok.Error?.Message);
            Assert.That(ok.Value!.Status, Is.EqualTo("compiled"));
            Assert.That(unbound.Value!.Status, Is.EqualTo("failed"),
                "A script the effect cannot bind must not report compiled, even with no graphics context.");
            Assert.That(unbound.Value.Error, Is.Not.Null.And.Not.Empty);
        });
    }

    [Test]
    public void Composition_tools_reuse_session_seed_when_seed_is_omitted()
    {
        var scene = new Scene(1920, 1080, "Scene");
        using var session = new AgentToolkitTestSession(scene);
        AgentSessionManager manager = session.CreateManager();
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
        });
    }

    [Test]
    public void Composition_plan_survives_volatile_live_session_ids()
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
        });
    }

    [Test]
    public void Read_document_returns_full_document_and_scoped_subtree()
    {
        var scene = new Scene(1920, 1080, "Scene");
        string dir = TestWorkspace.CreateDirectory();
        scene.Uri = new Uri(Path.Combine(dir, "Scene.scene"));
        var element = new Element
        {
            Start = { Value = MediaTime.FromSeconds(1) },
            Length = { Value = MediaTime.FromSeconds(2) }
        };
        element.AddObject(new TextBlock { Text = { Value = "Scoped" } });
        element.Uri = new Uri(Path.Combine(TestContext.CurrentContext.WorkDirectory, $"{Guid.NewGuid():N}.belm"));
        scene.Children.Add(element);
        scene.Children.Add(new Element
        {
            Name = { Value = "sibling" },
            Length = { Value = MediaTime.FromSeconds(1) },
            Uri = new Uri(Path.Combine(TestContext.CurrentContext.WorkDirectory, $"{Guid.NewGuid():N}.belm"))
        });

        using var session = new AgentToolkitTestSession(scene);
        AgentSessionManager manager = session.CreateManager();
        var tools = new QueryTools(manager);

        var full = tools.ReadDocument();
        var scoped = tools.ReadDocument(element.Id.Value.ToString());

        Assert.Multiple(() =>
        {
            Assert.That(full.IsSuccess, Is.True);
            Assert.That(full.Value!.Document["Elements"]!.AsArray(), Has.Count.EqualTo(2));
            Assert.That(scoped.IsSuccess, Is.True);
            Assert.That(scoped.Value!.Document["Id"]!.GetValue<string>(), Is.EqualTo(element.Id.Value.ToString()));
            Assert.That(scoped.Value.Document.ToJsonString(), Does.Not.Contain("sibling"));
            Assert.That(scoped.Value.SchemaVersion, Is.EqualTo(SchemaVersion.Current));
        });
    }

    [Test]
    public void Read_document_unknown_root_id_returns_stale_handle()
    {
        using var session = new AgentToolkitTestSession(new Scene());
        AgentSessionManager manager = session.CreateManager();
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
            Duration = { Value = MediaTime.FromSeconds(8) },
            Uri = new Uri(Path.Combine(TestContext.CurrentContext.WorkDirectory, $"{Guid.NewGuid():N}.scene"))
        };
        var element = new Element
        {
            Name = { Value = "Title element" },
            Start = { Value = MediaTime.FromSeconds(1) },
            Length = { Value = MediaTime.FromSeconds(4) },
            ZIndex = { Value = 3 },
            Uri = new Uri(Path.Combine(TestContext.CurrentContext.WorkDirectory, $"{Guid.NewGuid():N}.belm"))
        };
        var text = new TextBlock
        {
            Name = { Value = "Title" },
            Text = { Value = "Summary" },
            Fill = { Value = new SolidColorBrush(Colors.White) },
            FilterEffect = { Value = new FilterEffectGroup { Children = { new Blur() } } },
            Transform =
            {
                Value = new TransformGroup
                {
                    Children =
                    {
                        new TranslateTransform(20, 30),
                        new RotationTransform(12)
                    }
                }
            }
        };
        text.Opacity.SetKeyFrameAnimation(new KeyFrameAnimation<float>());
        ((TranslateTransform)((TransformGroup)text.Transform.Value!).Children[0]).X.SetKeyFrameAnimation(CreateFloatAnimation());
        ((RotationTransform)((TransformGroup)text.Transform.Value!).Children[1]).Rotation.SetKeyFrameAnimation(CreateFloatAnimation());
        element.AddObject(text);
        scene.Children.Add(element);

        using var session = new AgentToolkitTestSession(scene);
        AgentSessionManager manager = session.CreateManager();
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
    public void Measure_object_bounds_uses_scene_relative_time_on_trimmed_scenes()
    {
        var scene = new Scene(1920, 1080, "Measure trimmed")
        {
            Start = { Value = MediaTime.FromSeconds(30) },
            Duration = { Value = MediaTime.FromSeconds(5) },
            Uri = new Uri(Path.Combine(TestContext.CurrentContext.WorkDirectory, $"{Guid.NewGuid():N}.scene"))
        };
        var element = new Element
        {
            Name = { Value = "Clip" },
            Start = { Value = MediaTime.FromSeconds(32) },
            Length = { Value = MediaTime.FromSeconds(2) },
            Uri = new Uri(Path.Combine(TestContext.CurrentContext.WorkDirectory, $"{Guid.NewGuid():N}.belm"))
        };
        var plate = new RoundedRectShape
        {
            Name = { Value = "Plate" },
            Width = { Value = 200 },
            Height = { Value = 80 }
        };
        element.AddObject(plate);
        scene.Children.Add(element);

        using var session = new AgentToolkitTestSession(scene);
        AgentSessionManager manager = session.CreateManager();
        var tools = new QueryTools(manager);

        // Scene-relative 3s = absolute 33s (inside the element); relative 1s = absolute 31s (before it).
        ToolResult<ObjectBoundsMeasurementResponse> during = tools.MeasureObjectBounds(timeSeconds: 3);
        ToolResult<ObjectBoundsMeasurementResponse> before = tools.MeasureObjectBounds(timeSeconds: 1);

        Assert.Multiple(() =>
        {
            Assert.That(during.IsSuccess, Is.True, during.Error?.Message);
            Assert.That(during.Value!.Objects, Has.Count.EqualTo(1));
            Assert.That(before.IsSuccess, Is.True, before.Error?.Message);
            Assert.That(before.Value!.Objects, Is.Empty);
        });
    }

    [Test]
    public void Measure_object_bounds_reports_center_aligned_scene_bounds()
    {
        var scene = new Scene(1920, 1080, "Measure")
        {
            Duration = { Value = MediaTime.FromSeconds(5) },
            Uri = new Uri(Path.Combine(TestContext.CurrentContext.WorkDirectory, $"{Guid.NewGuid():N}.scene"))
        };
        var titleElement = new Element
        {
            Name = { Value = "Title element" },
            Length = { Value = MediaTime.FromSeconds(5) },
            ZIndex = { Value = 2 },
            Uri = new Uri(Path.Combine(TestContext.CurrentContext.WorkDirectory, $"{Guid.NewGuid():N}.belm"))
        };
        var title = new TextBlock
        {
            Name = { Value = "Centered title" },
            Text = { Value = "Center" },
            Size = { Value = 100 }
        };
        titleElement.AddObject(title);
        scene.Children.Add(titleElement);

        var plateElement = new Element
        {
            Name = { Value = "Plate element" },
            Length = { Value = MediaTime.FromSeconds(5) },
            ZIndex = { Value = 1 },
            Uri = new Uri(Path.Combine(TestContext.CurrentContext.WorkDirectory, $"{Guid.NewGuid():N}.belm"))
        };
        var plate = new RoundedRectShape
        {
            Name = { Value = "Offset plate" },
            Width = { Value = 200 },
            Height = { Value = 80 },
            Transform = { Value = new TranslateTransform(120, -40) }
        };
        plateElement.AddObject(plate);
        scene.Children.Add(plateElement);

        using var session = new AgentToolkitTestSession(scene);
        AgentSessionManager manager = session.CreateManager();
        var tools = new QueryTools(manager);

        ToolResult<ObjectBoundsMeasurementResponse> all = tools.MeasureObjectBounds(timeSeconds: 0);
        ToolResult<ObjectBoundsMeasurementResponse> plateOnly = tools.MeasureObjectBounds(plate.Id.Value.ToString(), timeSeconds: 0);
        ToolResult<ObjectBoundsMeasurementResponse> late = tools.MeasureObjectBounds(timeSeconds: 6);
        ObjectBoundsMeasurement titleBounds = all.Value!.Objects.Single(item => item.ObjectId == title.Id.Value.ToString());
        ObjectBoundsMeasurement plateBounds = all.Value.Objects.Single(item => item.ObjectId == plate.Id.Value.ToString());

        Assert.Multiple(() =>
        {
            Assert.That(all.IsSuccess, Is.True, all.Error?.Message);
            Assert.That(all.Value!.Objects, Has.Count.EqualTo(2));
            Assert.That(all.Value.FrameCenter.X, Is.EqualTo(960));
            Assert.That(all.Value.FrameCenter.Y, Is.EqualTo(540));
            Assert.That(all.Value.TimeFiltered, Is.True);
            Assert.That(titleBounds.MeasurementKind, Is.EqualTo("render-node-query-bounds"));
            Assert.That(titleBounds.LocalBounds.Width, Is.GreaterThan(0));
            Assert.That(titleBounds.LocalBounds.Height, Is.GreaterThan(0));
            Assert.That(titleBounds.TransformedBounds.Left, Is.LessThan(960));
            Assert.That(titleBounds.TransformedBounds.Right, Is.GreaterThan(960));
            Assert.That(titleBounds.TransformedBounds.Top, Is.LessThan(540));
            Assert.That(titleBounds.TransformedBounds.Bottom, Is.GreaterThan(540));
            Assert.That(plateBounds.MeasurementKind, Is.EqualTo("render-node-query-bounds"));
            Assert.That(plateBounds.LocalBounds.Width, Is.EqualTo(200).Within(0.01));
            Assert.That(plateBounds.LocalBounds.Height, Is.EqualTo(80).Within(0.01));
            Assert.That(plateBounds.UserTranslate!.X, Is.EqualTo(120).Within(0.01));
            Assert.That(plateBounds.UserTranslate.Y, Is.EqualTo(-40).Within(0.01));
            Assert.That(plateBounds.TransformedBounds.Left, Is.EqualTo(980).Within(0.01));
            Assert.That(plateBounds.TransformedBounds.Top, Is.EqualTo(460).Within(0.01));
            Assert.That(plateBounds.Center.X, Is.EqualTo(1080).Within(0.01));
            Assert.That(plateBounds.Center.Y, Is.EqualTo(500).Within(0.01));
            Assert.That(plateOnly.IsSuccess, Is.True, plateOnly.Error?.Message);
            Assert.That(plateOnly.Value!.Objects.Single().ObjectId, Is.EqualTo(plate.Id.Value.ToString()));
            Assert.That(late.IsSuccess, Is.True, late.Error?.Message);
            Assert.That(late.Value!.Objects, Is.Empty);
        });
    }

    [Test]
    public void Measure_object_bounds_includes_filter_effect_render_node_bounds()
    {
        var scene = new Scene(200, 200, "Measure effect")
        {
            Duration = { Value = MediaTime.FromSeconds(5) },
            Uri = new Uri(Path.Combine(TestContext.CurrentContext.WorkDirectory, $"{Guid.NewGuid():N}.scene"))
        };
        var element = new Element
        {
            Name = { Value = "Plate element" },
            Length = { Value = MediaTime.FromSeconds(5) },
            Uri = new Uri(Path.Combine(TestContext.CurrentContext.WorkDirectory, $"{Guid.NewGuid():N}.belm"))
        };
        var plate = new RoundedRectShape
        {
            Name = { Value = "Shadowed plate" },
            Width = { Value = 100 },
            Height = { Value = 50 },
            FilterEffect =
            {
                Value = new DropShadow
                {
                    Position = { Value = new Point(10, 5) },
                    Sigma = { Value = new Size(3, 4) },
                    Color = { Value = Colors.Black }
                }
            }
        };
        element.AddObject(plate);
        scene.Children.Add(element);

        using var session = new AgentToolkitTestSession(scene);
        AgentSessionManager manager = session.CreateManager();
        var tools = new QueryTools(manager);

        ToolResult<ObjectBoundsMeasurementResponse> result = tools.MeasureObjectBounds(plate.Id.Value.ToString(), timeSeconds: 0);
        ObjectBoundsMeasurement bounds = result.Value!.Objects.Single();

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
            Assert.That(bounds.MeasurementKind, Is.EqualTo("render-node-query-bounds"));
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
    public void Measure_object_bounds_measures_a_node_graph_whose_measure_node_needs_a_target_domain()
    {
        var scene = new Scene(320, 180, "graph-measure-node")
        {
            Duration = { Value = MediaTime.FromSeconds(2) },
            Uri = new Uri(Path.Combine(TestContext.CurrentContext.WorkDirectory, $"{Guid.NewGuid():N}.scene"))
        };
        NodeGraphDrawable drawable = BuildMeasureNodeGraphDrawable();
        var element = new Element
        {
            Name = { Value = "Graph element" },
            Length = { Value = MediaTime.FromSeconds(2) },
            Uri = new Uri(Path.Combine(TestContext.CurrentContext.WorkDirectory, $"{Guid.NewGuid():N}.belm"))
        };
        element.AddObject(drawable);
        scene.Children.Add(element);

        using var session = new AgentToolkitTestSession(scene);
        AgentSessionManager manager = session.CreateManager();
        var tools = new QueryTools(manager);

        ToolResult<ObjectBoundsMeasurementResponse> result = tools.MeasureObjectBounds(
            objectId: drawable.Id.Value.ToString(),
            timeSeconds: 0.0);

        Assert.That(result.IsSuccess, Is.True, result.Error?.Message);
        ObjectBoundsMeasurement measured = result.Value!.Objects.Single();
        Assert.Multiple(() =>
        {
            Assert.That(measured.MeasurementKind, Is.EqualTo("render-node-query-bounds"));

            // The effect extent is symbolic, so it resolves to the owning target domain (the whole frame);
            // that is the correct result, not a fallback.
            Assert.That(measured.TransformedBounds.Left, Is.EqualTo(0).Within(0.5));
            Assert.That(measured.TransformedBounds.Top, Is.EqualTo(0).Within(0.5));
            Assert.That(measured.TransformedBounds.Width, Is.EqualTo(320).Within(0.5));
            Assert.That(measured.TransformedBounds.Height, Is.EqualTo(180).Within(0.5));
        });
    }

    // GeometryShape -> ShakeEffect -> Output, with a MeasureNode reading the same effect output.
    // ShakeEffect records a CustomEffect with no transformBounds, so the effect segment's extent stays
    // symbolic and only an owning target domain can resolve it.
    private static NodeGraphDrawable BuildMeasureNodeGraphDrawable()
    {
        var drawable = new NodeGraphDrawable { Name = { Value = "Graph" } };
        GraphModel model = drawable.Model.Value!;

        var geometryNode = new RectGeometryNode();
        geometryNode.Object.Value.Width.Value = 40;
        geometryNode.Object.Value.Height.Value = 20;
        var shapeNode = new GeometryShapeNode();
        SetUnconnectedInput(shapeNode.Fill, Brushes.White);
        var shakeNode = new FilterEffectNode<ShakeEffect>();
        var outputNode = new OutputNode();
        var measureNode = new MeasureNode();
        model.Nodes.Add(geometryNode);
        model.Nodes.Add(shapeNode);
        model.Nodes.Add(shakeNode);
        model.Nodes.Add(outputNode);
        model.Nodes.Add(measureNode);

        // ConfigureNode keeps its render-chain ports unexposed: Items[0] = Output, Items[1] = list Input.
        var shakeChainInput = (IInputPort)shakeNode.Items[1];
        var shakeChainOutput = (IOutputPort)shakeNode.Items[0];
        model.Connect(shapeNode.Geometry, geometryNode.OutputPort);
        model.Connect(shakeChainInput, shapeNode.Output);
        model.Connect(outputNode.InputPort, shakeChainOutput);
        model.Connect(measureNode.Input, shakeChainOutput);
        return drawable;
    }

    private static void SetUnconnectedInput<T>(InputPort<T> port, T value)
    {
        ((DefaultInputPort<T>)port).GetProperty()!.SetValue(value);
    }

    [Test]
    public void Read_document_summary_exposes_fallback_objects()
    {
        var scene = new Scene(1280, 720, "Fallback summary")
        {
            Duration = { Value = MediaTime.FromSeconds(2) },
            Uri = new Uri(Path.Combine(TestContext.CurrentContext.WorkDirectory, $"{Guid.NewGuid():N}.scene"))
        };
        var element = new Element
        {
            Name = { Value = "Fallback element" },
            Length = { Value = MediaTime.FromSeconds(2) },
            Uri = new Uri(Path.Combine(TestContext.CurrentContext.WorkDirectory, $"{Guid.NewGuid():N}.belm"))
        };
        var fallback = new FallbackEngineObject
        {
            Name = { Value = "Fallback rect" },
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
        AgentSessionManager manager = session.CreateManager();
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

        public void Invoke(Action action) => action();
    }

    private static KeyFrameAnimation<float> CreateFloatAnimation()
    {
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(
            new KeyFrame<float>
            {
                KeyTime = { Value = MediaTime.Zero },
                Value = { Value = 0 },
                Easing = { Value = new LinearEasing() }
            },
            out _);
        animation.KeyFrames.Add(
            new KeyFrame<float>
            {
                KeyTime = { Value = MediaTime.FromSeconds(1) },
                Value = { Value = 1 },
                Easing = { Value = new SineEaseOut() }
            },
            out _);
        return animation;
    }
}
