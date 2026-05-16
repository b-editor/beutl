using Beutl.Editor.Services;
using Beutl.Extensibility;
using Beutl.ProjectSystem;

namespace Beutl.Editor;

public interface ISceneEditorContext : IEditorContext
{
    Scene Scene { get; }

    HistoryManager HistoryManager { get; }

    IEditorClock Clock { get; }

    IEditorSelection Selection { get; }

    IPreviewPlayer Player { get; }

    IElementAdder ElementAdder { get; }

    IBufferStatus BufferStatus { get; }

    ITimelineOptionsProvider TimelineOptions { get; }
}
