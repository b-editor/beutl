# Editor context capabilities

`IEditorContext` provides an editor's document, tool tabs, services, and lifetime.
Implement optional interfaces on that same context to participate in the host's
standard commands:

| Interface | Methods | Result |
| --- | --- | --- |
| `ISavableEditorContext` | `ValueTask<bool> SaveAsync()` | `true` when the document was saved; `false` or an exception signals failure. |
| `IUndoRedoEditorContext` | `ValueTask<bool> UndoAsync()`, `ValueTask<bool> RedoAsync()` | `true` when the requested history operation was applied; `false` otherwise. |

An editor can implement either capability, both, or neither. The host checks the
current tab's context each time it executes a command. It skips contexts without
save support during Save All and project-wide saves; a supported save returning
`false` still counts as a failure and prevents a version-control save snapshot.
Undo and Redo do nothing when the selected context does not support history.

The host invokes these methods on the UI thread and awaits their completion.
Keep document persistence in `SaveAsync`; write admission, save notifications,
and version-control coordination belong to the host. Undo and Redo implementations
must preserve any editor-specific playback or mutation guards.

## Migrating from `IKnownEditorCommands`

The `IKnownEditorCommands` interface and the `IEditorContext.Commands` and
`EditorTabItem.Commands` properties have been removed. This is a source and binary
breaking change; rebuild extensions against the updated SDK.

- Move `OnSave` into the context as `ISavableEditorContext.SaveAsync`.
- Move `OnUndo` and `OnRedo` into the context as
  `IUndoRedoEditorContext.UndoAsync` and `RedoAsync`.
- Remove the `Commands` property and any adapter class that only forwards these
  operations. Contexts that previously returned `null` need no replacement member.
- `OnClose` had no host callers. Use the context's `Dispose`/`DisposeAsync` for
  cleanup; these methods are lifetime hooks and do not provide a close veto.

See `samples/PackageSample/SampleEditorExtension.cs` for a text editor that opts
into saving without implementing history.
