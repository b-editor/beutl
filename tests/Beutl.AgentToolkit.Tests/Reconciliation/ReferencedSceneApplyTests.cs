using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.AgentToolkit.Tests.Helpers;
using Beutl.Engine.Expressions;
using Beutl.ProjectSystem;

namespace Beutl.AgentToolkit.Tests.Reconciliation;

// Issue #2110: a Scene reference is set through the Expressions form (ReferenceExpression) so it
// resolves to the existing project scene by Id; a direct value cannot resolve (the engine would mint
// an empty Scene), and an unresolvable expression must be rejected instead of composing empty.
[TestFixture]
public sealed class ReferencedSceneApplyTests
{
    private static (AgentToolkitTestSession Session, Scene Parent, Scene Child, SceneDrawable Drawable)
        CreateNestedSceneSession()
    {
        string dir = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var parent = new Scene(1920, 1080, "parent") { Uri = new Uri(Path.Combine(dir, "parent.scene")) };
        var child = new Scene(960, 540, "child") { Uri = new Uri(Path.Combine(dir, "child.scene")) };
        var project = new Project();
        project.Items.Add(parent);
        project.Items.Add(child);
        _ = new BeutlApplication { Project = project };

        var drawable = new SceneDrawable();
        var element = new Element
        {
            Start = TimeSpan.Zero,
            Length = TimeSpan.FromSeconds(2),
            Uri = new Uri(Path.Combine(dir, "element.belm")),
        };
        element.AddObject(drawable);
        parent.Children.Add(element);

        var session = new AgentToolkitTestSession(parent);
        return (session, parent, child, drawable);
    }

    private static JsonObject GetDrawableNode(JsonObject document)
    {
        var elements = (JsonArray)document["Elements"]!;
        var objects = (JsonArray)((JsonObject)elements[0]!)["Objects"]!;
        return (JsonObject)objects[0]!;
    }

    private static Guid? ExpressionObjectId(SceneDrawable drawable)
        => (drawable.ReferencedScene.Expression as IReferenceExpression)?.ObjectId;

    [Test]
    public void Apply_DirectInlineSceneValue_IsRejectedWithExpressionsGuidance()
    {
        (AgentToolkitTestSession session, Scene parent, Scene child, SceneDrawable drawable) =
            CreateNestedSceneSession();
        using (session)
        {
            var reconciler = new Reconciler();
            JsonObject desired = session.Documents.Read(parent);
            GetDrawableNode(desired)["ReferencedScene"] = new JsonObject
            {
                ["$type"] = "[Beutl.ProjectSystem]:Scene",
                ["Id"] = child.Id.ToString(),
            };

            ReconcileException? ex = Assert.Throws<ReconcileException>(() => reconciler.Apply(session, desired));

            Assert.Multiple(() =>
            {
                Assert.That(ex!.Error.Code, Is.EqualTo(ErrorCode.ValidationRejected));
                Assert.That(ex.Error.Message, Does.Contain("Expressions"));
                Assert.That(drawable.ReferencedScene.CurrentValue, Is.Null);
                Assert.That(drawable.ReferencedScene.Expression, Is.Null);
            });
        }
    }

    [Test]
    public void Apply_DirectGuidStringValue_IsRejectedWithExpressionsGuidance()
    {
        (AgentToolkitTestSession session, Scene parent, Scene child, SceneDrawable drawable) =
            CreateNestedSceneSession();
        using (session)
        {
            var reconciler = new Reconciler();
            JsonObject desired = session.Documents.Read(parent);
            GetDrawableNode(desired)["ReferencedScene"] = child.Id.ToString();

            ReconcileException? ex = Assert.Throws<ReconcileException>(() => reconciler.Apply(session, desired));

            Assert.Multiple(() =>
            {
                Assert.That(ex!.Error.Code, Is.EqualTo(ErrorCode.ValidationRejected));
                Assert.That(ex.Error.Message, Does.Contain("Expressions"));
                Assert.That(drawable.ReferencedScene.CurrentValue, Is.Null);
            });
        }
    }

    [Test]
    public void Apply_ExpressionReferenceWithUnknownObjectId_IsRejectedWithoutMutation()
    {
        (AgentToolkitTestSession session, Scene parent, _, SceneDrawable drawable) =
            CreateNestedSceneSession();
        using (session)
        {
            var reconciler = new Reconciler();
            JsonObject desired = session.Documents.Read(parent);
            GetDrawableNode(desired)["Expressions"] = new JsonObject
            {
                ["ReferencedScene"] = new JsonObject { ["ObjectId"] = JsonValue.Create(Guid.NewGuid()) },
            };

            ReconcileException? ex = Assert.Throws<ReconcileException>(() => reconciler.Apply(session, desired));

            Assert.Multiple(() =>
            {
                Assert.That(ex!.Error.Code, Is.EqualTo(ErrorCode.ValidationRejected));
                Assert.That(ex.Error.Message, Does.Contain("exists in the project"));
                Assert.That(drawable.ReferencedScene.Expression, Is.Null);
            });
        }
    }

    [Test]
    public void Apply_ExpressionReferenceWithAllZeroObjectId_IsRejectedWithoutMutation()
    {
        (AgentToolkitTestSession session, Scene parent, _, SceneDrawable drawable) =
            CreateNestedSceneSession();
        using (session)
        {
            var reconciler = new Reconciler();
            JsonObject desired = session.Documents.Read(parent);
            GetDrawableNode(desired)["Expressions"] = new JsonObject
            {
                ["ReferencedScene"] = new JsonObject { ["ObjectId"] = JsonValue.Create(Guid.Empty) },
            };

            ReconcileException? ex = Assert.Throws<ReconcileException>(() => reconciler.Apply(session, desired));

            Assert.Multiple(() =>
            {
                Assert.That(ex!.Error.Code, Is.EqualTo(ErrorCode.ValidationRejected));
                Assert.That(ex.Error.Message, Does.Contain("all-zero"));
                Assert.That(drawable.ReferencedScene.Expression, Is.Null);
            });
        }
    }

    [Test]
    public void Apply_ExpressionReferenceMissingObjectId_IsRejected()
    {
        (AgentToolkitTestSession session, Scene parent, _, SceneDrawable drawable) =
            CreateNestedSceneSession();
        using (session)
        {
            var reconciler = new Reconciler();
            JsonObject desired = session.Documents.Read(parent);
            // A present expression node without a valid ObjectId (here a misspelled key) parses to
            // no expression, silently clearing the reference; apply must reject it instead.
            GetDrawableNode(desired)["Expressions"] = new JsonObject
            {
                ["ReferencedScene"] = new JsonObject { ["ObjectID"] = Guid.NewGuid().ToString() },
            };

            ReconcileException? ex = Assert.Throws<ReconcileException>(() => reconciler.Apply(session, desired));

            Assert.Multiple(() =>
            {
                Assert.That(ex!.Error.Code, Is.EqualTo(ErrorCode.ValidationRejected));
                Assert.That(ex.Error.Message, Does.Contain("ObjectId"));
                Assert.That(drawable.ReferencedScene.Expression, Is.Null);
            });
        }
    }

    [Test]
    public void Apply_ExpressionReferenceWithObjectFormObjectId_IsRejected()
    {
        (AgentToolkitTestSession session, Scene parent, Scene child, SceneDrawable drawable) =
            CreateNestedSceneSession();
        using (session)
        {
            var reconciler = new Reconciler();
            JsonObject desired = session.Documents.Read(parent);
            // The engine's expression parser only builds a ReferenceExpression from a scalar Guid
            // ObjectId; an object form would deserialize to no expression, so apply must reject it
            // rather than silently drop the reference.
            GetDrawableNode(desired)["Expressions"] = new JsonObject
            {
                ["ReferencedScene"] = new JsonObject
                {
                    ["ObjectId"] = new JsonObject { ["Id"] = child.Id.ToString() },
                },
            };

            ReconcileException? ex = Assert.Throws<ReconcileException>(() => reconciler.Apply(session, desired));

            Assert.Multiple(() =>
            {
                Assert.That(ex!.Error.Code, Is.EqualTo(ErrorCode.ValidationRejected));
                Assert.That(ex.Error.Message, Does.Contain("Guid string"));
                Assert.That(drawable.ReferencedScene.Expression, Is.Null);
            });
        }
    }

    [Test]
    public void Apply_ExpressionReferenceWithWrongType_IsRejectedWithoutMutation()
    {
        (AgentToolkitTestSession session, Scene parent, _, SceneDrawable drawable) =
            CreateNestedSceneSession();
        using (session)
        {
            var reconciler = new Reconciler();
            JsonObject desired = session.Documents.Read(parent);
            // ObjectId points at the timeline Element (not a Scene); with no PropertyPath the
            // expression evaluates to null, so apply must reject rather than compose empty.
            GetDrawableNode(desired)["Expressions"] = new JsonObject
            {
                ["ReferencedScene"] = new JsonObject { ["ObjectId"] = JsonValue.Create(parent.Children.Single().Id) },
            };

            ReconcileException? ex = Assert.Throws<ReconcileException>(() => reconciler.Apply(session, desired));

            Assert.Multiple(() =>
            {
                Assert.That(ex!.Error.Code, Is.EqualTo(ErrorCode.ValidationRejected));
                Assert.That(ex.Error.Message, Does.Contain("exists in the project"));
                Assert.That(drawable.ReferencedScene.Expression, Is.Null);
            });
        }
    }

    [Test]
    public void Apply_ExpressionReferenceWithEmptyPropertyPathAndWrongType_IsRejectedWithoutMutation()
    {
        (AgentToolkitTestSession session, Scene parent, _, SceneDrawable drawable) =
            CreateNestedSceneSession();
        using (session)
        {
            var reconciler = new Reconciler();
            JsonObject desired = session.Documents.Read(parent);
            // An empty PropertyPath evaluates as a direct-object reference, so a non-Scene target
            // (the timeline Element) must fail the referenced-type check instead of bypassing it.
            GetDrawableNode(desired)["Expressions"] = new JsonObject
            {
                ["ReferencedScene"] = new JsonObject
                {
                    ["ObjectId"] = JsonValue.Create(parent.Children.Single().Id),
                    ["PropertyPath"] = "",
                },
            };

            ReconcileException? ex = Assert.Throws<ReconcileException>(() => reconciler.Apply(session, desired));

            Assert.Multiple(() =>
            {
                Assert.That(ex!.Error.Code, Is.EqualTo(ErrorCode.ValidationRejected));
                Assert.That(ex.Error.Message, Does.Contain("exists in the project"));
                Assert.That(drawable.ReferencedScene.Expression, Is.Null);
            });
        }
    }

    [Test]
    public void Apply_ExpressionReferenceWithPropertyPath_IsRejectedAsUnsupported()
    {
        (AgentToolkitTestSession session, Scene parent, Scene child, SceneDrawable drawable) =
            CreateNestedSceneSession();
        using (session)
        {
            var reconciler = new Reconciler();
            JsonObject desired = session.Documents.Read(parent);
            // A scene reference names its target directly by Id. The PropertyPath form cannot be
            // attached to a project-detached render snapshot, so it is rejected even when the path
            // resolves to a Scene-typed property (here child.ReferencedScene) at runtime.
            GetDrawableNode(desired)["Expressions"] = new JsonObject
            {
                ["ReferencedScene"] = new JsonObject
                {
                    ["ObjectId"] = JsonValue.Create(child.Id),
                    ["PropertyPath"] = "ReferencedScene",
                },
            };

            ReconcileException? ex = Assert.Throws<ReconcileException>(() => reconciler.Apply(session, desired));

            Assert.Multiple(() =>
            {
                Assert.That(ex!.Error.Code, Is.EqualTo(ErrorCode.ValidationRejected));
                Assert.That(ex.Error.Message, Does.Contain("PropertyPath"));
                Assert.That(drawable.ReferencedScene.Expression, Is.Null);
            });
        }
    }

    [Test]
    public void Apply_ExpressionReferenceWithValidObjectId_IsAcceptedWithoutAdoptingChild()
    {
        (AgentToolkitTestSession session, Scene parent, Scene child, SceneDrawable drawable) =
            CreateNestedSceneSession();
        using (session)
        {
            IHierarchical? childParentBefore = ((IHierarchical)child).HierarchicalParent;
            var reconciler = new Reconciler();
            JsonObject desired = session.Documents.Read(parent);
            GetDrawableNode(desired)["Expressions"] = new JsonObject
            {
                ["ReferencedScene"] = new JsonObject { ["ObjectId"] = JsonValue.Create(child.Id) },
            };

            reconciler.Apply(session, desired);

            Assert.Multiple(() =>
            {
                Assert.That(ExpressionObjectId(drawable), Is.EqualTo(child.Id));
                // The reference resolves through the expression, so the child scene keeps its own
                // parent and is never adopted into the referencing drawable's subtree.
                Assert.That(drawable.ReferencedScene.CurrentValue, Is.Null);
                Assert.That(((IHierarchical)child).HierarchicalParent, Is.SameAs(childParentBefore));
                Assert.That(((IHierarchical)drawable).HierarchicalChildren, Does.Not.Contain(child));
            });
        }
    }

    [Test]
    public void Apply_InsertedDrawableWithUnknownExpressionReference_IsRejected()
    {
        (AgentToolkitTestSession session, Scene parent, _, _) = CreateNestedSceneSession();
        using (session)
        {
            var reconciler = new Reconciler();
            JsonObject desired = session.Documents.Read(parent);
            ((JsonArray)desired["Elements"]!).Add(new JsonObject
            {
                ["$type"] = "[Beutl.ProjectSystem]:Element",
                ["Name"] = "Nested scene element",
                ["Start"] = TimeSpan.FromSeconds(0).ToString("c"),
                ["Length"] = TimeSpan.FromSeconds(2).ToString("c"),
                ["Objects"] = new JsonArray(new JsonObject
                {
                    ["$type"] = "[Beutl.ProjectSystem]:SceneDrawable",
                    ["Expressions"] = new JsonObject
                    {
                        ["ReferencedScene"] = new JsonObject { ["ObjectId"] = JsonValue.Create(Guid.NewGuid()) },
                    },
                }),
            });

            ReconcileException? ex = Assert.Throws<ReconcileException>(() => reconciler.Apply(session, desired));

            Assert.Multiple(() =>
            {
                Assert.That(ex!.Error.Code, Is.EqualTo(ErrorCode.ValidationRejected));
                Assert.That(parent.Children, Has.Count.EqualTo(1));
            });
        }
    }

    [Test]
    public void Apply_InsertedDrawableWithValidExpressionReference_IsAccepted()
    {
        (AgentToolkitTestSession session, Scene parent, Scene child, _) = CreateNestedSceneSession();
        using (session)
        {
            var reconciler = new Reconciler();
            JsonObject desired = session.Documents.Read(parent);
            ((JsonArray)desired["Elements"]!).Add(new JsonObject
            {
                ["$type"] = "[Beutl.ProjectSystem]:Element",
                ["Name"] = "Nested scene element",
                ["Start"] = TimeSpan.FromSeconds(0).ToString("c"),
                ["Length"] = TimeSpan.FromSeconds(2).ToString("c"),
                ["Objects"] = new JsonArray(new JsonObject
                {
                    ["$type"] = "[Beutl.ProjectSystem]:SceneDrawable",
                    ["Expressions"] = new JsonObject
                    {
                        ["ReferencedScene"] = new JsonObject { ["ObjectId"] = JsonValue.Create(child.Id) },
                    },
                }),
            });

            reconciler.Apply(session, desired);

            SceneDrawable inserted = parent.Children
                .Single(e => e.Name == "Nested scene element")
                .Objects
                .OfType<SceneDrawable>()
                .Single();
            Assert.That(ExpressionObjectId(inserted), Is.EqualTo(child.Id));
        }
    }
}
