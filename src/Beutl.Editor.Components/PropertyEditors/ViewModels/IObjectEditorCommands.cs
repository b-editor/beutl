using Avalonia.Input;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.PropertyEditors;
using Beutl.Editor.Services;
using Beutl.Serialization;
using Reactive.Bindings;

namespace Beutl.Editor.Components.PropertyEditors.ViewModels;

// The copy, paste and template commands of an object value's editor; its menus call them through ObjectEditorCommands.
internal interface IObjectEditorCommands
{
    bool IsDisposed { get; }

    IPropertyAdapter PropertyAdapter { get; }

    IReadOnlyReactiveProperty<bool> CanCopy { get; }

    // Set when a menu opens, by RefreshCanPasteAsync.
    ReactivePropertySlim<bool> CanPaste { get; }

    IReadOnlyReactiveProperty<bool> CanSaveAsTemplate => ObjectEditorCommands.Never;

    DataFormat<string>? PasteFormat { get; }

    Type? TemplateBaseType => null;

    bool IsTemplateGroup => false;

    ICoreSerializable? GetCopyTarget();

    ICoreSerializable? GetTemplateTarget() => null;

    bool TryPasteJson(string json);

    bool ApplyTemplate(ObjectTemplateItem template) => false;
}

internal static class ObjectEditorCommands
{
    public static readonly IReadOnlyReactiveProperty<bool> Never = new ReactivePropertySlim<bool>(false);

    public static async ValueTask<bool> CopyAsync(this IObjectEditorCommands editor)
    {
        if (editor.PasteFormat is not { } format) return false;
        if (editor.GetCopyTarget() is not { } obj) return false;
        return await CoreObjectClipboard.CopyAsync(obj, format);
    }

    public static async ValueTask<bool> PasteAsync(this IObjectEditorCommands editor)
    {
        // TryPasteJson checks the edit it makes; a paste over content that could not be loaded repairs it.
        if (!((PropertyEditorViewModel)editor).CanReplaceContent) return false;
        if (editor.PasteFormat is not { } format) return false;
        var clipboard = ClipboardHelper.GetClipboard();
        if (clipboard == null) return false;
        string? json = await CoreObjectClipboard.TryGetJsonAsync(clipboard, format);
        return json != null && editor.TryPasteJson(json);
    }

    public static async ValueTask RefreshCanPasteAsync(this IObjectEditorCommands editor)
    {
        if (editor.PasteFormat is not { } format)
        {
            editor.CanPaste.Value = false;
            return;
        }
        var clipboard = ClipboardHelper.GetClipboard();
        if (clipboard == null)
        {
            editor.CanPaste.Value = false;
            return;
        }
        try
        {
            string? json = await CoreObjectClipboard.TryGetJsonAsync(clipboard, format);
            editor.CanPaste.Value = json != null;
        }
        catch
        {
            editor.CanPaste.Value = false;
        }
    }

    public static bool IsListItemAdapter(this IObjectEditorCommands editor) => editor.PropertyAdapter is IListItemAccessor;

    public static bool AddTemplateAsListItem(this IObjectEditorCommands editor, ObjectTemplateItem template)
    {
        var viewModel = (PropertyEditorViewModel)editor;
        if (!viewModel.IsElementEditable) return false;
        if (editor.PropertyAdapter is not IListItemAccessor accessor) return false;
        if (template.CreateInstance() is not { } instance) return false;

        try
        {
            int insertIndex = Math.Clamp(accessor.Index, 0, accessor.List.Count);
            accessor.List.Insert(insertIndex, instance);
            viewModel.Commit(CommandNames.ApplyTemplate);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static IEnumerable<ObjectTemplateItem> GetApplicableTemplates(this IObjectEditorCommands editor)
        => editor.TemplateBaseType is { } t ? ObjectTemplateService.Instance.FindByBaseType(t) : [];

    public static string GetTemplateDefaultName(this IObjectEditorCommands editor)
    {
        if (editor.GetTemplateTarget() is { } target)
            return TypeDisplayHelpers.GetLocalizedName(target.GetType());
        return string.Empty;
    }
}
