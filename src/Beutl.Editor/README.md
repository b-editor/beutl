# Beutl.Editor contributor guide

This subtree is the **non-UI editor logic** — undo / redo, project packaging, virtualised project root, history transactions. The Avalonia views live in `Beutl.Editor.Components` and `Beutl/`; do **not** add `using Avalonia.*` here.

## What lives here

- `HistoryManager.cs` / `HistoryTransaction.cs` / `HistoryEntry.cs` — undo / redo stack
- `EditSession.cs` — `HistoryManager.BeginEdit` opens one session at a time; a gesture's changes made inside `Enter()` become one entry. Other writers (agent edits, generated results, added elements, restores) go through `RunAfterEditAsync` and run after the session closes
- `Operations/` — `ChangeOperation` commands that history records
- `OperationSequenceGenerator.cs` — monotonically increasing sequence numbers for recorded operations
- `AutoSaveService.cs` — saves after each history commit; called on the UI thread
- `ProjectPackageService.cs` / `ResourceRelocationService.cs` — `.beutl` package import / export, asset relocation
- `Observers/` — turn model changes into `ChangeOperation`s for history
- `VirtualProjectRoot.cs` — in-memory project root used by tests and packaging
- `Services/I*Service.cs` — **editing pipeline** consumed by the Timeline View and ViewModel. The View calls a service instead of writing `Scene` properties and calling `history.Commit` itself. Services own the single-commit boundary per user-visible operation.

## Editing services

Located under `Services/`:

- `ISceneTimeRangeService` — Scene Start / Duration edits. `SetStart` / `SetEnd` are one-shot mutate+commit for keyboard / menu callers; drag callers use `UpdateStartDrag` / `UpdateEndDrag` per pointer frame then call `CommitStartChange` / `CommitEndChange` once on release. No session object; cancellation is the caller re-driving Update with the initial values.
- `IElementMoveService` — single / multi-element move + Alt+drag duplicate. `Move(...)` returns `Moved` / `None`; `DuplicateOrMove(...)` returns `Duplicated`, `FellBackToMove`, or `DuplicateOverlapsSource` and falls back to a plain move when the duplicate cannot be staged.
- `IElementResizeService` — `Resize(scene, IReadOnlyList<ElementResizeRequest>)` writes the final sizes (per element: start, length, zIndex) in one transaction. The View handles per-frame preview via VM reactive properties; the service is invoked once on drag release.
- `IElementClipboardService` — Copy / Cut / Paste. Dispatches on `IClipboardGateway` formats (`Elements`, `Element`, `Files`, `Bitmap`) so `Beutl.Editor` stays Avalonia-free.
- `IElementDuplicateService` — selection duplicate + Alt+drag position duplicate. Wraps the existing `DuplicateHelper`; `DuplicateAtClickedPosition` runs a bounded spiral search (`<= 100_000` steps) so a packed timeline cannot hang the caller.
- `IElementStructureService` — Delete / Split / Group / Ungroup. Structural ops that mutate `Scene.Children` / `Scene.Groups`; the scene's next save writes or deletes the element files.
- `IElementAttributeService` — SetEnabled / SetAccentColor. Single-element property writes; no file I/O. Split from structure service so a plugin replacing one does not inherit the other.
- `IElementNudgeService` — debounced keyboard nudge. The nudge window is an edit session, so a nudge never lands in another feature's entry. `System.Threading.Timer` (not `DispatcherTimer`) keeps it Avalonia-free; `Flush` is wired to `HistoryManager.BeforeMutation` inside `EditViewModel`, so Undo / Redo and the start of another session complete it first.
- `ILayerMoveService` — `ApplyMove(scene, oldLayer, newLayer, directElements)` owns the `Element.ZIndex` writes for the direct and shifted sets and commits one `MoveLayer` entry. Returns a `LayerMovePlan` listing the affected elements; the caller (LayerHeader View) drives matching animations afterward.
- `IClipboardGateway` + `ClipboardEntry` + `BeutlClipboardFormats` — Avalonia-free clipboard abstraction. The concrete `AvaloniaClipboardGateway` lives in `Beutl.Editor.Components/Services/`.

Add new edit-pipeline services here, not in `Beutl.Editor.Components`. The View / ViewModel must reach them through `IEditorContext.GetRequiredService<...>`.

## Mandatory rules

1. **Every undoable model mutation is recorded by the observers (or `Record`) and committed by its owner.** A gesture writes inside `Enter()` of the session `BeginEdit` opened and ends it with `Complete` or `Cancel`; a one-shot edit commits with `Commit` or `ExecuteInTransaction`. Other writers go through `RunAfterEditAsync` and run once the session closes.
2. **State derived from values is recomputed, not recorded.** The `TimeRange` and `ZIndex` that engine objects get from their element and `Connection.Status` are `[NotTracked]`, and an input port writes its connection-driven expression inside the internal `HistoryReplay` scope.
3. **Children with IDs are recorded in the same transaction as the change that creates them.** Model3D meshes, equalizer bands and GroupNode mirror ports are created by their owner's change handler, which skips rebuilding them while `HistoryReplay.IsActive`, because replay restores the recorded children.
4. **No Avalonia / view types here.** ViewModels live in `Beutl.Editor.Components` (or in the app); this project is consumed by them, not the other way around.
5. **`AutoSaveService` runs on the UI thread.** The editor calls it through the dispatcher after each history commit.

## Tests

Unit tests for this subtree live in `tests/Beutl.UnitTests/Editor*/` (NUnit). Add matching coverage whenever a new operation or service is introduced.
