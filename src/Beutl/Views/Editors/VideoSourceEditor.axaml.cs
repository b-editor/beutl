using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Media.Decoding;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.ViewModels;
using Beutl.ViewModels.Editors;
using Microsoft.Extensions.DependencyInjection;

namespace Beutl.Views.Editors;

public partial class VideoSourceEditor : UserControl
{
    public VideoSourceEditor()
    {
        InitializeComponent();
        string[] fileExtensions = DecoderRegistry.EnumerateDecoder()
            .SelectMany(x => x.VideoExtensions())
            .Distinct()
            .Select(x =>
            {
                if (x.Contains('*', StringComparison.Ordinal))
                {
                    return x;
                }
                else if (x.StartsWith('.'))
                {
                    return $"*{x}";
                }
                else
                {
                    return $"*.{x}";
                }
            })
            .ToArray();
        if (fileExtensions.Length == 0)
        {
            message.Text = Message.No_supported_extensions_were_found;
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

        VideoSource? oldValue = vm.PropertyAdapter.GetValue();
        vm.SetValueAndDispose(oldValue, VideoSource.Open(fi.FullName));

        // 動画の長さに要素の長さを合わせる
        if (vm.GetService<Element>() is not { } element) return;
        TimelineTabViewModel? timeline = vm.GetService<EditViewModel>()?.FindToolTab<TimelineTabViewModel>();
        ElementViewModel? elmViewModel = timeline?.GetViewModelFor(element);

        elmViewModel?.ChangeToOriginalLength.Execute();
    }
}
