# Async editor and tool context migration

The editor host now waits for context teardown before it replaces an editor,
applies a dock layout, or releases scene resources. This intentionally changes
the public extension contract in `Beutl.Extensibility`.

## Tool contexts

`IToolContext` implements `IAsyncDisposable` instead of `IDisposable`. Replace
`Dispose()` with `ValueTask DisposeAsync()` and put every subscription, native
resource, and in-flight operation behind that one completion boundary. A host
call to `OpenToolTabAsync` consumes the supplied context even when it returns
`false`; an extension must not dispose or reuse it afterward.

`CloseToolTabAsync` normally completes after the target tool is disposed. When
it is called from inside an `IToolContext.DisposeAsync` callback, the host only
schedules the sibling close and returns to avoid mutual-disposal cycles. The
enclosing layout/editor teardown still joins all scheduled tool disposals before
it releases editor resources.

```csharp
public ValueTask DisposeAsync()
{
    _disposables.Dispose();
    return ValueTask.CompletedTask;
}

await editorContext.OpenToolTabAsync(context);
await editorContext.CloseToolTabAsync(context);
```

## Editor contexts

`IEditorContext` no longer implements `IDisposable`. Implement
`IAsyncDisposable.DisposeAsync`, and update `OpenToolTab` / `CloseToolTab` calls
to their asynchronous counterparts. Disposal and context replacement are
idempotent host operations: callers should await them rather than adding a
synchronous wrapper or blocking with `GetAwaiter().GetResult()`.

Host publication and dispatcher callbacks must not synchronously wait for
`DisposeAsync` or `EditorService.CloseTabItem`, because both retain terminal
completion semantics. `TryCreateContext` must retain the close capability supplied
by `IEditorContextServices` and expose it through the required
`IEditorContext.CloseService` property, either directly or through a
context-specific wrapper. Request closure instead:

```csharp
EditorContextCloseRequest request = CloseService.RequestClose(this);
// Return from the callback. Observe request.Completion afterward if needed.
```

`ToolTabExtension` callbacks receive only `IEditorContext`; use its required
retained close capability:

```csharp
EditorContextCloseRequest request = editorContext.CloseService.RequestClose(editorContext);
```

The request distinguishes `Accepted`, `AlreadyClosing`, and `NotOwned`.
`Completion` is the stable terminal task for physical tab removal and context
teardown, including failures.

## Project shutdown

`ProjectService.CloseProject()` has been replaced by `CloseProjectAsync()`.
Await it before unloading packages, replacing the editor host, or releasing any
resource that an editor context can still reach. Repeated calls join the queued
project transition, including a close that has already cleared `CurrentProject`.

`ProjectObservable` is now a post-commit notification stream. Notifications are
ordered and run only after the editor reaches a stable state, but project methods
do not wait for observers to finish. Observers must not synchronously block on a
new project operation; enqueue or await the operation after returning from the
callback. The event payload identifies the historical transition; a later
transition may already be visible through `CurrentProject`. Use
`WaitForPendingProjectChangesAsync()` when code that mutates
`Project.Items` needs an explicit editor-state barrier.

Dock layout application and reset are asynchronous for the same reason. Await
the operation so outgoing tools finish teardown before replacement tools begin
using the editor.

The host-owned editor collections are now read-only to consumers:

- `EditorTabItem.Context` is `IReadOnlyReactiveProperty<IEditorContext?>`; it is `null` while
  replacement or terminal disposal is in progress, so callers must use a null-safe fallback.
- `EditorService.TabItems` is `ICoreReadOnlyList<EditorTabItem>`.

Use `EditorTabItem.ReplaceContextAsync` to replace a context. It serializes
replacement with tab close, awaits the outgoing context, and consumes the new
context if close wins. A context identity already owned by the same or another
tab is rejected before outgoing teardown and remains caller-owned. If outgoing
teardown fails, the tab is terminally removed and the replacement task faults
after cleanup. Add and remove tabs through
`EditorService`; do not cast
the read-only collections back to their concrete mutable implementations.
