# Beutl.Editor — local context

This subtree is the **non-UI editor logic** — undo / redo, project packaging, virtualised project root, history transactions. The Avalonia views live in `Beutl.Editor.Components` and `Beutl/`; do **not** add `using Avalonia.*` here.

## What lives here

- `HistoryManager.cs` / `HistoryTransaction.cs` / `HistoryEntry.cs` — undo / redo stack
- `OperationExecutionContext.cs` / `Operations/` — `IOperation` commands that history records
- `OperationSequenceGenerator.cs` — batches operations for atomic commits
- `RecordingScope.cs` — opens / closes a transactional scope; everything that mutates a recorded model has to flow through this
- `AutoSaveService.cs` — periodic save; runs on a background thread
- `ProjectPackageService.cs` / `ResourceRelocationService.cs` — `.beutlpkg` import / export, asset relocation
- `Observers/` / `Infrastructure/` — change-tracking glue between model and view
- `VirtualProjectRoot.cs` — in-memory project root used by tests and packaging
- `Services/I*Service.cs` — **editing pipeline** consumed by the Timeline View and ViewModel. The View calls a service instead of writing `Scene` properties and calling `history.Commit` itself. Services own the single-commit boundary per user-visible operation.

## Editing services

Located under `Services/`:

- `ISceneTimeRangeService` — Scene Start / Duration edits. `SetStart` / `SetEnd` are one-shot mutate+commit for keyboard / menu callers; drag callers use `UpdateStartDrag` / `UpdateEndDrag` per pointer frame then call `CommitStartChange` / `CommitEndChange` once on release. No session object; cancellation is the caller re-driving Update with the initial values.
- `IElementMoveService` — single / multi-element move + Alt+drag duplicate. `Move(...)` returns `Moved` / `None`; `DuplicateOrMove(...)` returns `Duplicated`, `FellBackToMove`, or `DuplicateOverlapsSource` and falls back to a plain move when the duplicate cannot be staged.
- `IElementResizeService` — `Resize(scene, IReadOnlyList<ElementResizeRequest>)` writes the final sizes (per element: start, length, zIndex) in one transaction. The View handles per-frame preview via VM reactive properties; the service is invoked once on drag release.
- `IElementClipboardService` — Copy / Cut / Paste. Dispatches on `IClipboardGateway` formats (`Elements`, `Element`, `Files`, `Bitmap`) so `Beutl.Editor` stays Avalonia-free.
- `IElementDuplicateService` — selection duplicate + Alt+drag position duplicate. Wraps the existing `DuplicateHelper`; `DuplicateAtClickedPosition` runs a bounded spiral search (`<= 100_000` steps) so a packed timeline cannot hang the caller.
- `IElementLifecycleService` — Exclude / Delete / Split / Group / Ungroup / SetEnabled / SetAccentColor.
- `IElementNudgeService` — debounced keyboard nudge. `System.Threading.Timer` (not `DispatcherTimer`) keeps it Avalonia-free; `Flush` is wired to `HistoryManager.BeforeMutation` inside `EditViewModel` so Undo / Redo never absorbs a pending nudge.
- `ILayerMoveService` — `PlanMove` enumerates affected elements, `CommitMove` is a pure commit boundary (the caller still drives ZIndex writes so the LayerHeader ViewModel and element animations stay in sync).
- `IKeyFrameMoveService` — commit boundary for InlineAnimation drag releases. The View mutates `KeyTime` while dragging, then calls `CommitMove`.
- `IClipboardGateway` + `ClipboardEntry` + `BeutlClipboardFormats` — Avalonia-free clipboard abstraction. The concrete `AvaloniaClipboardGateway` lives in `Beutl.Editor.Components/Services/`.

Add new edit-pipeline services here, not in `Beutl.Editor.Components`. The View / ViewModel must reach them through `IEditorContext.GetRequiredService<...>`.

## Mandatory rules

1. **Every model mutation that the user can undo must go through `HistoryManager` inside a `RecordingScope`.** Mutating a recorded property outside a scope is the most common source of broken-undo bugs.
2. **No Avalonia / view types here.** ViewModels live in `Beutl.Editor.Components` (or in the app); this project is consumed by them, not the other way around.
3. **`AutoSaveService` runs on a background thread.** Anything it touches needs to be thread-safe with respect to the UI thread or marshalled via the dispatcher inside the consumer.
4. **History entries must be value-equal.** Two equivalent edits should compare equal so the manager can collapse redundant entries. Override `Equals` / `GetHashCode` whenever you introduce a new entry type.

## Tests

Unit tests for this subtree live in `tests/Beutl.UnitTests/Editor*/` (NUnit). When adding a new operation or service, add the test before declaring done — `beutl-reviewer` will flag missing coverage.
