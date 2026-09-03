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

`IEditorContext.CloseService` is the canonical close-capability access path. The
`IServiceProvider` surface remains available for other editor-scoped services,
but contexts must not expose a second `IEditorContextCloseService` lookup through
`GetService`.

A successful `TryCreateContext` transfers a new, non-null context to the host. The
host disposes that context exactly once even when a subsequent attachment or
publication step fails. A failed creation returns `false` with `context == null`
and the extension is responsible for cleaning up partial state. A successful
factory result must be a newly created, unowned context; returning a context that
is already active in a tab violates the ownership contract.

The request distinguishes `Accepted`, `AlreadyClosing`, and `NotOwned`.
`Completion` is the stable terminal task for physical tab removal and context
teardown, including failures.

Every `IEditorContextCloseService` also exposes a required, opaque
`EditorContextHostToken`. The host creates one stable token and contexts must retain the supplied
close capability (or a wrapper that forwards both `RequestClose` and the exact `HostToken`).
`EditorService` rejects initial attachment and replacement when the token belongs to another host;
do not construct a fresh token in a context wrapper.

Independent editor-host implementations must call
`EditorContextHostToken.TryAcquireContext(context, out lease)` before publishing a context and
retain the lease until that context has been unpublished and asynchronously disposed. This atomic
claim lets every host distinguish a new factory result from a context that is already live, even
when the close capability is wrapped. The built-in `EditorService` manages these leases itself.

The built-in `EditViewModel` is now created only by its owning `EditorService` through
`SceneEditorExtension`. Extensions should implement `IEditorContext` and retain the supplied
`IEditorContextServices`; they must not instantiate the built-in view model directly.

## Project shutdown

`ProjectService.CloseProject()` has been replaced by `CloseProjectAsync()`.
Await it before unloading packages, replacing the editor host, or releasing any
resource that an editor context can still reach. Repeated calls join the queued
project transition, including a close that has already cleared `CurrentProject`.

When an editor host starts unregistering, transitions accepted before the fence
finish through that host. New `OpenProject`, `CreateProject`, and `CloseProjectAsync`
operations fail without changing `CurrentProject` until a replacement host has
finished replaying the current project. Callers may retry after host initialization.

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

Use `EditorService.ReplaceContextAsync(tab, extension)` to replace an editor
context. The host creates the context with its own services, validates the tab
owner and current context, serializes replacement with tab close, and owns every
successful factory result. The operation returns `EditorContextReplacementStatus`;
callers never dispose a context returned by this host-mediated overload. A tab
that is unowned, still being attached, or belongs to another host returns
`NotOwned` without changing either tab or registry. `EditorTabItem`'s raw
context overload is host-internal. Add and remove tabs through `EditorService`;
do not cast the read-only collections back to their concrete mutable implementations.
