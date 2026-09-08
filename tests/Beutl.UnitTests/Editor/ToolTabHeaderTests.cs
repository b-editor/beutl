using Beutl.Editor.Components.AudioVisualizerTab.ViewModels;
using Beutl.Editor.Components.ColorScopesTab.ViewModels;
using Beutl.Editor.Components.FileBrowserTab.ViewModels;
using Beutl.Editor.Components.Helpers;
using Beutl.Language;
using Beutl.ProjectSystem;

namespace Beutl.UnitTests.Editor;

[TestFixture]
public class ToolTabHeaderTests
{
    [Test]
    public void Compose_KeepsTheTabNameAloneWhenThereIsNoTarget()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ToolTabHeaderHelper.Compose("Curves", null), Is.EqualTo("Curves"));
            Assert.That(ToolTabHeaderHelper.Compose("Curves", "  "), Is.EqualTo("Curves"));
        });
    }

    [Test]
    public void Compose_JoinsTheTabNameAndTheTarget()
    {
        Assert.That(ToolTabHeaderHelper.Compose("Curves", "Text 1"), Is.EqualTo("Curves - Text 1"));
    }

    [Test]
    public void ElementLabel_PrefersTheElementName()
    {
        var element = new Element { Uri = new Uri(Path.Combine(Path.GetTempPath(), "layer.belm")) };

        Assert.That(ToolTabHeaderHelper.ElementLabel("Title card", element), Is.EqualTo("Title card"));
    }

    [Test]
    public void ElementLabel_FallsBackToTheFileNameForAnUnnamedElement()
    {
        var element = new Element { Uri = new Uri(Path.Combine(Path.GetTempPath(), "layer.belm")) };

        Assert.That(ToolTabHeaderHelper.ElementLabel(string.Empty, element), Is.EqualTo("layer"));
    }

    [Test]
    public void ElementLabel_IsEmptyWithoutAnElement()
    {
        Assert.That(ToolTabHeaderHelper.ElementLabel(null, null), Is.Empty);
    }

    [Test]
    public void FileBrowser_UsesTheToolNameForTheHomeView()
    {
        Assert.That(FileBrowserTabViewModel.CreateHeader(string.Empty), Is.EqualTo(Strings.FileBrowser));
    }

    [Test]
    public void FileBrowser_UsesTheFolderName()
    {
        string path = Path.Combine(Path.GetTempPath(), "proj", "resources");

        Assert.That(FileBrowserTabViewModel.CreateHeader(path), Is.EqualTo("resources"));
    }

    [Test]
    public void FileBrowser_IgnoresATrailingSeparator()
    {
        string path = Path.Combine(Path.GetTempPath(), "proj", "resources") + Path.DirectorySeparatorChar;

        Assert.That(FileBrowserTabViewModel.CreateHeader(path), Is.EqualTo("resources"));
    }

    [Test]
    public void FileBrowser_FallsBackToThePathForAFilesystemRoot()
    {
        string root = Path.GetPathRoot(Path.GetTempPath())!;

        Assert.That(FileBrowserTabViewModel.CreateHeader(root), Is.EqualTo(root));
    }

    [TestCase(ColorScopeType.Waveform)]
    [TestCase(ColorScopeType.Histogram)]
    [TestCase(ColorScopeType.Vectorscope)]
    [TestCase(ColorScopeType.FalseColor)]
    [TestCase(ColorScopeType.Zebra)]
    public void ColorScopes_LocalizesEveryScopeTypeDistinctly(ColorScopeType type)
    {
        string label = ColorScopesTabViewModel.LocalizeScopeType(type);

        Assert.Multiple(() =>
        {
            Assert.That(label, Is.Not.Empty);
            Assert.That(label, Is.Not.EqualTo(Strings.ColorScopes));
        });
    }

    [TestCase(AudioVisualizerMode.Waveform)]
    [TestCase(AudioVisualizerMode.Spectrum)]
    [TestCase(AudioVisualizerMode.Meter)]
    [TestCase(AudioVisualizerMode.Spectrogram)]
    [TestCase(AudioVisualizerMode.PhaseScope)]
    public void AudioVisualizer_LocalizesEveryModeDistinctly(AudioVisualizerMode mode)
    {
        string label = AudioVisualizerTabViewModel.LocalizeMode(mode);

        Assert.Multiple(() =>
        {
            Assert.That(label, Is.Not.Empty);
            Assert.That(label, Is.Not.EqualTo(Strings.AudioVisualizer));
        });
    }

    [Test]
    public void ColorScopes_MapsEveryDefinedScopeTypeToItsOwnLabel()
    {
        string[] labels = Enum.GetValues<ColorScopeType>()
            .Select(ColorScopesTabViewModel.LocalizeScopeType)
            .ToArray();

        Assert.That(labels, Is.Unique);
    }

    [Test]
    public void AudioVisualizer_MapsEveryDefinedModeToItsOwnLabel()
    {
        string[] labels = Enum.GetValues<AudioVisualizerMode>()
            .Select(AudioVisualizerTabViewModel.LocalizeMode)
            .ToArray();

        Assert.That(labels, Is.Unique);
    }
}
