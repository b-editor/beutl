using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Beutl.Configuration;
using Beutl.Controls;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor.Components.ElementPropertyTab.ViewModels;
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

    public static void MatchElementToOriginalDuration(PropertyEditorViewModel vm, Action? replaceSource = null)
    {
        // The tab can rebuild its property editors and dispose this one while it commits the replacement.
        var multiple = vm.GetService<MultiElementEditSession>();
        var element = vm.GetService<Element>();
        var scene = vm.GetService<Scene>() ?? element?.FindHierarchicalParent<Scene>();
        var resize = vm.GetService<IElementResizeService>();
        var config = vm.GetService<GlobalConfiguration>()?.EditorConfig;
        var timeline = vm.GetService<IEditorContext>()?.FindToolTab<TimelineTabViewModel>();
        replaceSource?.Invoke();

        if (multiple != null)
        {
            if (scene != null && resize != null && config != null)
            {
                Element[] elements = [.. multiple.Elements.Where(item => ElementEditability.IsEditable(item, scene))];
                resize.ResizeToOriginalDurations(scene, elements, config.IsRippleEnabled.Value);
            }

            return;
        }

        if (element == null) return;

        // An open timeline also animates the clip to its new length.
        if (timeline?.GetViewModelFor(element) is { } elmViewModel)
        {
            elmViewModel.ChangeToOriginalDuration.Execute();
            return;
        }

        if (scene != null && resize != null && config != null)
            resize.ResizeToOriginalDuration(scene, element, config.IsRippleEnabled.Value);
    }
}
