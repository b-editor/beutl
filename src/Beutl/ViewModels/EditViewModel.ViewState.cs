using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Beutl.Editor;
using Beutl.JsonConverters;
using Beutl.ProjectSystem;
using Beutl.Services;
using Microsoft.Extensions.Logging;

namespace Beutl.ViewModels;

public partial class EditViewModel
{
    private string ViewStateDirectory()
    {
        return EditorConstants.GetViewStateDirectory(Scene.Uri!);
    }

    private void SaveState(bool isExplicitUserSave = false)
    {
        if (_viewStateSaveSuppressed)
        {
            // RestoreState could not read the file, which may still hold the layout, so skip until the next
            // launch rather than replace it. An explicit save warns that it did not write the view state.
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
            ["selected-object"] = _editorSelection.SelectedObject.Value?.Id.Value,
            ["max-layer-count"] = _timelineOptionsProvider.Options.Value.MaxLayerCount,
            ["scale"] = _timelineOptionsProvider.Options.Value.Scale,
            ["offset"] = new JsonObject { ["x"] = _timelineOptionsProvider.Options.Value.Offset.X, ["y"] = _timelineOptionsProvider.Options.Value.Offset.Y, },
            ["bpm-grid"] = new JsonObject
            {
                ["bpm"] = _timelineOptionsProvider.Options.Value.BpmGrid.Bpm,
                ["subdivisions"] = _timelineOptionsProvider.Options.Value.BpmGrid.Subdivisions,
                ["offset"] = _timelineOptionsProvider.Options.Value.BpmGrid.Offset.ToTimeSpan().ToString("c"),
                ["is-enabled"] = _timelineOptionsProvider.Options.Value.BpmGrid.IsEnabled,
            }
        };

        DockHost.WriteToJson(json);

        json["current-time"] = JsonValue.Create(_editorClock.CurrentTime.Value.ToTimeSpan());

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

        _logger.LogInformation("Restoring state from {ViewStateFile}.", viewStateFile);
        bool parsed = false;
        try
        {
            JsonObject jsonObject;
            using (var stream = new FileStream(viewStateFile, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                jsonObject = JsonNode.Parse(stream) as JsonObject
                    ?? throw new JsonException("The view state root is not a JSON object.");
            }

            parsed = true;
            TimelineOptions timelineOptions = ReadTimelineOptions(jsonObject, Player.GetFrameRate());
            _timelineOptionsProvider.Options.Value = timelineOptions;

            DockHost.ReadFromJson(jsonObject);

            // After the tabs, so the restored selection opens in the property tabs, behind the tabs the layout
            // saved in front.
            KeepingFrontTabs(() => RestoreSelectedObject(jsonObject, viewStateFile));

            if (jsonObject.TryGetPropertyValueAsJsonValue("current-time", out string? currentTimeStr)
                && TimeSpan.TryParse(currentTimeStr, out TimeSpan currentTime))
            {
                // The view state is a file of the project, read on its frame grid.
                _editorClock.Seek(MediaTimeJsonConverter.FromFileTicks(currentTime.Ticks, Player.GetFrameRate()));
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // A file that could not be read, by a lock for example, may still hold the layout, so this session
            // does not replace it. A file that vanished holds nothing.
            _viewStateSaveSuppressed = !parsed
                && ex is UnauthorizedAccessException or (IOException and not (FileNotFoundException or DirectoryNotFoundException));
            _logger.LogError(ex, "Could not restore view state from {ViewStateFile}; opening default tabs ({SceneId}).", viewStateFile, SceneId);
            SafeOpenDefaultTabs();
        }
    }

    private void RestoreSelectedObject(JsonNode json, string viewStateFile)
    {
        try
        {
            Guid? id = (Guid?)json["selected-object"];
            if (id.HasValue)
            {
                var searcher = new ObjectSearcher(Scene, o => o is CoreObject obj && obj.Id.Value == id.Value);
                _editorSelection.Select(searcher.Search() as CoreObject);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not OperationCanceledException)
        {
            // Selection is non-critical state; let the rest of restore continue.
            // OOM / cancellation propagate and abort the restore.
            _logger.LogWarning(ex, "Could not restore the selected object from {ViewStateFile}; selection cleared ({SceneId}).", viewStateFile, SceneId);
        }
    }

    private static TimelineOptions ReadTimelineOptions(JsonObject jsonObject, Rational frameRate)
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
            var bpmGrid = ReadBpmGrid(bpmGridObj, frameRate);

            timelineOptions = timelineOptions with { BpmGrid = bpmGrid };
        }

        return timelineOptions;
    }

    private static BpmGridOptions ReadBpmGrid(JsonObject bpmGridObj, Rational frameRate)
    {
        var bpmGrid = new BpmGridOptions();

        if (bpmGridObj.TryGetPropertyValueAsJsonValue("bpm", out double bpm))
            bpmGrid = bpmGrid with { Bpm = bpm };

        if (bpmGridObj.TryGetPropertyValueAsJsonValue("subdivisions", out int subdivisions))
            bpmGrid = bpmGrid with { Subdivisions = subdivisions };

        if (bpmGridObj.TryGetPropertyValueAsJsonValue("offset", out string? bpmOffsetStr)
            && TimeSpan.TryParseExact(bpmOffsetStr, "c", CultureInfo.InvariantCulture, out TimeSpan bpmOffset))
            bpmGrid = bpmGrid with { Offset = MediaTimeJsonConverter.FromFileTicks(bpmOffset.Ticks, frameRate) };

        if (bpmGridObj.TryGetPropertyValueAsJsonValue("is-enabled", out bool isEnabled))
            bpmGrid = bpmGrid with { IsEnabled = isEnabled };

        return bpmGrid;
    }

    private void SafeOpenDefaultTabs()
    {
        // OpenDefaultTabs runs arbitrary tool-extension code, so swallow recoverable
        // exceptions here — the scene must remain openable even when the
        // default-layout fallback itself fails. OOM propagates.
        try
        {
            DockHost.OpenDefaultTabs();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.LogError(ex, "Failed to open default tabs ({SceneId}).", SceneId);
            NotificationService.ShowError(string.Empty, MessageStrings.DefaultTabsOpenFailed);
        }
    }
}
