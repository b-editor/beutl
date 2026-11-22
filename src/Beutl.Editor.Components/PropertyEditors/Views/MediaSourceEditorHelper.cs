using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Beutl.Configuration;
using Beutl.Controls;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.PropertyEditors;
using Beutl.Editor.Services;
using Beutl.Media.Decoding;
using Beutl.ProjectSystem;
using Microsoft.Extensions.DependencyInjection;
using ExtensionGuard = Beutl.Api.Services.ExtensionGuard;

namespace Beutl.Editor.Components.PropertyEditors.Views;

internal static class MediaSourceEditorHelper
{
    // The picker offers what the editor's decoders open, else the decoders its window passes down.
    public static void UpdateFileTypes(Control editor, StorageFileEditor fileEditor, TextBlock message, string fileTypeName,
        Func<IDecoderInfo, IEnumerable<string>> extensions)
    {
        var decoders = ExtensionGuard.FindService<DecoderRegistry>(editor.DataContext) ?? ControlDecoders.GetDecoders(editor);
        string[] fileExtensions = decoders?.FileExtensions.GetFilePatterns(extensions) ?? [];
        message.IsVisible = fileExtensions.Length == 0;
        message.Text = MessageStrings.NoSupportedExtensionsFound;

        fileEditor.OpenOptions = new FilePickerOpenOptions
        {
            FileTypeFilter = [new FilePickerFileType(fileTypeName) { Patterns = fileExtensions }]
        };
    }

    public static void MatchElementToOriginalDuration(PropertyEditorViewModel vm)
    {
        if (vm.GetService<Element>() is not { } element) return;

        // An open timeline also animates the clip to its new length.
        TimelineTabViewModel? timeline = vm.GetService<IEditorContext>()?.FindToolTab<TimelineTabViewModel>();
        if (timeline?.GetViewModelFor(element) is { } elmViewModel)
        {
            elmViewModel.ChangeToOriginalDuration.Execute();
            return;
        }

        if (element.FindHierarchicalParent<Scene>() is { } scene
            && vm.GetService<IElementResizeService>() is { } resizeService)
        {
            resizeService.ResizeToOriginalDuration(
                scene, element, vm.GetRequiredService<GlobalConfiguration>().EditorConfig.IsRippleEnabled.Value);
        }
    }
}
