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

## Mandatory rules

1. **Every model mutation that the user can undo must go through `HistoryManager` inside a `RecordingScope`.** Mutating a recorded property outside a scope is the most common source of broken-undo bugs.
2. **No Avalonia / view types here.** ViewModels live in `Beutl.Editor.Components` (or in the app); this project is consumed by them, not the other way around.
3. **`AutoSaveService` runs on a background thread.** Anything it touches needs to be thread-safe with respect to the UI thread or marshalled via the dispatcher inside the consumer.
4. **History entries must be value-equal.** Two equivalent edits should compare equal so the manager can collapse redundant entries. Override `Equals` / `GetHashCode` whenever you introduce a new entry type.

## Tests

Unit tests for this subtree live in `tests/Beutl.UnitTests/Editor*/` (NUnit). When adding a new operation or service, add the test before declaring done — `beutl-reviewer` will flag missing coverage.
