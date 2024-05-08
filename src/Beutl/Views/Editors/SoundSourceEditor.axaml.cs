using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Beutl.Controls.PropertyEditors;
using Beutl.Media.Decoding;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.ViewModels;
using Beutl.ViewModels.Editors;

using Microsoft.Extensions.DependencyInjection;

namespace Beutl.Views.Editors;

public partial class SoundSourceEditor : UserControl
{
    public SoundSourceEditor()
    {
        InitializeComponent();
        string[] fileExtensions = DecoderRegistry.EnumerateDecoder()
            .SelectMany(x => x.AudioExtensions().Concat(x.VideoExtensions()))
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

        FileEditor.OpenOptions = new FilePickerOpenOptions { FileTypeFilter = new[] { new FilePickerFileType("Audio File") { Patterns = fileExtensions } } };
        FileEditor.ValueConfirmed += FileEditorOnValueConfirmed;
    }

    private void FileEditorOnValueConfirmed(object? sender, PropertyEditorValueChangedEventArgs e)
    {
        if (DataContext is not SoundSourceEditorViewModel vm) return;
        if (e.NewValue is not FileInfo fi) return;

        // 音声ファイルを開く
        if (!SoundSource.TryOpen(fi.FullName, out SoundSource? soundSource)) return;

        ISoundSource? oldValue = vm.WrappedProperty.GetValue();
        vm.SetValueAndDispose(oldValue, soundSource);

        // 動画の長さに要素の長さを合わせる
        if (vm.GetService<Element>() is not { } element) return;
        TimelineViewModel? timeline = vm.GetService<EditViewModel>()?.FindToolTab<TimelineViewModel>();
        ElementViewModel? elmViewModel = timeline?.GetViewModelFor(element);

        elmViewModel?.ChangeToOriginalLength.Execute();
    }
}
