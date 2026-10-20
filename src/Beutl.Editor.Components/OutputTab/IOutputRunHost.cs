namespace Beutl.Editor.Components.OutputTab;

// The workspace an output runs in: it admits the output and shows the running ones in the title bar.
internal interface IOutputRunHost : IOutputOperationLeaseProvider
{
    IDisposable TrackRunningOutput(OutputProfileItem item);
}
