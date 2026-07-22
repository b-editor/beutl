using Beutl.AgentToolkit.Rendering;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Tests.Helpers;
using Beutl.AgentToolkit.Tools;
using Beutl.Engine;
using Beutl.Engine.Expressions;
using Beutl.Graphics.Shapes;
using Beutl.Graphics3D;
using Beutl.ProjectSystem;

namespace Beutl.AgentToolkit.Tests.Rendering;

public sealed class SceneSnapshotTests
{
    [Test]
    public void File_session_snapshots_are_isolated_clones()
    {
        var scene = new Scene(640, 360, "Scene");
        using var session = new AgentToolkitTestSession(scene, EditingSessionSource.File);

        // Renders run after ReadOnSession releases the dispatch lock, so a concurrent apply_edit
        // could mutate a shared live scene mid-render; every snapshot must be a clone.
        Scene snapshot = RenderTools.CreateSceneSnapshot(session);

        Assert.Multiple(() =>
        {
            Assert.That(snapshot, Is.Not.SameAs(scene));
            Assert.That(snapshot.Id, Is.EqualTo(scene.Id));
        });
    }

    [Test]
    public void Gpu_preflight_scopes_to_the_sampled_time_and_visible_window()
    {
        string dir = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var scene = new Scene(640, 360, "Scene")
        {
            Duration = TimeSpan.FromSeconds(4),
            Uri = new Uri(Path.Combine(dir, "Scene.scene"))
        };
        var element = new Element
        {
            Start = TimeSpan.Zero,
            Length = TimeSpan.FromSeconds(2),
            Uri = new Uri(Path.Combine(dir, "el.belm"))
        };
        element.AddObject(new Scene3D());
        scene.Children.Add(element);

        Assert.Multiple(() =>
        {
            Assert.That(StillRenderer.ContainsGpuOnlyContent(scene, TimeSpan.FromSeconds(1)), Is.True);
            Assert.That(StillRenderer.ContainsGpuOnlyContent(scene, TimeSpan.FromSeconds(3)), Is.False);
            Assert.That(StillRenderer.ContainsGpuOnlyContent(scene), Is.True);
        });

        // Trimming the scene to [10s, 14s) puts the 3D element outside the visible window entirely.
        scene.Start = TimeSpan.FromSeconds(10);

        Assert.Multiple(() =>
        {
            Assert.That(StillRenderer.ContainsGpuOnlyContent(scene, TimeSpan.FromSeconds(1)), Is.False);
            Assert.That(StillRenderer.ContainsGpuOnlyContent(scene), Is.False);
        });
    }

    [Test]
    public void Gpu_preflight_follows_expression_supplied_scene_references()
    {
        (Scene outer, Scene referenced) = BuildOuterWithReferencedScene3D(r =>
        {
            var sceneDrawable = new SceneDrawable();
            sceneDrawable.ReferencedScene.Expression = new ReferenceExpression<Scene?>(r.Id);
            return sceneDrawable;
        });
        AttachProject(outer, referenced);

        Assert.That(StillRenderer.ContainsGpuOnlyContent(outer), Is.True);
    }

    [Test]
    public void Gpu_preflight_ignores_non_scene_reference_expressions()
    {
        // ReferenceExpression is a general binding form; one on a non-scene property (here a Shape's
        // Width) is a data-binding, not a scene inclusion, so the referenced scene's 3D content must
        // not be pulled into the GPU requirement.
        (Scene outer, Scene referenced) = BuildOuterWithReferencedScene3D(r =>
        {
            var rect = new RectShape();
            rect.Width.Expression = new ReferenceExpression<float>(r.Id);
            return rect;
        });
        AttachProject(outer, referenced);

        Assert.That(StillRenderer.ContainsGpuOnlyContent(outer), Is.False);
    }

    [Test]
    public void Gpu_preflight_ignores_reference_expressions_resolving_to_a_non_scene()
    {
        // ReferenceExpression<Scene?> evaluates a non-Scene target to null, so a reference whose
        // ObjectId resolves to another object (here the referenced scene's Element, not the Scene)
        // contributes nothing at composition time and must not force the GPU requirement.
        (Scene outer, Scene referenced) = BuildOuterWithReferencedScene3D(r =>
        {
            var sceneDrawable = new SceneDrawable();
            sceneDrawable.ReferencedScene.Expression = new ReferenceExpression<Scene?>(r.Children.Single().Id);
            return sceneDrawable;
        });
        AttachProject(outer, referenced);

        Assert.That(StillRenderer.ContainsGpuOnlyContent(outer), Is.False);
    }

    [Test]
    public void Gpu_preflight_ignores_audio_only_scene_references()
    {
        // A SceneSound renders the referenced scene's audio only and is not a Drawable, so its
        // Scene3D must not force the graphics GPU requirement.
        (Scene outer, Scene referenced) = BuildOuterWithReferencedScene3D(r =>
        {
            var sceneSound = new SceneSound();
            sceneSound.ReferencedScene.Expression = new ReferenceExpression<Scene?>(r.Id);
            return sceneSound;
        });
        AttachProject(outer, referenced);

        Assert.That(StillRenderer.ContainsGpuOnlyContent(outer), Is.False);
    }

    [Test]
    public void Snapshot_clones_expression_referenced_scenes_for_isolated_render()
    {
        (Scene outer, Scene referenced) = BuildOuterWithReferencedScene3D(r =>
        {
            var sceneDrawable = new SceneDrawable();
            sceneDrawable.ReferencedScene.Expression = new ReferenceExpression<Scene?>(r.Id);
            return sceneDrawable;
        });
        AttachProject(outer, referenced);
        using var session = new AgentToolkitTestSession(outer, EditingSessionSource.File);

        Scene snapshot = RenderTools.CreateSceneSnapshot(session);

        SceneDrawable snapshotDrawable = snapshot.Children
            .Single()
            .Objects
            .OfType<SceneDrawable>()
            .Single();
        Scene? clonedTarget = (((IHierarchical)snapshot).HierarchicalRoot as ICoreObject)
            ?.FindById(referenced.Id) as Scene;
        Assert.Multiple(() =>
        {
            Assert.That(
                (snapshotDrawable.ReferencedScene.Expression as IReferenceExpression)?.ObjectId,
                Is.EqualTo(referenced.Id));
            Assert.That(clonedTarget, Is.Not.Null);
            Assert.That(clonedTarget, Is.Not.SameAs(referenced));
            Assert.That(clonedTarget!.Children, Has.Count.EqualTo(1));
            Assert.That(StillRenderer.ContainsGpuOnlyContent(snapshot), Is.True);
        });
    }

    [Test]
    public void Snapshot_clones_expression_referenced_scenes_into_its_own_root()
    {
        (Scene outer, Scene referenced) = BuildOuterWithReferencedScene3D(r =>
        {
            var sceneDrawable = new SceneDrawable();
            sceneDrawable.ReferencedScene.Expression = new ReferenceExpression<Scene?>(r.Id);
            return sceneDrawable;
        });
        AttachProject(outer, referenced);
        using var session = new AgentToolkitTestSession(outer, EditingSessionSource.File);

        Scene snapshot = RenderTools.CreateSceneSnapshot(session);

        // The snapshot must carry its own hierarchy root: a detached owner's expression lookup
        // falls back to the live BeutlApplication.Current, breaking snapshot isolation.
        IHierarchicalRoot? snapshotRoot = ((IHierarchical)snapshot).HierarchicalRoot;
        Assert.That(snapshotRoot, Is.Not.Null);
        Scene? clonedTarget = (snapshotRoot as ICoreObject)?.FindById(referenced.Id) as Scene;
        Assert.Multiple(() =>
        {
            Assert.That(clonedTarget, Is.Not.Null);
            Assert.That(clonedTarget, Is.Not.SameAs(referenced));
            Assert.That(clonedTarget!.Children, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void Gpu_preflight_terminates_on_mutual_scene_references()
    {
        string dir = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var sceneA = new Scene(640, 360, "A")
        {
            Duration = TimeSpan.FromSeconds(2),
            Uri = new Uri(Path.Combine(dir, "A.scene"))
        };
        var sceneB = new Scene(640, 360, "B")
        {
            Duration = TimeSpan.FromSeconds(2),
            Uri = new Uri(Path.Combine(dir, "B.scene"))
        };
        AddReferenceElement(sceneA, sceneB, Path.Combine(dir, "a-el.belm"));
        AddReferenceElement(sceneB, sceneA, Path.Combine(dir, "b-el.belm"));
        AttachProject(sceneA, sceneB);

        Assert.That(StillRenderer.ContainsGpuOnlyContent(sceneA), Is.False);

        static void AddReferenceElement(Scene owner, Scene target, string uri)
        {
            var drawable = new SceneDrawable();
            drawable.ReferencedScene.Expression = new ReferenceExpression<Scene?>(target.Id);
            var element = new Element
            {
                Start = TimeSpan.Zero,
                Length = TimeSpan.FromSeconds(2),
                Uri = new Uri(uri)
            };
            element.AddObject(drawable);
            owner.Children.Add(element);
        }
    }

    [Test]
    public void Active_element_summary_filters_at_the_scene_start_offset()
    {
        string dir = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var scene = new Scene(640, 360, "Scene")
        {
            Start = TimeSpan.FromSeconds(10),
            Uri = new Uri(Path.Combine(dir, "Scene.scene"))
        };
        var element = new Element
        {
            Start = TimeSpan.FromSeconds(10),
            Length = TimeSpan.FromSeconds(2),
            Uri = new Uri(Path.Combine(dir, "el.belm"))
        };
        scene.Children.Add(element);

        // The frame at local time 1s renders at 11s (time + scene.Start), where the element is visible.
        IReadOnlyList<RenderStillActiveElement> visible = StillRenderer.CreateActiveElementSummaries(scene, TimeSpan.FromSeconds(1));
        // Local time 5s renders at 15s, past the element's range.
        IReadOnlyList<RenderStillActiveElement> hidden = StillRenderer.CreateActiveElementSummaries(scene, TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(visible.Select(e => e.Id), Does.Contain(element.Id.ToString()));
            Assert.That(hidden, Is.Empty);
        });
    }

    private static (Scene Outer, Scene Referenced) BuildOuterWithReferencedScene3D(Func<Scene, EngineObject> makeReference)
    {
        string dir = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var outer = new Scene(640, 360, "Outer")
        {
            Duration = TimeSpan.FromSeconds(4),
            Uri = new Uri(Path.Combine(dir, "Outer.scene"))
        };
        var referenced = new Scene(640, 360, "Referenced")
        {
            Duration = TimeSpan.FromSeconds(4),
            Uri = new Uri(Path.Combine(dir, "Referenced.scene"))
        };
        var referencedElement = new Element
        {
            Start = TimeSpan.Zero,
            Length = TimeSpan.FromSeconds(4),
            Uri = new Uri(Path.Combine(dir, "ref-el.belm"))
        };
        referencedElement.AddObject(new Scene3D());
        referenced.Children.Add(referencedElement);

        var outerElement = new Element
        {
            Start = TimeSpan.Zero,
            Length = TimeSpan.FromSeconds(4),
            Uri = new Uri(Path.Combine(dir, "outer-el.belm"))
        };
        outerElement.AddObject(makeReference(referenced));
        outer.Children.Add(outerElement);
        return (outer, referenced);
    }

    // A ReferenceExpression resolves its ObjectId through the owner's hierarchical root, so the
    // referencing and referenced scenes must share a project root for the lookup to succeed.
    private static void AttachProject(params Scene[] scenes)
    {
        var project = new Project();
        foreach (Scene scene in scenes)
        {
            project.Items.Add(scene);
        }

        _ = new BeutlApplication { Project = project };
    }
}
