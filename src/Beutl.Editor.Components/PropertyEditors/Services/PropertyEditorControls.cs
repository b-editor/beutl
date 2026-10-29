using System.Diagnostics.CodeAnalysis;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Platform.Storage;
using Beutl.Api.Services;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Editor.Components.PropertyEditors.Views;
using Beutl.Editor.PropertyEditors;
using Beutl.PropertyAdapters;
using Reactive.Bindings.Extensions;

namespace Beutl.Editor.Components.PropertyEditors.Services;

// Makes the controls of property editors: binds their header and state, lets the context set them up, connects their
// values and fits them to the surface.
internal sealed class PropertyEditorControls : IPropertyEditorHost
{
    private static int s_installed;

    private PropertyEditorControls()
    {
    }

    public static PropertyEditorControls Instance { get; } = new();

    // Registers the built-in editors and makes this the host the editors' controls come from.
    public static void Install()
    {
        if (Interlocked.Exchange(ref s_installed, 1) == 1) return;

        BuiltInPropertyEditors.Register(PropertyEditorExtension.Instance.Registry);
        PropertyEditorHost.Current = Instance;
    }

    public bool TryCreateControl(
        IPropertyEditorContext context, PropertyEditorSurface surface, [NotNullWhen(true)] out Control? control)
    {
        if (context is PropertyEditorGroupContext)
        {
            control = surface switch
            {
                PropertyEditorSurface.Panel => new PropertyEditorGroup(),
                PropertyEditorSurface.Settings => new Views.Settings.PropertyEditorGroup(),
                _ => null,
            };
            return control != null;
        }

        control = null;
        PropertyEditorRegistration? registration = PropertyEditorResolver.RegistrationOf(context) ?? FindFor(context, surface);
        if (registration is null) return false;

        Control? created = ExtensionGuard.Call(registration.Owner, () => registration.CreateControl(context, surface));
        if (created is null) return false;

        // Preparing runs the context's Accept and the control's Connect, which are extension code too.
        control = registration.IsStandard
            ? ExtensionGuard.Call(registration.Owner, () => Prepare(context, created, registration, surface))
            : FitListItem(created, surface);
        return control != null;
    }

    public bool TryCreateDefault(IPropertyAdapter property, [NotNullWhen(true)] out IPropertyEditorContext? context)
    {
        context = PropertyEditorResolver.CreateContext(property, [PropertyEditorExtension.Instance], PropertyEditorSurface.Panel);
        return context != null;
    }

    // For a control the host did not make for the context, such as one a view declares.
    public static void Attach(IPropertyEditorContext context, Control control)
        => Attach(context, control, null);

    private static void Attach(IPropertyEditorContext context, Control control, Action<IPropertyEditorContext, Control>? connect)
    {
        if (control is PropertyEditor editor && context is PropertyEditorViewModel viewModel)
            BindState(editor, viewModel);

        if (control is IPropertyEditorContextVisitor visitor)
            context.Accept(visitor);

        if (connect != null)
            connect(context, control);
        else
            (context as PropertyEditorViewModel)?.TryConnect(control);
    }

    // A context made elsewhere, such as by a test, gets the built-in editor of its property.
    private static PropertyEditorRegistration? FindFor(IPropertyEditorContext context, PropertyEditorSurface surface)
    {
        if (context is not PropertyEditorViewModel { PropertyAdapter: { } property }) return null;

        return PropertyEditorResolver.Candidates(property, [PropertyEditorExtension.Instance], surface).FirstOrDefault();
    }

    private static Control Prepare(
        IPropertyEditorContext context, Control control, PropertyEditorRegistration registration, PropertyEditorSurface surface)
    {
        ApplyFileFilters(context, control);
        Attach(context, control, registration.Connect);
        switch (surface)
        {
            case PropertyEditorSurface.Panel when control is PropertyEditor editor:
                editor.MenuContent = new PropertyEditorMenu();
                break;
            case PropertyEditorSurface.Node:
                control.Margin -= new Thickness(control is PropertyEditor ? 4 : 8, 0);
                break;
            case PropertyEditorSurface.ListItem:
                control = FitListItem(control, surface);
                if (control is PropertyEditor listItem)
                    listItem.EditorStyle = PropertyEditorStyle.ListItem;
                break;
            case PropertyEditorSurface.Settings when control is PropertyEditor settings:
                settings.EditorStyle = PropertyEditorStyle.Settings;
                break;
        }

        if (context is PropertyEditorViewModel { CanSeparateComponents: true } model
            && surface is PropertyEditorSurface.Panel or PropertyEditorSurface.Node)
        {
            var components = new ComponentEditors(model, control);
            model.Own(components);
            control = components;
        }
        return control;
    }

    // A list shows its items in a host with the reorder handle and the delete button, unless the item's control has them.
    private static Control FitListItem(Control control, PropertyEditorSurface surface)
    {
        if (surface != PropertyEditorSurface.ListItem || control is IListItemEditor) return control;

        var host = new ListItemEditorHost();
        host.SetChild(control);
        return host;
    }

    private static void BindState(PropertyEditor editor, PropertyEditorViewModel viewModel)
    {
        editor[!PropertyEditor.IsReadOnlyProperty] = viewModel.CanEdit.Inverse().ToBinding();
        editor.Header = viewModel.Header;
        editor[!PropertyEditor.DescriptionProperty] = viewModel.Description.ToBinding();
        editor[!PropertyEditor.HoverInfoProperty] = viewModel.HoverInfo.ToBinding();
        if (viewModel.PropertyAdapter is IAnimatablePropertyAdapter)
        {
            editor[!PropertyEditor.KeyFrameCountProperty] = viewModel.KeyFrameCount.ToBinding();
            editor[!PropertyEditor.KeyFrameIndexProperty] = viewModel.KeyFrameIndex.ToPropertyBinding(BindingMode.TwoWay);
        }
    }

    private static void ApplyFileFilters(IPropertyEditorContext context, Control control)
    {
        if (context is not PropertyEditorViewModel viewModel) return;

        StorageFileEditor? editor = control as StorageFileEditor
            ?? (control as UserControl)?.FindControl<StorageFileEditor>("FileEditor");
        if (editor == null) return;

        FilePickerFileType[] filters = viewModel.PropertyAdapter.GetAttributes()
            .OfType<FileFilterAttribute>()
            .Select(attribute => new FilePickerFileType(attribute.Name)
            {
                Patterns = attribute.Patterns,
                MimeTypes = attribute.MimeTypes,
                AppleUniformTypeIdentifiers = attribute.AppleUniformTypeIdentifiers
            })
            .ToArray();
        if (filters.Length > 0)
        {
            editor.OpenOptions.FileTypeFilter = filters;
        }
    }
}
