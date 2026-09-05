namespace Beutl.Graphics.Rendering.Requests;

internal enum NodeRecordingTransactionState : byte
{
    Active,
    Committed,
    RolledBack,
}
