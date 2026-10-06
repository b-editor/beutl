namespace Beutl.Extensibility;

// UIの拡張機能の基本クラス
public abstract class ViewExtension : Extension
{
    /// <summary>
    /// Gets the commands registered by this extension. Commands with
    /// <see cref="ContextCommandScope.Extension"/> are handled by this extension implementing
    /// <see cref="IContextCommandHandler"/> and are available in the palette without an open tab.
    /// Other commands are handled by an open editor or tool context.
    /// </summary>
    public virtual IEnumerable<ContextCommandDefinition> ContextCommands => [];
}
