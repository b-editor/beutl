using Avalonia;
using Beutl.Controls;

namespace Beutl.HeadlessUITests;

// Fixtures using this output substitution must be non-parallelizable.
internal sealed class HapticFeedbackRecorder : IDisposable
{
    private readonly Action<Visual, HapticFeedbackKind, ulong> _previous = NativeHapticFeedback.Performer;

    public HapticFeedbackRecorder()
    {
        NativeHapticFeedback.Performer = (_, kind, _) => Pulses.Add(kind);
    }

    public List<HapticFeedbackKind> Pulses { get; } = [];

    public void Dispose() => NativeHapticFeedback.Performer = _previous;
}
