namespace Beutl.Editor.Components.MissingMediaTab;

// The editor's part of a media repair: its notification and scan, its suspension and the state a save carries.
internal interface IMediaRepairHost
{
    CancellationToken RepairCancellationToken { get; }

    bool HasMediaRepairs { get; set; }

    Task RefreshMissingMediaAsync();

    void DismissMissingMediaNotification();

    IDisposable SuspendEditor();

    void ScheduleMediaFingerprints(bool finishSaveSnapshot);

    void ClearRenderCaches();
}
