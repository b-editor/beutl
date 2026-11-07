using System.Text.Json.Nodes;
using Beutl.Configuration;
using Beutl.Serialization;

namespace Beutl.UnitTests.Configuration;

[TestFixture]
public class EditorConfigToneMappingTests
{
    [Test]
    public void DefaultValue_IsAuto()
    {
        Assert.That(new EditorConfig().ToneMappingMode.Value, Is.EqualTo(UIToneMappingOperator.Auto));
    }

    // Settings files hold the mode's number. 0 was None, the default, before Auto existed and reads as Auto now, so a
    // preview left at that default follows its display; the other numbers keep their modes.
    [TestCase(0, UIToneMappingOperator.Auto)]
    [TestCase(1, UIToneMappingOperator.Reinhard)]
    [TestCase(2, UIToneMappingOperator.ACES)]
    [TestCase(3, UIToneMappingOperator.Hable)]
    [TestCase(4, UIToneMappingOperator.FitToDisplay)]
    [TestCase(5, UIToneMappingOperator.None)]
    public void SavedNumber_ReadsAsItsMode(int saved, UIToneMappingOperator expected)
    {
        JsonObject json = CoreSerializer.SerializeToJsonObject(new EditorConfig());
        json[nameof(EditorConfig.ToneMappingMode)] = saved;
        UIToneMappingOperator other = expected == UIToneMappingOperator.Reinhard
            ? UIToneMappingOperator.ACES
            : UIToneMappingOperator.Reinhard;
        var restored = new EditorConfig { ToneMappingMode = { Value = other } };

        CoreSerializer.PopulateFromJsonObject(restored, json);

        Assert.That(restored.ToneMappingMode.Value, Is.EqualTo(expected));
    }

    [Test]
    public void None_IsSavedAsFive()
    {
        var config = new EditorConfig { ToneMappingMode = { Value = UIToneMappingOperator.None } };

        JsonObject json = CoreSerializer.SerializeToJsonObject(config);

        Assert.That(json[nameof(EditorConfig.ToneMappingMode)]!.GetValue<int>(), Is.EqualTo(5));
    }

    // The settings page lists Auto first and the other modes in the order they had before it, which differs from the
    // numbers settings files hold.
    [TestCase(0, UIToneMappingOperator.Auto)]
    [TestCase(1, UIToneMappingOperator.None)]
    [TestCase(2, UIToneMappingOperator.Reinhard)]
    [TestCase(3, UIToneMappingOperator.ACES)]
    [TestCase(4, UIToneMappingOperator.Hable)]
    [TestCase(5, UIToneMappingOperator.FitToDisplay)]
    public void Index_MapsToTheListedMode(int index, UIToneMappingOperator mode)
    {
        UIToneMappingOperator other = mode == UIToneMappingOperator.Hable
            ? UIToneMappingOperator.ACES
            : UIToneMappingOperator.Hable;
        var config = new EditorConfig { ToneMappingMode = { Value = other } };

        config.SetToneMappingModeFromIndex(index);

        Assert.Multiple(() =>
        {
            Assert.That(config.ToneMappingMode.Value, Is.EqualTo(mode));
            Assert.That(EditorConfig.GetToneMappingModeIndex(mode), Is.EqualTo(index));
        });
    }

    // A ComboBox with no selection reports SelectedIndex -1.
    [TestCase(-1)]
    [TestCase(6)]
    public void OutOfRangeIndex_LeavesTheModeUnchanged(int index)
    {
        var config = new EditorConfig { ToneMappingMode = { Value = UIToneMappingOperator.Hable } };

        config.SetToneMappingModeFromIndex(index);

        Assert.That(config.ToneMappingMode.Value, Is.EqualTo(UIToneMappingOperator.Hable));
    }

    // Auto fits the frame to a display that shows HDR and clips it on one that does not, as SDR exports do; a mode
    // chosen outright stays as it is.
    [TestCase(UIToneMappingOperator.Auto, true, UIToneMappingOperator.FitToDisplay)]
    [TestCase(UIToneMappingOperator.Auto, false, UIToneMappingOperator.None)]
    [TestCase(UIToneMappingOperator.None, true, UIToneMappingOperator.None)]
    [TestCase(UIToneMappingOperator.FitToDisplay, false, UIToneMappingOperator.FitToDisplay)]
    [TestCase(UIToneMappingOperator.Reinhard, true, UIToneMappingOperator.Reinhard)]
    public void Resolve_SettlesAutoByWhetherThePreviewShowsHdr(
        UIToneMappingOperator mode, bool showsHdr, UIToneMappingOperator expected)
    {
        Assert.That(mode.Resolve(showsHdr), Is.EqualTo(expected));
    }
}
