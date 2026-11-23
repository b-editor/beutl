using Avalonia;
using Avalonia.Controls;
using Beutl.Controls;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Media.Source;

namespace Beutl.Editor.Components.PropertyEditors.Views;

public partial class VideoSourceEditor : UserControl
{
    public VideoSourceEditor()
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
        => MediaSourceEditorHelper.UpdateFileTypes(this, FileEditor, message, "Video File", x => x.VideoExtensions());

    private void FileEditorOnValueConfirmed(object? sender, PropertyEditorValueChangedEventArgs e)
    {
        if (DataContext is not VideoSourceEditorViewModel { IsDisposed: false } vm) return;
        if (e.NewValue is not FileInfo fi) return;

        MediaSourceEditorHelper.MatchElementToOriginalDuration(vm, () => vm.SetValue(VideoSource.Open(fi.FullName)));
    }
}
