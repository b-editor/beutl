namespace Beutl.Graphics.Rendering;

internal enum NodeRecordingTransactionState : byte
{
    Active,
    Committed,
    RolledBack,
}
