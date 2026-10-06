using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Beutl.Controls.PropertyEditors;
using Beutl.Media.Decoding;
using Beutl.Media.Source;
using Beutl.ViewModels.Editors;

namespace Beutl.Views.Editors;

public partial class SoundSourceEditor : UserControl
{
    public SoundSourceEditor()
    {
        InitializeComponent();
        string[] fileExtensions = DecoderFileExtensions.GetFilePatterns(
            x => x.AudioExtensions().Concat(x.VideoExtensions()));
        if (fileExtensions.Length == 0)
        {
            message.Text = MessageStrings.NoSupportedExtensionsFound;
            message.IsVisible = true;
        }

        FileEditor.OpenOptions = new FilePickerOpenOptions
        {
            FileTypeFilter =
            [
                new FilePickerFileType("Audio File") { Patterns = fileExtensions }
            ]
        };
        FileEditor.ValueConfirmed += FileEditorOnValueConfirmed;
    }

    private void FileEditorOnValueConfirmed(object? sender, PropertyEditorValueChangedEventArgs e)
    {
        if (DataContext is not SoundSourceEditorViewModel { IsDisposed: false } vm) return;
        if (e.NewValue is not FileInfo fi) return;

        vm.SetValue(SoundSource.Open(fi.FullName));

        MediaSourceEditorHelper.MatchElementToOriginalDuration(vm);
    }
}
