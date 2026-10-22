using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;

namespace Beutl.Extensibility;

// Where a PropertyEditorExtension registers its editors, once, in RegisterEditors.
public sealed class PropertyEditorRegistry
{
    private readonly List<PropertyEditorRegistration> _registrations = [];

    internal PropertyEditorRegistry(PropertyEditorExtension owner)
    {
        Owner = owner;
    }

    internal PropertyEditorExtension Owner { get; }

    internal IReadOnlyList<PropertyEditorRegistration> Registrations => _registrations;

    // An editor with the standard look for TValue properties, whose control edits TEdit values.
    public void RegisterValue<TValue, TEdit>(
        Func<IPropertyAdapter<TValue>, IValueEditorContext<TEdit>> createContext,
        Func<IValueEditorControl<TEdit>> createControl,
        PropertyEditorOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(createContext);
        ArgumentNullException.ThrowIfNull(createControl);
        Add(new PropertyEditorRegistration
        {
            ValueType = typeof(TValue),
            Tier = options?.Match is null ? PropertyEditorTier.Type : PropertyEditorTier.Match,
            Match = MatchFirst<TValue>(options?.Match),
            CreateContext = properties => properties[0] is IPropertyAdapter<TValue> property ? createContext(property) : null,
            CreateControl = (context, surface) => CreateSurfaceControl(options, context, surface) ?? (Control)createControl(),
            Connect = static (context, control) =>
            {
                if (control is IValueEditorControl<TEdit> valueControl && context is IValueEditorContext<TEdit> valueContext)
                    valueControl.Connect(valueContext);
            },
            IsStandard = true,
            HasListItemControl = options?.CreateListItemControl is not null,
            IsPanelOnly = options?.PanelOnly == true,
            Owner = Owner,
        });
    }

    // An editor with its own context and control for TValue properties.
    public void Register<TValue, TContext>(
        Func<IPropertyAdapter<TValue>, TContext> createContext,
        Func<TContext, Control> createControl,
        PropertyEditorOptions? options = null)
        where TContext : IPropertyEditorContext
    {
        ArgumentNullException.ThrowIfNull(createContext);
        ArgumentNullException.ThrowIfNull(createControl);
        Add(new PropertyEditorRegistration
        {
            ValueType = typeof(TValue),
            Tier = options?.Match is null ? PropertyEditorTier.Type : PropertyEditorTier.Match,
            Match = MatchFirst<TValue>(options?.Match),
            CreateContext = properties => properties[0] is IPropertyAdapter<TValue> property ? createContext(property) : null,
            CreateControl = (context, surface) => CreateSurfaceControl(options, context, surface) ?? createControl((TContext)context),
            HasListItemControl = options?.CreateListItemControl is not null,
            IsPanelOnly = options?.PanelOnly == true,
            Owner = Owner,
        });
    }

    // An editor for several properties together; match picks them from the properties not yet taken.
    public void RegisterGroup<TContext>(
        Func<IReadOnlyList<IPropertyAdapter>, IReadOnlyList<IPropertyAdapter>> match,
        Func<IReadOnlyList<IPropertyAdapter>, TContext> createContext,
        Func<TContext, Control> createControl)
        where TContext : IPropertyEditorContext
    {
        ArgumentNullException.ThrowIfNull(match);
        ArgumentNullException.ThrowIfNull(createContext);
        ArgumentNullException.ThrowIfNull(createControl);
        Add(new PropertyEditorRegistration
        {
            Tier = PropertyEditorTier.Match,
            Match = match,
            CreateContext = properties => createContext(properties),
            CreateControl = (context, _) => createControl((TContext)context),
            Owner = Owner,
        });
    }

    // The built-in editor of property, for an editor that wraps it; TryCreateControl makes its control.
    public bool TryCreateDefault(IPropertyAdapter property, [NotNullWhen(true)] out IPropertyEditorContext? context)
    {
        if (PropertyEditorHost.Current is { } host)
            return host.TryCreateDefault(property, out context);

        context = null;
        return false;
    }

    // A new control for context, as the property panel shows it; each call makes another.
    public bool TryCreateControl(IPropertyEditorContext context, [NotNullWhen(true)] out Control? control)
        => PropertyEditorHost.TryCreateControl(context, PropertyEditorSurface.Panel, out control);

    internal void Add(PropertyEditorRegistration registration) => _registrations.Add(registration);

    private static Func<IReadOnlyList<IPropertyAdapter>, IReadOnlyList<IPropertyAdapter>>? MatchFirst<TValue>(
        Func<IPropertyAdapter, bool>? match)
    {
        if (match is null) return null;

        return properties =>
        {
            foreach (IPropertyAdapter property in properties)
            {
                if (property is IPropertyAdapter<TValue> && match(property))
                    return [property];
            }

            return [];
        };
    }

    private static Control? CreateSurfaceControl(
        PropertyEditorOptions? options, IPropertyEditorContext context, PropertyEditorSurface surface)
    {
        return surface switch
        {
            PropertyEditorSurface.ListItem => options?.CreateListItemControl?.Invoke(context),
            PropertyEditorSurface.Node => options?.CreateNodeControl?.Invoke(context),
            PropertyEditorSurface.Settings => options?.CreateSettingsControl?.Invoke(context),
            _ => null,
        };
    }
}
