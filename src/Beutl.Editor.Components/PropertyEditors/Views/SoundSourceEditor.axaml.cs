using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Beutl.Controls;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Media.Decoding;
using Beutl.Media.Source;
using ExtensionGuard = Beutl.Api.Services.ExtensionGuard;

namespace Beutl.Editor.Components.PropertyEditors.Views;

public partial class SoundSourceEditor : UserControl
{
    public SoundSourceEditor()
    {
        InitializeComponent();
        UpdateFileTypes();
        FileEditor.ValueConfirmed += FileEditorOnValueConfirmed;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if ((change.Property == DataContextProperty || change.Property == ControlDecoders.DecodersProperty) && FileEditor != null)
            UpdateFileTypes();
    }

    private void UpdateFileTypes()
    {
        var decoders = ExtensionGuard.FindService<DecoderRegistry>(DataContext) ?? ControlDecoders.GetDecoders(this);
        string[] fileExtensions = decoders?.FileExtensions.GetFilePatterns(x => x.AudioExtensions().Concat(x.VideoExtensions())) ?? [];
        message.IsVisible = fileExtensions.Length == 0;
        message.Text = MessageStrings.NoSupportedExtensionsFound;

        FileEditor.OpenOptions = new FilePickerOpenOptions
        {
            FileTypeFilter =
            [
                new FilePickerFileType("Audio File") { Patterns = fileExtensions }
            ]
        };
    }

    private void FileEditorOnValueConfirmed(object? sender, PropertyEditorValueChangedEventArgs e)
    {
        if (DataContext is not SoundSourceEditorViewModel { IsDisposed: false } vm) return;
        if (e.NewValue is not FileInfo fi) return;

        vm.SetValue(SoundSource.Open(fi.FullName));

        MediaSourceEditorHelper.MatchElementToOriginalDuration(vm);
    }
}
