using System.Reactive.Linq;
using Beutl.Editor.Services;
using Beutl.ProjectSystem;

namespace Beutl.UnitTests.Editor.Services;

[TestFixture]
public class ElementEditabilityTests
{
    [Test]
    public void Observe_TracksLocksRowsAndLayerMembership()
    {
        var scene = new Scene();
        var element = new Element { ZIndex = 2 };
        scene.Children.Add(element);
        var values = new List<bool>();
        using var subscription = ElementEditability.Observe(element).Subscribe(values.Add);
        Assert.That(values[^1], Is.True);

        var layer = new TimelineLayer { ZIndex = 2, IsLocked = true };
        scene.Layers.Add(layer);
        Assert.That(values[^1], Is.False);
        layer.IsLocked = false;
        Assert.That(values[^1], Is.True);
        layer.IsLocked = true;
        Assert.That(values[^1], Is.False);

        layer.ZIndex = 3;
        Assert.That(values[^1], Is.True);
        element.ZIndex = 3;
        Assert.That(values[^1], Is.False);
        element.IsLocked = true;
        scene.Layers.Remove(layer);
        Assert.That(values[^1], Is.False, "The clip's own lock still applies after its layer is removed.");
        element.IsLocked = false;
        Assert.That(values[^1], Is.True);

        scene.Layers.Add(layer);
        Assert.That(values[^1], Is.False);
        scene.Layers[0] = new TimelineLayer { ZIndex = 3 };
        Assert.That(values[^1], Is.True);
        int count = values.Count;
        layer.IsLocked = false;
        layer.IsLocked = true;
        Assert.That(values, Has.Count.EqualTo(count), "Removed layers must be unsubscribed.");

        // Scene.IsElementLocked considers every matching layer, not only the first.
        scene.Layers.Add(new TimelineLayer { ZIndex = 3, IsLocked = true });
        Assert.That(values[^1], Is.False);
        scene.Layers.Clear();
        Assert.That(values[^1], Is.True);
    }

    [Test]
    public void Observe_WithoutSceneHonorsTheElementLock()
    {
        var element = new Element { IsLocked = true };
        var values = new List<bool>();
        using var subscription = ElementEditability.Observe(element).Subscribe(values.Add);
        element.IsLocked = false;
        element.IsLocked = true;
        Assert.That(values, Is.EqualTo(new[] { false, true, false }));
    }

    [Test]
    public void Observe_ManyLayersInitializeAndRebindToCurrentLocks()
    {
        var scene = new Scene();
        var element = new Element { ZIndex = 2999 };
        for (int i = 0; i < 3000; i++)
            scene.Layers.Add(new TimelineLayer { ZIndex = i });
        var values = new List<bool>();
        using var subscription = ElementEditability.Observe(element, scene).Subscribe(values.Add);
        Assert.That(values, Is.EqualTo(new[] { true }));

        TimelineLayer locked = scene.Layers[^1];
        locked.IsLocked = true;
        scene.Layers.Add(new TimelineLayer { ZIndex = 3000 });
        Assert.That(values, Is.EqualTo(new[] { true, false }));
        scene.Layers.Remove(locked);
        Assert.That(values[^1], Is.True);
        scene.Layers[^1].ZIndex = element.ZIndex;
        scene.Layers[^1].IsLocked = true;
        Assert.That(values[^1], Is.False);
    }

    [Test]
    public void Observe_WithoutElementAllowsNonClipEditors()
    {
        bool? editable = null;
        using var subscription = ElementEditability.Observe(null).Subscribe(value => editable = value);
        Assert.That(editable, Is.True);
    }

    [Test]
    public void Disposing_StopsElementAndLayerNotifications()
    {
        var scene = new Scene();
        var element = new Element();
        var layer = new TimelineLayer();
        scene.Layers.Add(layer);
        var values = new List<bool>();
        var subscription = ElementEditability.Observe(element, scene).Subscribe(values.Add);
        subscription.Dispose();
        element.IsLocked = true;
        element.ZIndex = 1;
        layer.IsLocked = true;
        layer.ZIndex = 1;
        scene.Layers.Add(new TimelineLayer { ZIndex = 1, IsLocked = true });
        Assert.That(values, Is.EqualTo(new[] { true }));
    }
}
