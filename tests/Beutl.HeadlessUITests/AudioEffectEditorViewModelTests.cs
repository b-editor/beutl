using Avalonia.Headless.NUnit;
using Beutl.Api.Services;
using Beutl.Audio;
using Beutl.Audio.Effects;
using Beutl.Extensibility;
using Beutl.PropertyAdapters;
using Beutl.ViewModels.Editors;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class AudioEffectEditorViewModelTests
{
    [AvaloniaTest]
    public void Dispose_DisposesTheExpandedPropertyEditors()
    {
        var sound = new SourceSound();
        sound.Effect.CurrentValue = new DelayEffect();
        AudioEffectEditorViewModel viewModel = CreateExpanded(sound);
        BaseEditorViewModel[] children = [.. viewModel.Properties.Value!.Properties.OfType<BaseEditorViewModel>()];
        Assert.That(children, Is.Not.Empty);

        viewModel.Dispose();

        Assert.That(children.Select(child => child.IsDisposed), Is.All.True);
    }

    [AvaloniaTest]
    public void Dispose_DisposesTheExpandedGroupEditor()
    {
        var sound = new SourceSound();
        sound.Effect.CurrentValue = new AudioEffectGroup();
        AudioEffectEditorViewModel viewModel = CreateExpanded(sound);
        ListEditorViewModel<AudioEffect> group = viewModel.Group.Value!;

        viewModel.Dispose();

        Assert.That(group.IsDisposed, Is.True);
    }

    private static AudioEffectEditorViewModel CreateExpanded(SourceSound sound)
    {
        var viewModel = new AudioEffectEditorViewModel(new EnginePropertyAdapter<AudioEffect?>(sound.Effect, sound));
        viewModel.Accept(new Services(TestShell.Extensions));
        viewModel.IsExpanded.Value = true;
        return viewModel;
    }

    // Supplies the extension provider the expanded editor needs to build its property editors.
    private sealed class Services(ExtensionProvider extensions) : IPropertyEditorContextVisitor, IServiceProvider
    {
        public void Visit(IPropertyEditorContext context)
        {
        }

        public object? GetService(Type serviceType)
            => serviceType == typeof(ExtensionProvider) ? extensions : null;
    }
}
