using Beutl.IO;

namespace Beutl.Editor.Components.MissingMediaTab;

// The editor's part of a media repair: its notification and scan, its suspension and the state a save carries.
internal interface IMediaRepairHost
{
    CancellationToken RepairCancellationToken { get; }

    bool HasMediaRepairs { get; set; }

    // The service of the editor's own scan, so that a repair does not decode again what the scan already decoded.
    MissingMediaService MissingMediaDetector { get; }

    Task<IReadOnlyList<MissingMedia>> FindMissingMediaAsync(CancellationToken token);

    // The scene's media sources with a file URI, each once. The editor's index walks only what changed since it was asked.
    IReadOnlyList<IFileSource> GetMediaSources();

    Task RefreshMissingMediaAsync();

    void DismissMissingMediaNotification();

    IDisposable SuspendEditor();

    // Called while the repair holds the project-file write lease.
    ValueTask<bool> SaveRepairsAsync();

    void ScheduleMediaFingerprints(bool finishSaveSnapshot);

    void ClearRenderCaches();

    // Reads again the fonts bundled where the scene's project (or the scene, without one) keeps them, after a repair
    // copied fonts there or took them back.
    void ReloadBundledFonts();
}
