using Beutl.Configuration;

namespace Beutl.Testing.Headless;

// Turns off the pointer lock of property drags, and puts the setting back when disposed. A headless move never moves the
// real pointer, which the lock reads on macOS and warps on Windows, and the lock would hide the developer's cursor while a
// test drags. Linux has no lock, so a test that leaves it on can pass there and fail on a Mac.
public sealed class PointerLockScope : IDisposable
{
    private readonly bool _previous;

    private PointerLockScope(bool previous) => _previous = previous;

    public static PointerLockScope Off()
    {
        EditorConfig config = GlobalConfiguration.Instance.EditorConfig;
        var scope = new PointerLockScope(config.EnablePointerLockInProperty.Value);
        config.EnablePointerLockInProperty.Value = false;
        return scope;
    }

    public void Dispose() => GlobalConfiguration.Instance.EditorConfig.EnablePointerLockInProperty.Value = _previous;
}
