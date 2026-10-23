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
        var element = new Element { ZIndex = { Value = 2 } };
        scene.Children.Add(element);
        var values = new List<bool>();
        using var subscription = ElementEditability.Observe(element).Subscribe(values.Add);
        Assert.That(values[^1], Is.True);

        var layer = new TimelineLayer { ZIndex = { Value = 2 }, IsLocked = { Value = true } };
        scene.Layers.Add(layer);
        Assert.That(values[^1], Is.False);
        layer.IsLocked.Value = false;
        Assert.That(values[^1], Is.True);
        layer.IsLocked.Value = true;
        Assert.That(values[^1], Is.False);

        layer.ZIndex.Value = 3;
        Assert.That(values[^1], Is.True);
        element.ZIndex.Value = 3;
        Assert.That(values[^1], Is.False);
        element.IsLocked.Value = true;
        scene.Layers.Remove(layer);
        Assert.That(values[^1], Is.False, "The clip's own lock still applies after its layer is removed.");
        element.IsLocked.Value = false;
        Assert.That(values[^1], Is.True);

        scene.Layers.Add(layer);
        Assert.That(values[^1], Is.False);
        scene.Layers[0] = new TimelineLayer { ZIndex = { Value = 3 } };
        Assert.That(values[^1], Is.True);
        int count = values.Count;
        layer.IsLocked.Value = false;
        layer.IsLocked.Value = true;
        Assert.That(values, Has.Count.EqualTo(count), "Removed layers must be unsubscribed.");

        // Scene.IsElementLocked considers every matching layer, not only the first.
        scene.Layers.Add(new TimelineLayer { ZIndex = { Value = 3 }, IsLocked = { Value = true } });
        Assert.That(values[^1], Is.False);
        scene.Layers.Clear();
        Assert.That(values[^1], Is.True);
    }

    [Test]
    public void Observe_WithoutSceneHonorsTheElementLock()
    {
        var element = new Element { IsLocked = { Value = true } };
        var values = new List<bool>();
        using var subscription = ElementEditability.Observe(element).Subscribe(values.Add);
        element.IsLocked.Value = false;
        element.IsLocked.Value = true;
        Assert.That(values, Is.EqualTo(new[] { false, true, false }));
    }

    [Test]
    public void Observe_ManyLayersInitializeAndRebindToCurrentLocks()
    {
        var scene = new Scene();
        var element = new Element { ZIndex = { Value = 2999 } };
        for (int i = 0; i < 3000; i++)
            scene.Layers.Add(new TimelineLayer { ZIndex = { Value = i } });
        var values = new List<bool>();
        using var subscription = ElementEditability.Observe(element, scene).Subscribe(values.Add);
        Assert.That(values, Is.EqualTo(new[] { true }));

        TimelineLayer locked = scene.Layers[^1];
        locked.IsLocked.Value = true;
        scene.Layers.Add(new TimelineLayer { ZIndex = { Value = 3000 } });
        Assert.That(values, Is.EqualTo(new[] { true, false }));
        scene.Layers.Remove(locked);
        Assert.That(values[^1], Is.True);
        scene.Layers[^1].ZIndex.Value = element.ZIndex.Value;
        scene.Layers[^1].IsLocked.Value = true;
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
        element.IsLocked.Value = true;
        element.ZIndex.Value = 1;
        layer.IsLocked.Value = true;
        layer.ZIndex.Value = 1;
        scene.Layers.Add(new TimelineLayer { ZIndex = { Value = 1 }, IsLocked = { Value = true } });
        Assert.That(values, Is.EqualTo(new[] { true }));
    }
}
