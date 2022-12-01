using Avalonia.Controls;
using Avalonia.Platform.Storage;

using Beutl.Api.Services;
using Beutl.Media.Decoding;
using Beutl.Media.Source;
using Beutl.ViewModels.Editors;

using Microsoft.Extensions.DependencyInjection;

namespace Beutl.Views.Editors;

public partial class SoundSourceEditor : UserControl
{
    public SoundSourceEditor()
    {
        InitializeComponent();
        button.Click += Button_Click;
    }

    private async void Button_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not SoundSourceEditorViewModel vm || VisualRoot is not TopLevel topLevel) return;

        ExtensionProvider provider = ServiceLocator.Current.GetRequiredService<ExtensionProvider>();

        string[] fileExtensions = DecoderRegistry.EnumerateDecoder()
            .SelectMany(x => x.AudioExtensions().Concat(x.VideoExtensions()))
            .Distinct()
            .Select(x =>
            {
                if (x.Contains('*', StringComparison.Ordinal))
                {
                    return x;
                }
                else
                {
                    if (x.StartsWith('.'))
                    {
                        return $"*{x}";
                    }
                    else
                    {
                        return $"*.{x}";
                    }
                }
            })
            .ToArray();

        var options = new FilePickerOpenOptions
        {
            FileTypeFilter = new FilePickerFileType[]
            {
                new FilePickerFileType("Audio File")
                {
                    Patterns = fileExtensions
                }
            }
        };

        IReadOnlyList<IStorageFile> result = await topLevel.StorageProvider.OpenFilePickerAsync(options);
        if (result.Count > 0
            && result[0].TryGetUri(out Uri? uri)
            && uri.IsFile
            && MediaSourceManager.Shared.OpenSoundSource(uri.LocalPath, out ISoundSource? soundSource))
        {
            ISoundSource? oldValue = vm.WrappedProperty.GetValue();
            vm.SetValue(oldValue, soundSource);
            oldValue?.Dispose();
        }
    }
}
