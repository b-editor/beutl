using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Media.Decoding;
using Beutl.Media.Source;

namespace Beutl.Editor.Components.PropertyEditors.Views;

public partial class VideoSourceEditor : UserControl
{
    public VideoSourceEditor()
    {
        InitializeComponent();
        string[] fileExtensions = DecoderFileExtensions.GetFilePatterns(x => x.VideoExtensions());
        if (fileExtensions.Length == 0)
        {
            message.Text = MessageStrings.NoSupportedExtensionsFound;
            message.IsVisible = true;
        }

        FileEditor.OpenOptions = new FilePickerOpenOptions
        {
            FileTypeFilter = [new FilePickerFileType("Video File") { Patterns = fileExtensions }]
        };
        FileEditor.ValueConfirmed += FileEditorOnValueConfirmed;
    }

    private void FileEditorOnValueConfirmed(object? sender, PropertyEditorValueChangedEventArgs e)
    {
        if (DataContext is not VideoSourceEditorViewModel { IsDisposed: false } vm) return;
        if (e.NewValue is not FileInfo fi) return;

        vm.SetValue(VideoSource.Open(fi.FullName));

        MediaSourceEditorHelper.MatchElementToOriginalDuration(vm);
    }
}
