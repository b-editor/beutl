namespace Beutl.Graphics.Rendering.Requests;

internal static class NestedRenderTargetBindingScope
{
    private static readonly AsyncLocal<Scope?> s_current = new();

    public static void Use(object identity, NestedRenderTargetBinding binding, Action use)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(use);

        Scope? previous = s_current.Value;
        s_current.Value = new Scope(identity, binding, previous);
        try
        {
            use();
        }
        finally
        {
            s_current.Value = previous;
        }
    }

    public static bool TryGet(object identity, out NestedRenderTargetBinding binding)
    {
        ArgumentNullException.ThrowIfNull(identity);
        for (Scope? current = s_current.Value; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current.Identity, identity))
            {
                binding = current.Binding;
                return true;
            }
        }

        binding = null!;
        return false;
    }

    private sealed record Scope(
        object Identity,
        NestedRenderTargetBinding Binding,
        Scope? Parent);
}
