using System.Text.Json;
using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Documents;
using Beutl.AgentToolkit.Rendering;
using Beutl.AgentToolkit.Schema;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Tests.Helpers;
using Beutl.AgentToolkit.Tools;
using Beutl.AgentToolkit.Workspace;
using Beutl.Animation;
using Beutl.Editor;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.AgentToolkit.Tests.Tools;

public sealed class SessionToolsTests
{
    [Test]
    public async Task Open_project_warns_about_corrupt_element_and_render_still_remains_available()
    {
        string root = CreateWorkspace();
        string projectPath = Path.Combine(root, "corrupt-element.bep");
        Project project = ProjectOperations.CreateProject(new ProjectCreateOptions(
            projectPath,
            64,
            64,
            30,
            TimeSpan.FromSeconds(1)));
        Scene scene = project.Items.OfType<Scene>().Single();
        var element = new Element
        {
            Name = "Corrupt element",
            Length = TimeSpan.FromSeconds(1),
            Uri = new Uri(Path.Combine(
                Path.GetDirectoryName(scene.Uri!.LocalPath)!,
                "corrupt-element.belm"))
        };
        element.AddObject(new RectShape
        {
            Width = { CurrentValue = 32 },
            Height = { CurrentValue = 32 },
            Fill = { CurrentValue = Brushes.White }
        });
        scene.Children.Add(element);
        ProjectOperations.Save(project);

        string elementPath = element.Uri!.LocalPath;
        string elementRelativePath = Path.GetRelativePath(
            Path.GetDirectoryName(scene.Uri!.LocalPath)!,
            elementPath).Replace('\\', '/');
        JsonObject elementJson = JsonNode.Parse(File.ReadAllText(elementPath))!.AsObject();
        JsonObject drawableJson = elementJson[nameof(Element.Objects)]!.AsArray()[0]!.AsObject();
        drawableJson[nameof(RectShape.Width)] = "not-a-number";
        File.WriteAllText(elementPath, elementJson.ToJsonString());

        var manager = new AgentSessionManager();
        using var source = new FileSessionSource();
        SessionTools sessionTools = CreateSessionTools(source, manager, root);

        ToolResult<OpenProjectResponse> opened = await sessionTools.OpenProject(projectPath);
        Assert.That(opened.IsSuccess, Is.True, opened.Error?.Message);
        Assert.That(opened.Value, Is.Not.Null, opened.Error?.Message);
        JsonObject responseJson = JsonSerializer.SerializeToNode(opened.Value)!.AsObject();

        var stillRenderer = new StillRenderer();
        var motionAnalyzer = new MotionVariationAnalyzer(stillRenderer);
        var renderTools = new RenderTools(
            manager,
            new WorkspaceGuard(root),
            new DestructiveGuard(),
            stillRenderer,
            new StoryboardRenderer(),
            motionAnalyzer,
            new AudioRhythmAnalyzer(),
            new QualityAnalyzer(motionAnalyzer, stillRenderer),
            new VideoExporter(new EncoderRegistration()),
            new RenderJobManager());
        string outputPath = Path.Combine(root, "corrupt-element.png");
        var rendered = await renderTools.RenderStill(
            outputPath,
            cancellationToken: CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(opened.IsSuccess, Is.True, opened.Error?.Message);
            Assert.That(
                responseJson["Warnings"]?.AsArray().Select(static item => item!.GetValue<string>()),
                Has.Some.Contains(elementRelativePath).And.Some.Contains("could not be converted"));
            Assert.That(rendered.IsError, Is.Not.True);
            Assert.That(File.Exists(outputPath), Is.True);
        });
    }

    [Test]
    public async Task Open_project_reports_fallback_and_lossy_easing_incidents_together()
    {
        const string MissingType = "[Beutl.Engine]Beutl.Engine:MissingAnimatedValue";
        string root = CreateWorkspace();
        string projectPath = Path.Combine(root, "animation-fallback.bep");
        Project project = ProjectOperations.CreateProject(new ProjectCreateOptions(
            projectPath,
            64,
            64,
            30,
            TimeSpan.FromSeconds(1)));
        Scene scene = project.Items.OfType<Scene>().Single();
        var holder = new AnimatedValueHolder();
        var animation = new KeyFrameAnimation<EngineObject?>();
        animation.KeyFrames.Add(new KeyFrame<EngineObject?>
        {
            KeyTime = TimeSpan.Zero,
            Value = new RectShape(),
        }, out _);
        holder.AnimatedValue.Animation = animation;
        var element = new Element
        {
            Name = "Animated fallback",
            Length = TimeSpan.FromSeconds(1),
            Uri = new Uri(Path.Combine(
                Path.GetDirectoryName(scene.Uri!.LocalPath)!,
                "animation-fallback.belm")),
        };
        element.AddObject(holder);
        scene.Children.Add(element);
        ProjectOperations.Save(project);

        string elementPath = element.Uri!.LocalPath;
        string elementRelativePath = Path.GetRelativePath(
            Path.GetDirectoryName(scene.Uri!.LocalPath)!,
            elementPath).Replace('\\', '/');
        JsonObject elementJson = JsonNode.Parse(File.ReadAllText(elementPath))!.AsObject();
        JsonObject objectJson = elementJson[nameof(Element.Objects)]!.AsArray()[0]!.AsObject();
        JsonObject animationJson = objectJson["Animations"]![nameof(AnimatedValueHolder.AnimatedValue)]!.AsObject();
        JsonObject keyFrameJson = animationJson[nameof(KeyFrameAnimation.KeyFrames)]!.AsArray()[0]!.AsObject();
        keyFrameJson[nameof(IKeyFrame.Value)]!.AsObject()["$type"] = MissingType;
        keyFrameJson[nameof(KeyFrame.Easing)] = "[Missing.Assembly]Missing.Namespace:MissingEasing";
        File.WriteAllText(elementPath, elementJson.ToJsonString());

        var manager = new AgentSessionManager();
        using var source = new FileSessionSource();
        SessionTools sessionTools = CreateSessionTools(source, manager, root);

        ToolResult<OpenProjectResponse> opened = await sessionTools.OpenProject(projectPath);

        Assert.Multiple(() =>
        {
            Assert.That(opened.IsSuccess, Is.True, opened.Error?.Message);
            Assert.That(
                opened.Value!.Warnings,
                Has.Some.Contains(elementRelativePath).And.Some.Contains(nameof(FallbackReason.TypeNotFound)));
            Assert.That(opened.Value.Warnings,
                Has.Some.Contains(elementRelativePath).And.Some.Contains("replaced during load"));
            Assert.That(opened.Value.RecoveryIncidents, Has.Count.EqualTo(2));
            Assert.That(opened.Value.RecoveryIncidents.Select(static incident => incident.ElementFile),
                Is.All.EqualTo(elementRelativePath));
            Assert.That(opened.Value.RecoveryIncidents,
                Has.One.Matches<RecoveryIncident>(incident =>
                    incident.Reason == nameof(FallbackReason.TypeNotFound)
                    && incident.TypeName == MissingType
                    && incident.Message is null));
            Assert.That(opened.Value.RecoveryIncidents,
                Has.One.Matches<RecoveryIncident>(incident =>
                    incident.Reason == nameof(FallbackReason.DeserializationFailed)
                    && incident.TypeName is null
                    && incident.Message != null
                    && incident.Message.Contains("value was replaced during load", StringComparison.Ordinal)));
        });
    }

    [Test]
    public async Task Open_project_warns_about_unresolvable_keyframe_easing_without_fallback()
    {
        string root = CreateWorkspace();
        string projectPath = Path.Combine(root, "easing-replacement.bep");
        Project project = ProjectOperations.CreateProject(new ProjectCreateOptions(
            projectPath,
            64,
            64,
            30,
            TimeSpan.FromSeconds(1)));
        Scene scene = project.Items.OfType<Scene>().Single();
        var shape = new RectShape();
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float>
        {
            KeyTime = TimeSpan.Zero,
            Value = 32,
        });
        shape.Width.Animation = animation;
        var element = new Element
        {
            Name = "Easing replacement",
            Length = TimeSpan.FromSeconds(1),
            Uri = new Uri(Path.Combine(
                Path.GetDirectoryName(scene.Uri!.LocalPath)!,
                "easing-replacement.belm")),
        };
        element.AddObject(shape);
        scene.Children.Add(element);
        ProjectOperations.Save(project);

        string elementPath = element.Uri!.LocalPath;
        string elementRelativePath = Path.GetRelativePath(
            Path.GetDirectoryName(scene.Uri!.LocalPath)!,
            elementPath).Replace('\\', '/');
        JsonObject elementJson = JsonNode.Parse(File.ReadAllText(elementPath))!.AsObject();
        JsonObject objectJson = elementJson[nameof(Element.Objects)]!.AsArray()[0]!.AsObject();
        JsonObject animationJson = objectJson["Animations"]![nameof(RectShape.Width)]!.AsObject();
        JsonObject keyFrameJson = animationJson[nameof(KeyFrameAnimation.KeyFrames)]!.AsArray()[0]!.AsObject();
        keyFrameJson[nameof(KeyFrame.Easing)] = "[Missing.Assembly]Missing.Namespace:MissingEasing";
        File.WriteAllText(elementPath, elementJson.ToJsonString());

        var manager = new AgentSessionManager();
        using var source = new FileSessionSource();
        SessionTools sessionTools = CreateSessionTools(source, manager, root);

        ToolResult<OpenProjectResponse> opened = await sessionTools.OpenProject(projectPath);

        Assert.Multiple(() =>
        {
            Assert.That(opened.IsSuccess, Is.True, opened.Error?.Message);
            Assert.That(
                opened.Value!.Warnings,
                Has.Some.Contains(elementRelativePath).And.Some.Contains("replaced during load"));
            Assert.That(opened.Value.RecoveryIncidents, Has.Count.EqualTo(1));
            Assert.That(opened.Value.RecoveryIncidents[0].ElementFile, Is.EqualTo(elementRelativePath));
            Assert.That(opened.Value.RecoveryIncidents[0].Reason,
                Is.EqualTo(nameof(FallbackReason.DeserializationFailed)));
            Assert.That(opened.Value.RecoveryIncidents[0].TypeName, Is.Null);
            Assert.That(opened.Value.RecoveryIncidents[0].Message,
                Does.Contain("value was replaced during load").And.Contain("original element file is preserved"));
        });
    }

    [Test]
    public async Task Open_project_warns_about_malformed_element_json_and_keeps_healthy_elements()
    {
        string root = CreateWorkspace();
        string projectPath = Path.Combine(root, "malformed-element.bep");
        Project project = ProjectOperations.CreateProject(new ProjectCreateOptions(
            projectPath,
            64,
            64,
            30,
            TimeSpan.FromSeconds(1)));
        Scene scene = project.Items.OfType<Scene>().Single();
        string sceneDirectory = Path.GetDirectoryName(scene.Uri!.LocalPath)!;
        var healthy = new Element
        {
            Name = "Healthy element",
            Length = TimeSpan.FromSeconds(1),
            Uri = new Uri(Path.Combine(sceneDirectory, "healthy-element.belm")),
        };
        healthy.AddObject(new RectShape
        {
            Width = { CurrentValue = 32 },
            Height = { CurrentValue = 32 },
            Fill = { CurrentValue = Brushes.White },
        });
        var malformed = new Element
        {
            Name = "Malformed element",
            Length = TimeSpan.FromSeconds(1),
            Uri = new Uri(Path.Combine(sceneDirectory, "malformed-element.belm")),
        };
        malformed.AddObject(new RectShape
        {
            Width = { CurrentValue = 16 },
            Height = { CurrentValue = 16 },
            Fill = { CurrentValue = Brushes.Red },
        });
        scene.Children.Add(healthy);
        scene.Children.Add(malformed);
        ProjectOperations.Save(project);
        File.WriteAllText(malformed.Uri!.LocalPath, "{ this is not valid JSON");
        string malformedRelativePath = Path.GetRelativePath(
            sceneDirectory,
            malformed.Uri.LocalPath).Replace('\\', '/');

        var manager = new AgentSessionManager();
        using var source = new FileSessionSource();
        SessionTools sessionTools = CreateSessionTools(source, manager, root);

        ToolResult<OpenProjectResponse> opened = await sessionTools.OpenProject(projectPath);
        Assert.That(opened.IsSuccess, Is.True, opened.Error?.Message);
        JsonObject responseJson = JsonSerializer.SerializeToNode(opened.Value)!.AsObject();

        var stillRenderer = new StillRenderer();
        var motionAnalyzer = new MotionVariationAnalyzer(stillRenderer);
        var renderTools = new RenderTools(
            manager,
            new WorkspaceGuard(root),
            new DestructiveGuard(),
            stillRenderer,
            new StoryboardRenderer(),
            motionAnalyzer,
            new AudioRhythmAnalyzer(),
            new QualityAnalyzer(motionAnalyzer, stillRenderer),
            new VideoExporter(new EncoderRegistration()),
            new RenderJobManager());
        string outputPath = Path.Combine(root, "malformed-element.png");
        var rendered = await renderTools.RenderStill(
            outputPath,
            cancellationToken: CancellationToken.None);
        var recoveredFallback = (IFallback)((Scene)manager.CurrentSession!.Root)
            .Children.Single(item => item.Uri!.LocalPath == malformed.Uri.LocalPath)
            .Objects.Single();

        Assert.Multiple(() =>
        {
            Assert.That(opened.Value!.Summary.Scenes.Single().Elements, Is.EqualTo(2));
            Assert.That(
                responseJson["Warnings"]?.AsArray().Select(static item => item!.GetValue<string>()),
                Has.Some.Contains(malformedRelativePath)
                    .And.Some.Contains("JsonReaderException")
                    .And.Some.Contains("invalid start"));
            Assert.That(opened.Value.RecoveryIncidents, Has.Count.EqualTo(1));
            Assert.That(opened.Value.RecoveryIncidents[0].ElementFile, Is.EqualTo(malformedRelativePath));
            Assert.That(opened.Value.RecoveryIncidents[0].Reason,
                Is.EqualTo(nameof(FallbackReason.DeserializationFailed)));
            Assert.That(opened.Value.RecoveryIncidents[0].TypeName, Is.Null);
            Assert.That(opened.Value.RecoveryIncidents[0].Message, Is.EqualTo(recoveredFallback.ErrorMessage));
            Assert.That(rendered.IsError, Is.Not.True);
            Assert.That(File.Exists(outputPath), Is.True);
        });
    }

    [Test]
    public async Task Open_project_warning_paths_distinguish_same_named_sidecars_in_different_directories()
    {
        string root = CreateWorkspace();
        string projectPath = Path.Combine(root, "duplicate-sidecar-names.bep");
        Project project = ProjectOperations.CreateProject(new ProjectCreateOptions(
            projectPath,
            64,
            64,
            30,
            TimeSpan.FromSeconds(1)));
        Scene scene = project.Items.OfType<Scene>().Single();
        string sceneDirectory = Path.GetDirectoryName(scene.Uri!.LocalPath)!;
        string firstPath = Path.Combine(sceneDirectory, "first", "clip.belm");
        string secondPath = Path.Combine(sceneDirectory, "second", "clip.belm");
        scene.Children.Add(new Element
        {
            Name = "First clip",
            Length = TimeSpan.FromSeconds(1),
            Uri = new Uri(firstPath),
        });
        scene.Children.Add(new Element
        {
            Name = "Second clip",
            Length = TimeSpan.FromSeconds(1),
            Uri = new Uri(secondPath),
        });
        ProjectOperations.Save(project);
        File.WriteAllText(firstPath, "{ this is not valid JSON");
        File.WriteAllText(secondPath, "{ this is not valid JSON");

        var manager = new AgentSessionManager();
        using var source = new FileSessionSource();
        SessionTools sessionTools = CreateSessionTools(source, manager, root);

        ToolResult<OpenProjectResponse> opened = await sessionTools.OpenProject(projectPath);

        Assert.Multiple(() =>
        {
            Assert.That(opened.IsSuccess, Is.True, opened.Error?.Message);
            Assert.That(opened.Value!.Warnings, Has.Some.Contains("first/clip.belm"));
            Assert.That(opened.Value.Warnings, Has.Some.Contains("second/clip.belm"));
            Assert.That(opened.Value.RecoveryIncidents.Select(static item => item.ElementFile),
                Is.EquivalentTo(new[] { "first/clip.belm", "second/clip.belm" }));
        });
    }

    [Test]
    public async Task Open_project_incidents_distinguish_same_named_sidecars_across_scenes_and_keep_top_level_type()
    {
        const string MissingType = "[Missing.Assembly]Missing.Namespace:MissingElement";
        string root = CreateWorkspace();
        string projectPath = Path.Combine(root, "duplicate-sidecars-across-scenes.bep");
        Project project = ProjectOperations.CreateProject(new ProjectCreateOptions(
            projectPath,
            64,
            64,
            30,
            TimeSpan.FromSeconds(1),
            Name: "First scene"));
        Scene firstScene = project.Items.OfType<Scene>().Single();
        Scene secondScene = ProjectOperations.AddScene(project, new SceneCreateOptions(
            64,
            64,
            TimeSpan.Zero,
            TimeSpan.FromSeconds(1),
            Name: "Second scene"));
        Scene[] scenes = [firstScene, secondScene];
        foreach (Scene scene in scenes)
        {
            scene.Children.Add(new Element
            {
                Name = "Clip",
                Length = TimeSpan.FromSeconds(1),
                Uri = new Uri(Path.Combine(Path.GetDirectoryName(scene.Uri!.LocalPath)!, "clip.belm")),
            });
        }

        ProjectOperations.Save(project);
        foreach (Scene scene in scenes)
        {
            Element element = scene.Children.Single();
            File.WriteAllText(
                element.Uri!.LocalPath,
                $$"""{"$type":"{{MissingType}}","Id":"{{element.Id}}","Name":"Clip"}""");
        }

        var manager = new AgentSessionManager();
        using var source = new FileSessionSource();
        SessionTools sessionTools = CreateSessionTools(source, manager, root);

        ToolResult<OpenProjectResponse> opened = await sessionTools.OpenProject(projectPath);

        Assert.Multiple(() =>
        {
            Assert.That(opened.IsSuccess, Is.True, opened.Error?.Message);
            Assert.That(opened.Value!.RecoveryIncidents, Has.Count.EqualTo(2));
            Assert.That(opened.Value.RecoveryIncidents.Select(static incident => incident.ElementFile),
                Is.All.EqualTo("clip.belm"));
            Assert.That(opened.Value.RecoveryIncidents.Select(static incident => incident.SceneId),
                Is.EquivalentTo(scenes.Select(static scene => scene.Id.ToString())));
            Assert.That(opened.Value.RecoveryIncidents.Select(static incident => incident.SceneName),
                Is.EquivalentTo(new[] { "First scene", "Second scene" }));
            Assert.That(opened.Value.RecoveryIncidents.Select(static incident => incident.TypeName),
                Is.All.EqualTo(MissingType));
            Assert.That(opened.Value.RecoveryIncidents.Select(static incident => incident.Reason),
                Is.All.EqualTo(nameof(FallbackReason.TypeNotFound)));
            Assert.That(opened.Value.RecoveryIncidents.Select(static incident => incident.Message),
                Is.All.Null);
        });
    }

    [Test]
    public async Task Apply_edit_can_rename_healthy_element_while_malformed_element_is_recovered()
    {
        string root = CreateWorkspace();
        RecoveredProjectFixture fixture = CreateProjectWithMalformedElement(root);
        var manager = new AgentSessionManager();
        using var source = new FileSessionSource();
        SessionTools sessionTools = CreateSessionTools(source, manager, root);
        ToolResult<OpenProjectResponse> opened = await sessionTools.OpenProject(fixture.ProjectPath);
        Assert.That(opened.IsSuccess, Is.True, opened.Error?.Message);
        var editTools = new EditTools(manager);
        JsonObject patch = new()
        {
            ["Elements"] = new JsonArray(new JsonObject
            {
                [nameof(CoreObject.Id)] = fixture.HealthyId.ToString(),
                [nameof(CoreObject.Name)] = "Renamed healthy element",
            }),
        };

        ToolResult<ApplyEditResponse> applied = editTools.ApplyEdit(
            patch: patch,
            schemaVersion: SchemaVersion.Current);
        ToolResult<SaveProjectResponse> saved = sessionTools.SaveProject(opened.Value!.Session);

        Assert.Multiple(() =>
        {
            Assert.That(applied.IsSuccess, Is.True, applied.Error?.Message);
            Assert.That(saved.IsSuccess, Is.True, saved.Error?.Message);
            Assert.That(File.ReadAllBytes(fixture.MalformedPath), Is.EqualTo(fixture.MalformedBytes));
            Assert.That(
                ((Scene)manager.CurrentSession!.Root).Children.Single(item => item.Id == fixture.HealthyId).Name,
                Is.EqualTo("Renamed healthy element"));
        });
    }

    [Test]
    public async Task Delete_recovered_element_and_save_excludes_it_without_deleting_its_sidecar()
    {
        string root = CreateWorkspace();
        RecoveredProjectFixture fixture = CreateProjectWithMalformedElement(root);
        byte[] healthyBytes = File.ReadAllBytes(fixture.HealthyPath);
        var manager = new AgentSessionManager();
        using var source = new FileSessionSource();
        SessionTools sessionTools = CreateSessionTools(source, manager, root);
        ToolResult<OpenProjectResponse> opened = await sessionTools.OpenProject(fixture.ProjectPath);
        Assert.That(opened.IsSuccess, Is.True, opened.Error?.Message);
        var editTools = new EditTools(manager);
        JsonObject patch = new()
        {
            ["Elements"] = new JsonArray(new JsonObject
            {
                [nameof(CoreObject.Id)] = fixture.MalformedId.ToString(),
                ["$delete"] = true,
            }),
        };

        ToolResult<ApplyEditResponse> deleted = editTools.ApplyEdit(
            patch: patch,
            schemaVersion: SchemaVersion.Current);
        ToolResult<SaveProjectResponse> saved = sessionTools.SaveProject(opened.Value!.Session);

        Project reopenedProject = CoreSerializer.RestoreFromUri<Project>(new Uri(fixture.ProjectPath));
        Scene reopenedScene = reopenedProject.Items.OfType<Scene>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(deleted.IsSuccess, Is.True, deleted.Error?.Message);
            Assert.That(saved.IsSuccess, Is.True, saved.Error?.Message);
            Assert.That(File.ReadAllBytes(fixture.MalformedPath), Is.EqualTo(fixture.MalformedBytes),
                "Declarative deletion excludes the recovered sidecar; it does not destroy the opaque source file.");
            Assert.That(File.ReadAllBytes(fixture.HealthyPath), Is.EqualTo(healthyBytes));
            Assert.That(reopenedScene.Children.Select(static item => item.Id), Does.Not.Contain(fixture.MalformedId));
            Assert.That(reopenedScene.Children.Select(static item => item.Id), Does.Contain(fixture.HealthyId));
        });
    }

    [Test]
    public async Task Create_project_starts_file_backed_session_for_document_tools()
    {
        string root = CreateWorkspace();
        var manager = new AgentSessionManager();
        using var source = new FileSessionSource();
        SessionTools sessionTools = CreateSessionTools(source, manager, root);
        var queryTools = new QueryTools(manager);

        ToolResult<CreateProjectResponse> created = await sessionTools.CreateProject(
            "motion.bep",
            width: 640,
            height: 360,
            frameRate: 30,
            duration: "00:00:04");
        Assert.That(created.IsSuccess, Is.True, created.Error?.Message);

        ToolResult<DocumentSummaryResponse> summary = queryTools.ReadDocumentSummary();
        ToolResult<SaveProjectResponse> saved = sessionTools.SaveProject(created.Value!.Session);

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(created.Value!.SavedPath), Is.True);
            Assert.That(created.Value.Summary.Scenes, Has.Count.EqualTo(1));
            Assert.That(summary.IsSuccess, Is.True, summary.Error?.Message);
            Assert.That(summary.Value!.Source, Is.EqualTo("File"));
            Assert.That(summary.Value.Width, Is.EqualTo(640));
            Assert.That(summary.Value.Height, Is.EqualTo(360));
            Assert.That(summary.Value.Duration, Is.EqualTo("00:00:04"));
            Assert.That(saved.IsSuccess, Is.True, saved.Error?.Message);
            Assert.That(saved.Value!.SavedPath, Is.EqualTo(created.Value.SavedPath));
        });
    }

    [Test]
    public async Task Create_project_existing_path_requires_confirmation()
    {
        string root = CreateWorkspace();
        string path = Path.Combine(root, "exists.bep");
        File.WriteAllText(path, "{}");
        using var source = new FileSessionSource();
        SessionTools sessionTools = CreateSessionTools(source, new AgentSessionManager(), root);

        ToolResult<CreateProjectResponse> result = await sessionTools.CreateProject(
            "exists.bep",
            width: 640,
            height: 360,
            frameRate: 30,
            duration: "00:00:04");

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error!.Code, Is.EqualTo(ErrorCode.DestructiveIntent));
        });
    }

    [Test]
    public async Task Create_project_does_not_overwrite_existing_default_scene_sidecar()
    {
        string root = CreateWorkspace();
        string existingDir = Path.Combine(root, "demo");
        Directory.CreateDirectory(existingDir);
        string existingSidecar = Path.Combine(existingDir, "demo.scene");
        File.WriteAllText(existingSidecar, "existing scene sidecar");
        using var source = new FileSessionSource();
        SessionTools sessionTools = CreateSessionTools(source, new AgentSessionManager(), root);

        ToolResult<CreateProjectResponse> created = await sessionTools.CreateProject(
            "demo.bep",
            width: 640,
            height: 360,
            frameRate: 30,
            duration: "00:00:04");

        Scene scene = source.CurrentFileSession!.Project.Items.OfType<Scene>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(created.IsSuccess, Is.True, created.Error?.Message);
            Assert.That(File.ReadAllText(existingSidecar), Is.EqualTo("existing scene sidecar"));
            Assert.That(scene.Uri!.LocalPath, Is.Not.EqualTo(existingSidecar));
            Assert.That(File.Exists(scene.Uri.LocalPath), Is.True);
            Assert.That(Path.GetFileName(Path.GetDirectoryName(scene.Uri.LocalPath)!), Is.EqualTo("demo-2"));
        });
    }

    [Test]
    public async Task Add_scene_adds_and_activates_the_new_scene_through_the_session_dispatcher()
    {
        string root = CreateWorkspace();
        var manager = new AgentSessionManager();
        using var source = new FileSessionSource();
        SessionTools sessionTools = CreateSessionTools(source, manager, root);

        ToolResult<CreateProjectResponse> created = await sessionTools.CreateProject(
            "motion.bep", width: 640, height: 360, frameRate: 30, duration: "00:00:04");
        Assert.That(created.IsSuccess, Is.True, created.Error?.Message);

        ToolResult<AddSceneResponse> added = await sessionTools.AddScene(
            created.Value!.Session, width: 320, height: 240, start: "00:00:00", duration: "00:00:02", name: "second");

        FileEditingSession session = source.CurrentFileSession!;
        Assert.Multiple(() =>
        {
            Assert.That(added.IsSuccess, Is.True, added.Error?.Message);
            Assert.That(session.Project.Items.OfType<Scene>().Count(), Is.EqualTo(2));
            Assert.That(session.Scene.Id.ToString(), Is.EqualTo(added.Value!.SceneId));
        });
    }

    [Test]
    public async Task Failed_create_project_save_keeps_the_previous_session_current()
    {
        string root = CreateWorkspace();
        var manager = new AgentSessionManager();
        using var source = new FileSessionSource();
        SessionTools sessionTools = CreateSessionTools(source, manager, root);

        ToolResult<CreateProjectResponse> first = await sessionTools.CreateProject(
            "first.bep", width: 640, height: 360, frameRate: 30, duration: "00:00:04");
        Assert.That(first.IsSuccess, Is.True, first.Error?.Message);
        FileEditingSession firstSession = source.CurrentFileSession!;

        // A directory occupying the target .bep path makes the initial save throw.
        Directory.CreateDirectory(Path.Combine(root, "broken.bep"));
        ToolResult<CreateProjectResponse> failed = await sessionTools.CreateProject(
            "broken.bep", width: 640, height: 360, frameRate: 30, duration: "00:00:04");

        Assert.Multiple(() =>
        {
            Assert.That(failed.IsSuccess, Is.False);
            Assert.That(source.CurrentFileSession, Is.SameAs(firstSession));
            Assert.That(manager.CurrentSession, Is.SameAs(firstSession));
        });
    }

    [Test]
    public async Task Failed_save_as_restores_the_original_project_and_scene_uris()
    {
        string root = CreateWorkspace();
        var manager = new AgentSessionManager();
        using var source = new FileSessionSource();
        SessionTools sessionTools = CreateSessionTools(source, manager, root);

        ToolResult<CreateProjectResponse> created = await sessionTools.CreateProject(
            "orig.bep", width: 640, height: 360, frameRate: 30, duration: "00:00:04");
        Assert.That(created.IsSuccess, Is.True, created.Error?.Message);

        FileEditingSession session = source.CurrentFileSession!;
        string originalProjectUri = session.Project.Uri!.LocalPath;
        string[] originalSceneUris = session.Project.Items.OfType<Scene>().Select(s => s.Uri!.LocalPath).ToArray();

        // A directory occupying the destination .bep path makes ProjectOperations.Save throw after
        // SetProjectPath has already rewritten the URIs.
        Directory.CreateDirectory(Path.Combine(root, "dest.bep"));

        ToolResult<SaveProjectResponse> failed = sessionTools.SaveProject(
            created.Value!.Session, "dest.bep", confirmOverwrite: true);

        string[] restoredSceneUris = session.Project.Items.OfType<Scene>().Select(s => s.Uri!.LocalPath).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(failed.IsSuccess, Is.False);
            Assert.That(session.Project.Uri!.LocalPath, Is.EqualTo(originalProjectUri));
            Assert.That(restoredSceneUris, Is.EqualTo(originalSceneUris));
        });
    }

    [Test]
    public async Task Create_project_rejects_a_malformed_duration_as_a_validation_error()
    {
        string root = CreateWorkspace();
        using var source = new FileSessionSource();
        SessionTools sessionTools = CreateSessionTools(source, new AgentSessionManager(), root);

        ToolResult<CreateProjectResponse> result = await sessionTools.CreateProject(
            "motion.bep",
            width: 640,
            height: 360,
            frameRate: 30,
            duration: "not-a-timespan");

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error!.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(result.Error.Target, Is.EqualTo("duration"));
        });
    }

    [TestCase("-00:00:04")]
    [TestCase("00:00:00")]
    public async Task Create_project_rejects_a_non_positive_duration(string duration)
    {
        string root = CreateWorkspace();
        using var source = new FileSessionSource();
        SessionTools sessionTools = CreateSessionTools(source, new AgentSessionManager(), root);

        ToolResult<CreateProjectResponse> result = await sessionTools.CreateProject(
            "motion.bep",
            width: 640,
            height: 360,
            frameRate: 30,
            duration: duration);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error!.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(result.Error.Target, Is.EqualTo("duration"));
        });
    }

    [TestCase("-00:00:01", "00:00:02", "start")]
    [TestCase("00:00:00", "-00:00:02", "duration")]
    [TestCase("00:00:00", "00:00:00", "duration")]
    public async Task Add_scene_rejects_negative_start_and_non_positive_duration(
        string start, string duration, string expectedTarget)
    {
        string root = CreateWorkspace();
        var manager = new AgentSessionManager();
        using var source = new FileSessionSource();
        SessionTools sessionTools = CreateSessionTools(source, manager, root);

        ToolResult<CreateProjectResponse> created = await sessionTools.CreateProject(
            "motion.bep", width: 640, height: 360, frameRate: 30, duration: "00:00:04");
        Assert.That(created.IsSuccess, Is.True, created.Error?.Message);

        ToolResult<AddSceneResponse> added = await sessionTools.AddScene(
            created.Value!.Session, width: 320, height: 240, start: start, duration: duration);

        FileEditingSession session = source.CurrentFileSession!;
        Assert.Multiple(() =>
        {
            Assert.That(added.IsSuccess, Is.False);
            Assert.That(added.Error!.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(added.Error.Target, Is.EqualTo(expectedTarget));
            Assert.That(session.Project.Items.OfType<Scene>().Count(), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Create_project_rejects_package_extension_and_appends_missing_project_extension()
    {
        string root = CreateWorkspace();
        using var source = new FileSessionSource();
        SessionTools sessionTools = CreateSessionTools(source, new AgentSessionManager(), root);

        ToolResult<CreateProjectResponse> rejected = await sessionTools.CreateProject(
            "wrong.beutl",
            width: 640,
            height: 360,
            frameRate: 30,
            duration: "00:00:04");
        ToolResult<CreateProjectResponse> normalized = await sessionTools.CreateProject(
            "motion",
            width: 640,
            height: 360,
            frameRate: 30,
            duration: "00:00:04");

        Assert.Multiple(() =>
        {
            Assert.That(rejected.IsSuccess, Is.False);
            Assert.That(rejected.Error!.Code, Is.EqualTo(ErrorCode.ValidationRejected));
            Assert.That(rejected.Error.Message, Does.Contain(".bep"));
            Assert.That(rejected.Error.Message, Does.Contain(".beutl"));
            Assert.That(normalized.IsSuccess, Is.True, normalized.Error?.Message);
            Assert.That(Path.GetExtension(normalized.Value!.SavedPath), Is.EqualTo(".bep"));
        });
    }

    [Test]
    public async Task Save_project_rejects_package_extension()
    {
        string root = CreateWorkspace();
        using var source = new FileSessionSource();
        SessionTools sessionTools = CreateSessionTools(source, new AgentSessionManager(), root);
        ToolResult<CreateProjectResponse> created = await sessionTools.CreateProject(
            "motion.bep",
            width: 640,
            height: 360,
            frameRate: 30,
            duration: "00:00:04");

        ToolResult<SaveProjectResponse> rejected = sessionTools.SaveProject(created.Value!.Session, "package.beutl");

        Assert.Multiple(() =>
        {
            Assert.That(created.IsSuccess, Is.True, created.Error?.Message);
            Assert.That(rejected.IsSuccess, Is.False);
            Assert.That(rejected.Error!.Code, Is.EqualTo(ErrorCode.ValidationRejected));
        });
    }

    [Test]
    public async Task Save_project_uses_current_file_session_when_session_is_omitted()
    {
        string root = CreateWorkspace();
        using var source = new FileSessionSource();
        var manager = new AgentSessionManager();
        SessionTools sessionTools = CreateSessionTools(source, manager, root);
        ToolResult<CreateProjectResponse> created = await sessionTools.CreateProject(
            "sessionless-save.bep",
            width: 640,
            height: 360,
            frameRate: 30,
            duration: "00:00:04");

        ToolResult<SaveProjectResponse> saved = sessionTools.SaveProject();

        Assert.Multiple(() =>
        {
            Assert.That(created.IsSuccess, Is.True, created.Error?.Message);
            Assert.That(saved.IsSuccess, Is.True, saved.Error?.Message);
            Assert.That(saved.Value!.Session, Is.EqualTo(created.Value!.Session));
            Assert.That(saved.Value.SavedPath, Is.EqualTo(created.Value.SavedPath));
            Assert.That(File.Exists(created.Value.SavedPath), Is.True);
        });
    }

    [Test]
    public void Save_project_reports_live_editor_sessions_as_not_required()
    {
        string root = CreateWorkspace();
        using var liveSession = new AgentToolkitTestSession(new Scene(), EditingSessionSource.LiveEditor);
        var manager = new AgentSessionManager();
        manager.UseSource(new AgentToolkitTestSessionSource(liveSession));
        using var fileSource = new FileSessionSource();
        SessionTools sessionTools = CreateSessionTools(fileSource, manager, root);

        ToolResult<SaveProjectResponse> saved = sessionTools.SaveProject(liveSession.SessionId);
        ToolResult<OperationStatusResponse> status = sessionTools.ReadOperationStatus();

        Assert.Multiple(() =>
        {
            Assert.That(saved.IsSuccess, Is.True, saved.Error?.Message);
            Assert.That(saved.Value!.Saved, Is.False);
            Assert.That(saved.Value.Source, Is.EqualTo(nameof(EditingSessionSource.LiveEditor)));
            Assert.That(saved.Value.Message, Does.Contain("save_project is not required or supported"));
            Assert.That(status.IsSuccess, Is.True, status.Error?.Message);
            Assert.That(status.Value!.SaveProjectSupported, Is.False);
            Assert.That(status.Value.Source, Is.EqualTo(nameof(EditingSessionSource.LiveEditor)));
        });
    }

    [Test]
    public async Task Create_project_builds_live_summary_through_session_dispatcher()
    {
        string root = CreateWorkspace();
        var gateway = new DispatchingProjectGateway();
        var workspace = new WorkspaceGuard(root);
        var sessionTools = new SessionTools(
            gateway,
            new AgentSessionManager(),
            workspace,
            new DestructiveGuard(),
            new RenderJobManager());

        ToolResult<CreateProjectResponse> created = await sessionTools.CreateProject(
            "live-summary.bep",
            width: 640,
            height: 360,
            frameRate: 30,
            duration: "00:00:04");

        Assert.Multiple(() =>
        {
            Assert.That(created.IsSuccess, Is.True, created.Error?.Message);
            Assert.That(created.Value!.Summary.Scenes, Has.Count.EqualTo(1));
            Assert.That(gateway.LastSession!.InvokeCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Add_scene_operates_on_the_passed_session_not_the_current_one()
    {
        string root = CreateWorkspace();
        var manager = new AgentSessionManager();
        using var source = new FileSessionSource();
        var gateway = new FileProjectSessionGateway(source, manager, new WorkspaceGuard(root));

        FileEditingSession sessionB = source.CreateProject(new ProjectCreateOptions(
            Path.Combine(root, "b.bep"), 640, 360, 30, TimeSpan.FromSeconds(4)));
        sessionB.Save(skipConflictCheck: true);

        // The gateway uses the passed session, not the current one: add_scene targets B, and B is
        // the only scene collection mutated.
        ProjectSceneResult result = await gateway.AddSceneAsync(
            sessionB, new SceneCreateOptions(320, 240, TimeSpan.Zero, TimeSpan.FromSeconds(2), "added"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Session, Is.SameAs(sessionB));
            Assert.That(sessionB.Project.Items.OfType<Scene>().Count(), Is.EqualTo(2));
        });

        // A session swapped out of the source is disposed, so add_scene authorized for it is
        // rejected rather than silently retargeting the new current project.
        FileEditingSession sessionC = source.CreateProject(new ProjectCreateOptions(
            Path.Combine(root, "c.bep"), 640, 360, 30, TimeSpan.FromSeconds(4)));
        sessionC.Save(skipConflictCheck: true);

        Assert.ThrowsAsync<SessionUnavailableException>(async () => await gateway.AddSceneAsync(
            sessionB, new SceneCreateOptions(320, 240, TimeSpan.Zero, TimeSpan.FromSeconds(2), "stale")));
    }

    private static SessionTools CreateSessionTools(FileSessionSource source, AgentSessionManager manager, string root)
    {
        var workspace = new WorkspaceGuard(root);
        return new SessionTools(
            new FileProjectSessionGateway(source, manager, workspace),
            manager,
            workspace,
            new DestructiveGuard(),
            new RenderJobManager());
    }

    private static string CreateWorkspace()
    {
        string path = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static RecoveredProjectFixture CreateProjectWithMalformedElement(string root)
    {
        string projectPath = Path.Combine(root, "recovered-project.bep");
        Project project = ProjectOperations.CreateProject(new ProjectCreateOptions(
            projectPath,
            64,
            64,
            30,
            TimeSpan.FromSeconds(1)));
        Scene scene = project.Items.OfType<Scene>().Single();
        string sceneDirectory = Path.GetDirectoryName(scene.Uri!.LocalPath)!;
        var healthy = new Element
        {
            Name = "Healthy element",
            Length = TimeSpan.FromSeconds(1),
            Uri = new Uri(Path.Combine(sceneDirectory, "healthy-element.belm")),
        };
        healthy.AddObject(new RectShape
        {
            Width = { CurrentValue = 32 },
            Height = { CurrentValue = 32 },
            Fill = { CurrentValue = Brushes.White },
        });
        var malformed = new Element
        {
            Name = "Malformed element",
            Length = TimeSpan.FromSeconds(1),
            Uri = new Uri(Path.Combine(sceneDirectory, "malformed-element.belm")),
        };
        malformed.AddObject(new RectShape
        {
            Width = { CurrentValue = 16 },
            Height = { CurrentValue = 16 },
            Fill = { CurrentValue = Brushes.Red },
        });
        scene.Children.Add(healthy);
        scene.Children.Add(malformed);
        ProjectOperations.Save(project);

        byte[] malformedBytes = System.Text.Encoding.UTF8.GetBytes(
            $"{{\"Id\":\"{malformed.Id}\",\"Name\":\"Malformed element\",\"Objects\":[");
        File.WriteAllBytes(malformed.Uri!.LocalPath, malformedBytes);
        return new RecoveredProjectFixture(
            projectPath,
            healthy.Uri!.LocalPath,
            healthy.Id,
            malformed.Uri.LocalPath,
            malformed.Id,
            malformedBytes);
    }

    public sealed class AnimatedValueHolder : EngineObject
    {
        public AnimatedValueHolder()
        {
            ScanProperties<AnimatedValueHolder>();
        }

        public IProperty<EngineObject?> AnimatedValue { get; } = Property.CreateAnimatable<EngineObject?>();
    }

    private sealed record RecoveredProjectFixture(
        string ProjectPath,
        string HealthyPath,
        Guid HealthyId,
        string MalformedPath,
        Guid MalformedId,
        byte[] MalformedBytes);

    private sealed class DispatchingProjectGateway : IProjectSessionGateway
    {
        public DispatchingLiveSession? LastSession { get; private set; }

        public ValueTask<ProjectSessionResult> OpenProjectAsync(string fullPath, CancellationToken cancellationToken = default)
            => CreateProjectAsync(new ProjectCreateOptions(fullPath, 640, 360, 30, TimeSpan.FromSeconds(4)), cancellationToken);

        public ValueTask<ProjectSessionResult> CreateProjectAsync(
            ProjectCreateOptions options,
            CancellationToken cancellationToken = default)
        {
            Project project = ProjectOperations.CreateProject(options);
            Scene scene = project.Items.OfType<Scene>().Single();
            LastSession = new DispatchingLiveSession(scene);
            return ValueTask.FromResult(new ProjectSessionResult(LastSession, project));
        }

        public ValueTask<ProjectSceneResult> AddSceneAsync(
            IEditingSession activeSession,
            SceneCreateOptions options,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class DispatchingLiveSession(CoreObject root) : IEditingSession, IEditingSessionDispatcher
    {
        public int InvokeCount { get; private set; }

        public string SessionId { get; } = Guid.NewGuid().ToString("N");

        public EditingSessionSource Source => EditingSessionSource.LiveEditor;

        public CoreObject Root => root;

        public HistoryManager History => throw new NotSupportedException();

        public DocumentAdapter Documents { get; } = new();

        public bool IsDirty => false;

        public void Invoke(Action action)
        {
            InvokeCount++;
            action();
        }
    }
}
