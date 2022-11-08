namespace Beutl.Configuration;

public abstract class ConfigurationBase : CoreObject
{
    public event EventHandler? ConfigurationChanged;

    protected void OnChanged()
    {
        ConfigurationChanged?.Invoke(this, EventArgs.Empty);
    }
}
