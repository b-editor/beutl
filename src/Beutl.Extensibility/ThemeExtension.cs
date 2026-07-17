namespace Beutl.Extensibility;

// Register a ThemeDescriptor into ThemeRegistry at Load; an extension ships a theme by overriding
// GetThemeDescriptor with its id, base variant, and optional brush-override ResourceDictionary Uri.
// OnApplied/OnReverted are invoked by the host when this theme becomes active/inactive, so an
// extension can add apply-time side effects (telemetry, dynamic accent, resource recomputation).
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

    // Called by the host when this theme is no longer the active theme. Default is a no-op.
    public virtual void OnReverted()
    {
    }
}
