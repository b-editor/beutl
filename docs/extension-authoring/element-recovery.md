# Completing element repairs in plugins

Recovered elements preserve their original sidecar bytes until all recovery blockers
have been repaired or removed. Plugins and custom editors can finish a repair with
`Beutl.Editor.Services.ElementRecoveryService.TryCompleteRepair(element, history)`.

Recovery keeps an element's file only when saving the element would lose part of it:
the file cannot be read, a value failed to load and left nothing that saves it again
(such as a keyframe easing whose type is missing), or the element holds content the
Beutl 1.x migration could not convert (a node tree, or an operator it does not know).
An object whose type is missing, usually because an extension is not installed, does
not protect its element: it loads as a fallback, which saves the JSON it was read from
unchanged, so the rest of the element stays editable and its edits are saved. The
fallback itself is read-only; removing or replacing it is an ordinary edit.

Saving does not write a protected element's edits, so the editor treats it as read-only:
`Scene.IsElementLocked` returns `true` for it, and the built-in editors refuse its edits.
They still allow deleting the element, and removing or replacing an object or value that
holds a blocker, because that is the repair. Custom editors should refuse other edits
while `Scene.IsElementLocked` is `true`, the same way they respect a locked clip.

Use the `HistoryManager` already observing the edited scene. Apply the repair through
the normal public property, keyframe, or collection APIs, call `TryCompleteRepair`,
then commit that same history transaction:

```csharp
keyFrame.Easing = new LinearEasing();
ElementRecoveryService.TryCompleteRepair(element, history);
history.Commit("Repair easing");
```

The service returns `true` when it resumes normal persistence. It returns `false`
when the element is not protected or another blocker remains. It does not commit
history. Undo restores recovery protection and the retained bytes, including nested
sidecars after Save As; redo resumes normal persistence again.

Reference migration rebuilds framework read-only dictionaries, sets, and immutable
collections while preserving their comparison policies and sequence order. Custom
collections and composite expressions with additional state should implement
`Beutl.IReferenceRewritable` to control their own replacement and preserve that state.
This opt-in takes precedence over `IReferenceExpression.Rebind` when an expression
implements both contracts; its rewrite method must cover all contained references.

## Members a newer version adds

Reading an object keeps every member of its JSON that the object did not read, and
saving the object writes those members back after its own. An `EngineObject` keeps
the `Animations` and `Expressions` entries of properties it does not have the same way.
So data that a newer version of Beutl or of your extension writes survives an older
version, as long as the newer version adds members and types instead of changing the
meaning of existing ones. A key your type reads, or only asks for with
`ICoreSerializationContext.Contains`, counts as known and is not kept, and no key is
kept when your type reads the whole JSON object through
`IJsonSerializationContext.GetJsonObject`. When a type stops reading a key whose value
should not be kept, list it with `[DroppedMembers("OldKey")]`, and do not use that name
again for something else.
