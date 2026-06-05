using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Serialization;

namespace Beutl.UnitTests.ProjectSystem;

// SC-002 (T018): feature 003 must not change the serialized file format. Its scale machinery
// (ResolutionPolicy overrides, scale ctor params, WorkingScale/EffectiveScale) is runtime-only, so
// existing projects load with zero migration and no version bump. Non-GPU.
public class NoMigrationRegressionTests
{
    [Test]
    public void Dilate_DoesNotSerializeScaleMachinery_AndRoundTripsStably()
    {
        var dilate = new Dilate();
        dilate.RadiusX.CurrentValue = 5f;
        dilate.RadiusY.CurrentValue = 7f;

        var json = CoreSerializer.SerializeToJsonObject(dilate);
        string s1 = json.ToJsonString();

        // 003 added ResolutionPolicy as a computed override (PreserveSource on Dilate), NOT a property.
        Assert.That(s1, Does.Not.Contain("ResolutionPolicy"));
        Assert.That(s1, Does.Not.Contain("WorkingScale"));
        Assert.That(s1, Does.Not.Contain("EffectiveScale"));
        Assert.That(s1, Does.Not.Contain("OutputScale"));

        var restored = (Dilate)CoreSerializer.DeserializeFromJsonObject(json, typeof(Dilate));
        string s2 = CoreSerializer.SerializeToJsonObject(restored).ToJsonString();
        Assert.That(s2, Is.EqualTo(s1), "round-trip must be byte-stable (no migration / format drift)");
    }

    [Test]
    public void BlurredEllipse_RoundTripsStably_AndIsCurrentFormat()
    {
        var shape = new EllipseShape();
        shape.Width.CurrentValue = 100;
        shape.Height.CurrentValue = 80;
        var blur = new Blur();
        blur.Sigma.CurrentValue = new Size(4, 4);
        shape.FilterEffect.CurrentValue = blur;

        var json = CoreSerializer.SerializeToJsonObject(shape);
        string s1 = json.ToJsonString();

        // Current EngineObject format, not the legacy Operation/Children one that ElementMigration handles.
        Assert.That(s1, Does.Not.Contain("\"Operation\""));
        Assert.That(s1, Does.Not.Contain("WorkingScale"));

        var restored = (EllipseShape)CoreSerializer.DeserializeFromJsonObject(json, typeof(EllipseShape));
        string s2 = CoreSerializer.SerializeToJsonObject(restored).ToJsonString();
        Assert.That(s2, Is.EqualTo(s1), "round-trip must be byte-stable");
    }
}
