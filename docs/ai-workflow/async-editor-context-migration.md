# Async editor and tool context migration

The editor host now waits for context teardown before it replaces an editor,
applies a dock layout, or releases scene resources. This intentionally changes
the public extension contract in `Beutl.Extensibility`.

## Tool contexts

`IToolContext` implements `IAsyncDisposable` instead of `IDisposable`. Replace
`Dispose()` with `ValueTask DisposeAsync()` and put every subscription, native
resource, and in-flight operation behind that one completion boundary. A host
call to `OpenToolTabAsync` consumes a fresh supplied context even when it returns
`false`; a context already claimed by another host remains with its owner. In either case, the
caller must not dispose or reuse it afterward.

`CloseToolTabAsync` normally completes after the target tool is disposed. When
it is called from inside an `IToolContext.DisposeAsync` callback, the host only
schedules the sibling close and returns to avoid mutual-disposal cycles. The
enclosing layout/editor teardown still joins all scheduled tool disposals before
it releases editor resources.

Independent tool hosts must own a stable `ToolContextHostToken`. Call
`TryAcquireContext` before publishing a tool and retain the returned
`ToolContextOwnershipLease` until the tool has been unpublished and its asynchronous teardown has
completed. Claims are process-wide: an open request for a tool already owned by another editor
returns `false` without disposing the owner's live instance, and a foreign close request is a
no-op. A rejected fresh context is still consumed and fully disposed before `OpenToolTabAsync`
returns.

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
completion semantics. `EditorExtension.CreateContextAsync` must retain the close capability supplied
by `IEditorContextServices` and expose it through the required
`IEditorContext.CloseService` property, either directly or through a
context-specific wrapper. Request closure instead:

```csharp
EditorContextCloseRequest request = CloseService.RequestClose(this);
// Return from the callback. Observe request.Completion afterward if needed.
```

`EditorExtension.TryCreateContext` has been replaced by
`ValueTask<IEditorContext?> CreateContextAsync(...)`. Return a newly created context to transfer
ownership, or await cleanup of every partial resource before returning `null`. Opening a core
object is correspondingly awaitable through `EditorService.ActivateTabItemAsync(CoreObject)`.

`CreateContextAsync`, publication observers, and disposal callbacks must not synchronously start and
wait for a project or editor lifecycle operation on the same or another thread. Queue the operation
so it begins only after the callback returns. The host rejects detected causal reentry, but this is
diagnostic protection rather than the contract: manually suppressing execution context or blocking
through a dispatcher can hide causality and must not be used to bypass the rule. Independent
shutdown and reconciliation remain valid and drain in-flight admitted work.

`ToolTabExtension` callbacks receive only `IEditorContext`; use its required
retained close capability:

```csharp
EditorContextCloseRequest request = editorContext.CloseService.RequestClose(editorContext);
```

`IEditorContext.CloseService` is the canonical close-capability access path. The
`IServiceProvider` surface remains available for other editor-scoped services,
but contexts must not expose a second `IEditorContextCloseService` lookup through
`GetService`.

A non-null `CreateContextAsync` result transfers a new context to the host. The
host disposes that context exactly once even when a subsequent attachment or
publication step fails. A failed creation returns `null` only after the extension has finished
asynchronous cleanup of partial state. A successful
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

`MainViewModel` now implements `IAsyncDisposable`; hosts that own the application composition root
should await `DisposeAsync`. The inherited synchronous `Dispose` starts the same idempotent terminal
task for UI lifetime callbacks, but does not provide a completion boundary by itself.

The host-owned editor collections are now read-only to consumers:

- `EditorTabItem.Context` is `IReadOnlyReactiveProperty<IEditorContext?>`; it is `null` while
  replacement or terminal disposal is in progress, so callers must use a null-safe fallback. The
  returned object is a read-only projection and cannot be cast back to `IReactiveProperty`.
- `EditorTabItem.IsSelected` and `EditorService.SelectedTabItem` are read-only reactive
  projections. Request selection through `EditorService.ActivateTabItem(EditorTabItem)`, which
  rejects tabs that are not currently owned and published by that host. The projections cannot be
  cast back to `IReactiveProperty`.
- `EditorService.TabItems` is an `ICoreReadOnlyList<EditorTabItem>` facade that forwards collection
  notifications without exposing the mutable `ICoreList` implementation.

Use `EditorService.ReplaceContextAsync(tab, extension)` to replace an editor
context. The host creates the context with its own services, validates the tab
owner and current context, serializes replacement with tab close, and owns every
successful factory result. The operation returns `EditorContextReplacementStatus`;
callers never dispose a context returned by this host-mediated overload. A tab
that is unowned, still being attached, or belongs to another host returns
`NotOwned` without changing either tab or registry. `EditorTabItem`'s raw
context overload is host-internal. Add and remove tabs through `EditorService`;
the returned collection has no mutable implementation to cast back to.
`EditorTabItem` construction is also host-internal; external editor hosts should define their
own tab model and claim each context with `EditorContextHostToken` before publishing it.
