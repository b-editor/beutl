namespace Beutl.Extensibility;

// Register a ThemeDescriptor into ThemeRegistry at Load; an extension ships a theme by overriding
// GetThemeDescriptor with its id, base variant, and optional brush-override ResourceDictionary Uri.
// OnApplied/OnReverted are invoked by the host when this theme becomes active/inactive, so an
// extension can add apply-time side effects (telemetry, resource recomputation), and OnAccentChanged
// when the accent moves under an already-applied theme. Setting the accent is not among them: the
// host owns FluentAvalonia's accent and would overwrite a write made here — declare it via
// ThemeDescriptor.AccentColor, or override SystemAccentColor* keys in ResourceUri for full shade
// control. Reading it is what the context's Accent is for.
public abstract class ThemeExtension : Extension
{
    private ThemeDescriptor? _descriptor;

    // The descriptor registered at Load, or null before Load / after Unload.
    public ThemeDescriptor? Descriptor => _descriptor;

    public abstract ThemeDescriptor GetThemeDescriptor();

    public override void Load()
    {
        // Assigned only once Register accepts it, so a rejected descriptor (e.g. an id the host
        // owns) leaves Descriptor null rather than naming a theme this extension does not have.
        ThemeDescriptor descriptor = GetThemeDescriptor();
        ThemeRegistry.Register(descriptor, this);
        _descriptor = descriptor;
    }

    public override void Unload()
    {
        if (_descriptor != null)
        {
            ThemeRegistry.Unregister(_descriptor);
            _descriptor = null;
        }
    }

    // Called by the host when this theme becomes the active theme. Default is a no-op.
    public virtual void OnApplied(ThemeApplyContext context)
    {
    }

    // Called by the host when the accent resolved for this theme changes while it stays applied —
    // the user enabling, disabling or recoloring the custom accent. Not called on the apply path:
    // OnApplied already carries the accent, so an extension that derives resources from it routes
    // both callbacks into one method rather than duplicating the work. Default is a no-op.
    public virtual void OnAccentChanged(ThemeApplyContext context)
    {
    }

    // Called by the host when this theme is no longer the active theme. Default is a no-op.
    public virtual void OnReverted()
    {
    }
}
