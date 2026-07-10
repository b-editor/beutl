using Beutl.Configuration;
using Beutl.Editor.Components.PreviewSettingsTab.ViewModels;
using Beutl.Editor.Services;
using Beutl.Extensibility;

using Moq;

namespace Beutl.UnitTests.Editor;

// #5: the Preview Settings tool tab exposes PreviewSourceMode as an int so a ComboBox.SelectedIndex
// (an int) round-trips. Binding SelectedIndex to an enum-typed ReactiveProperty writes the index back
// untranslated, so toggling the combo from the tab never updates the setting.
[TestFixture, NonParallelizable]
public sealed class PreviewSettingsTabPreviewSourceModeTests
{
    private PreviewSourceMode _saved;

    [SetUp]
    public void SetUp() => _saved = GlobalConfiguration.Instance.EditorConfig.PreviewSourceMode;

    [TearDown]
    public void TearDown() => GlobalConfiguration.Instance.EditorConfig.PreviewSourceMode = _saved;

    private static PreviewSettingsTabViewModel CreateViewModel()
    {
        var factory = new Mock<IPropertyEditorFactory>();
        factory.Setup(f => f.CreateEditor(It.IsAny<IPropertyAdapter>())).Returns((IPropertyEditorContext?)null);
        var ctx = new Mock<IEditorContext>();
        ctx.Setup(c => c.GetService(typeof(IPropertyEditorFactory))).Returns(factory.Object);
        return new PreviewSettingsTabViewModel(ctx.Object);
    }

    [Test]
    public void PreviewSourceMode_ReflectsConfigAsInt()
    {
        GlobalConfiguration.Instance.EditorConfig.PreviewSourceMode = PreviewSourceMode.ForceOriginal;

        using PreviewSettingsTabViewModel vm = CreateViewModel();

        Assert.That(vm.PreviewSourceMode.Value, Is.EqualTo((int)PreviewSourceMode.ForceOriginal));
    }

    [Test]
    public void SettingIntValue_UpdatesConfigEnum()
    {
        GlobalConfiguration.Instance.EditorConfig.PreviewSourceMode = PreviewSourceMode.PreferProxy;
        using PreviewSettingsTabViewModel vm = CreateViewModel();

        // The ComboBox writes back the selected index as an int; the enum setting must follow.
        vm.PreviewSourceMode.Value = (int)PreviewSourceMode.ForceOriginal;

        Assert.That(GlobalConfiguration.Instance.EditorConfig.PreviewSourceMode,
            Is.EqualTo(PreviewSourceMode.ForceOriginal));
    }
}
