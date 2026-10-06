using System.Numerics;
using System.Security;
using System.Text.Json;
using System.Text.Json.Nodes;
using Beutl.Editor;
using Beutl.ProjectSystem;
using Beutl.Services;
using Microsoft.Extensions.Logging;

namespace Beutl.ViewModels;

public partial class EditViewModel
{
    private string ViewStateDirectory()
    {
        string directory = Path.GetDirectoryName(Scene.Uri!.LocalPath)!;

        directory = Path.Combine(directory, EditorConstants.BeutlFolder, EditorConstants.ViewStateFolder);
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return directory;
    }

    private void SaveState(bool isExplicitUserSave = false)
    {
        if (_viewStateSaveSuppressed)
        {
            // RestoreState left an unreadable file in place (transient IO failure or a
            // failed quarantine attempt). Writing default state now would clobber the
            // file the user actually cares about — skip until the next launch. The
            // explicit-save path also surfaces a warning so the user knows their Ctrl+S
            // did not write view state (AutoSave / dispose stay silent — by design).
            if (isExplicitUserSave)
            {
                _logger.LogWarning(
                    "Explicit save requested but view state save is suppressed this session ({SceneId}).",
                    SceneId);
                NotificationService.ShowWarning(string.Empty, MessageStrings.ViewStateSaveSuppressed);
            }
            return;
        }

        string viewStateDir = ViewStateDirectory();
        var json = new JsonObject
        {
            ["selected-object"] = _editorSelection.SelectedObject.Value?.Id,
            ["max-layer-count"] = _timelineOptionsProvider.Options.Value.MaxLayerCount,
            ["scale"] = _timelineOptionsProvider.Options.Value.Scale,
            ["offset"] = new JsonObject { ["x"] = _timelineOptionsProvider.Options.Value.Offset.X, ["y"] = _timelineOptionsProvider.Options.Value.Offset.Y, },
            ["bpm-grid"] = new JsonObject
            {
                ["bpm"] = _timelineOptionsProvider.Options.Value.BpmGrid.Bpm,
                ["subdivisions"] = _timelineOptionsProvider.Options.Value.BpmGrid.Subdivisions,
                ["offset"] = _timelineOptionsProvider.Options.Value.BpmGrid.Offset.ToString("c"),
                ["is-enabled"] = _timelineOptionsProvider.Options.Value.BpmGrid.IsEnabled,
            }
        };

        DockHost.WriteToJson(json);

        json["current-time"] = JsonValue.Create(_editorClock.CurrentTime.Value);

        string name = Path.GetFileNameWithoutExtension(Scene.Uri!.LocalPath);
        json.JsonSave(Path.Combine(viewStateDir, $"{name}.config"));
    }

    private void RestoreState()
    {
        string viewStateDir = ViewStateDirectory();
        string name = Path.GetFileNameWithoutExtension(Scene.Uri!.LocalPath);
        string viewStateFile = Path.Combine(viewStateDir, $"{name}.config");

        if (!File.Exists(viewStateFile))
        {
            _logger.LogInformation("No state file found, opening default tabs.");
            SafeOpenDefaultTabs();
            return;
        }

        JsonObject? jsonObject = ReadViewState(viewStateFile);
        if (jsonObject is null)
            return;

        try
        {
            RestoreSelectedObject(jsonObject, viewStateFile);

            TimelineOptions timelineOptions = ReadTimelineOptions(jsonObject);
            _timelineOptionsProvider.Options.Value = timelineOptions;

            DockHost.ReadFromJson(jsonObject);

            if (jsonObject.TryGetPropertyValueAsJsonValue("current-time", out string? currentTimeStr)
                && TimeSpan.TryParse(currentTimeStr, out TimeSpan currentTime))
            {
                _editorClock.CurrentTime.Value = currentTime;
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not OperationCanceledException)
        {
            // The file parsed as JSON but a deeper restore step blew up — treat the
            // file as effectively corrupt so the next AutoSave does not silently
            // overwrite it with the default layout. OOM / cancellation propagate
            // because they signal "abort this restore", not "the file is bad" —
            // quarantining a valid file on those would permanently lose the layout.
            _logger.LogError(ex, "Unexpected error while restoring view state from {ViewStateFile}; quarantining and opening default tabs ({SceneId}).", viewStateFile, SceneId);
            QuarantineCorruptViewState(viewStateFile);
            SafeOpenDefaultTabs();
        }
    }

    // Returns null when the file cannot be used; the failure has then been logged, the file quarantined or
    // view state saving suppressed as the failure requires, and the default tabs opened.
    private JsonObject? ReadViewState(string viewStateFile)
    {
        _logger.LogInformation("Restoring state from {ViewStateFile}.", viewStateFile);
        JsonNode? json;
        try
        {
            using var stream = new FileStream(viewStateFile, FileMode.Open, FileAccess.Read, FileShare.Read);
            json = JsonNode.Parse(stream);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "View state file {ViewStateFile} is malformed; quarantining and opening default tabs ({SceneId}).", viewStateFile, SceneId);
            QuarantineCorruptViewState(viewStateFile);
            SafeOpenDefaultTabs();
            return null;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            // File existed at File.Exists() but vanished before FileStream could open it
            // (TOCTOU — another process or a user cleanup). Nothing to protect, so treat
            // this like the no-state-file branch and let SaveState() proceed normally.
            _logger.LogWarning(ex, "View state file {ViewStateFile} disappeared before it could be read; opening default tabs ({SceneId}).", viewStateFile, SceneId);
            SafeOpenDefaultTabs();
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // IO / permission failure (file lock, antivirus, sharing violation, path-too-long,
            // disk error, etc.). The file may still be valid — leave it in place so the next
            // launch can retry, and suppress SaveState() this session so AutoSave does not
            // overwrite it with the default layout before the user gets a chance to recover.
            _logger.LogError(ex, "Failed to read view state file {ViewStateFile}; opening default tabs and suppressing view state save this session ({SceneId}).", viewStateFile, SceneId);
            _viewStateSaveSuppressed = true;
            SafeOpenDefaultTabs();
            return null;
        }

        if (json is not JsonObject jsonObject)
        {
            // JsonNode.Parse returns C# null for the JSON null literal; report that
            // explicitly and fall back to GetValueKind() for the rest (more informative
            // than the runtime type name).
            _logger.LogWarning(
                "View state root is not a JSON object (was {Kind}) in {ViewStateFile}; opening default tabs ({SceneId}).",
                json is null ? nameof(JsonValueKind.Null) : json.GetValueKind().ToString(),
                viewStateFile,
                SceneId);
            QuarantineCorruptViewState(viewStateFile);
            SafeOpenDefaultTabs();
            return null;
        }

        return jsonObject;
    }

    private void RestoreSelectedObject(JsonNode json, string viewStateFile)
    {
        try
        {
            Guid? id = (Guid?)json["selected-object"];
            if (id.HasValue)
            {
                var searcher = new ObjectSearcher(Scene, o => o is CoreObject obj && obj.Id == id.Value);
                _editorSelection.SelectedObject.Value = searcher.Search() as CoreObject;
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not OperationCanceledException)
        {
            // Selection is non-critical state; let the rest of restore continue.
            // OOM / cancellation propagate so the deeper catch can quarantine.
            _logger.LogWarning(ex, "Could not restore the selected object from {ViewStateFile}; selection cleared ({SceneId}).", viewStateFile, SceneId);
        }
    }

    private static TimelineOptions ReadTimelineOptions(JsonObject jsonObject)
    {
        var timelineOptions = new TimelineOptions();

        if (jsonObject.TryGetPropertyValue("max-layer-count", out JsonNode? maxLayer)
            && maxLayer is JsonValue maxLayerValue
            && maxLayerValue.TryGetValue(out int maxLayerCount))
        {
            timelineOptions = timelineOptions with { MaxLayerCount = maxLayerCount };
        }

        if (jsonObject.TryGetPropertyValue("scale", out JsonNode? scaleNode)
            && scaleNode is JsonValue scaleValue
            && scaleValue.TryGetValue(out float scale))
        {
            timelineOptions = timelineOptions with { Scale = scale };
        }

        if (jsonObject.TryGetPropertyValue("offset", out JsonNode? offsetNode)
            && offsetNode is JsonObject offsetObj
            && offsetObj.TryGetPropertyValueAsJsonValue("x", out float x)
            && offsetObj.TryGetPropertyValueAsJsonValue("y", out float y))
        {
            timelineOptions = timelineOptions with { Offset = new Vector2(x, y) };
        }

        if (jsonObject.TryGetPropertyValue("bpm-grid", out JsonNode? bpmGridNode)
            && bpmGridNode is JsonObject bpmGridObj)
        {
            var bpmGrid = ReadBpmGrid(bpmGridObj);

            timelineOptions = timelineOptions with { BpmGrid = bpmGrid };
        }

        return timelineOptions;
    }

    private static BpmGridOptions ReadBpmGrid(JsonObject bpmGridObj)
    {
        var bpmGrid = new BpmGridOptions();

        if (bpmGridObj.TryGetPropertyValueAsJsonValue("bpm", out double bpm))
            bpmGrid = bpmGrid with { Bpm = bpm };

        if (bpmGridObj.TryGetPropertyValueAsJsonValue("subdivisions", out int subdivisions))
            bpmGrid = bpmGrid with { Subdivisions = subdivisions };

        if (bpmGridObj.TryGetPropertyValueAsJsonValue("offset", out string? bpmOffsetStr)
            && TimeSpan.TryParseExact(bpmOffsetStr, "c", CultureInfo.InvariantCulture, out TimeSpan bpmOffset))
            bpmGrid = bpmGrid with { Offset = bpmOffset };

        if (bpmGridObj.TryGetPropertyValueAsJsonValue("is-enabled", out bool isEnabled))
            bpmGrid = bpmGrid with { IsEnabled = isEnabled };

        return bpmGrid;
    }

    private void SafeOpenDefaultTabs()
    {
        // OpenDefaultTabs runs arbitrary tool-extension code, so swallow recoverable
        // exceptions here — the scene must remain openable even when the
        // default-layout fallback itself fails. OOM / cancellation propagate.
        try
        {
            DockHost.OpenDefaultTabs();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to open default tabs ({SceneId}).", SceneId);
            NotificationService.ShowError(string.Empty, MessageStrings.DefaultTabsOpenFailed);
        }
    }

    private void QuarantineCorruptViewState(string viewStateFile)
    {
        // Move the unreadable file aside so subsequent SaveState() calls (AutoSave or
        // dispose) do not overwrite it with the default layout and erase the user's
        // customizations. The 8-hex-char random suffix (~4 billion variants per second)
        // prevents collisions between concurrent restores or other Beutl instances
        // that hit corruption with the same one-second timestamp.
        try
        {
            string suffix = Guid.NewGuid().ToString("N")[..8];
            string quarantined = $"{viewStateFile}.corrupt-{DateTime.UtcNow:yyyyMMddHHmmss}-{suffix}";
            File.Move(viewStateFile, quarantined);
            _logger.LogInformation("Moved unreadable view state to {QuarantinedFile} ({SceneId}).", quarantined, SceneId);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            // The file (or its directory) vanished between the parse failure and the
            // move — nothing left to protect, so do NOT suppress SaveState(); a later
            // save can write fresh state to the now-empty path normally.
            _logger.LogWarning(ex, "View state file {ViewStateFile} disappeared before it could be quarantined ({SceneId}).", viewStateFile, SceneId);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            // Expected move failures (sharing violation, permission denied, etc.) where
            // the corrupt file likely still occupies the original path; suppress
            // SaveState() so it does not get overwritten before a developer can recover
            // the original for diagnostics.
            _logger.LogWarning(ex, "Failed to quarantine view state file {ViewStateFile}; suppressing view state save this session ({SceneId}).", viewStateFile, SceneId);
            _viewStateSaveSuppressed = true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not OperationCanceledException)
        {
            // Belt-and-suspenders: any other File.Move failure (NotSupportedException,
            // ArgumentException, etc.) must still leave us in a state where AutoSave
            // cannot overwrite the file we tried to protect.
            _logger.LogError(ex, "Unexpected failure quarantining view state file {ViewStateFile}; suppressing view state save this session ({SceneId}).", viewStateFile, SceneId);
            _viewStateSaveSuppressed = true;
        }
    }
}
