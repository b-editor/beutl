# Completing element repairs in plugins

Recovered elements preserve their original sidecar bytes until all recovery blockers
have been repaired or removed. Plugins and custom editors can finish a repair with
`Beutl.Editor.Services.ElementRecoveryService.TryCompleteRepair(element, history)`.

Saving does not write a protected element's edits, so the editor treats it as read-only:
`Scene.IsElementLocked` returns `true` for it, and the built-in editors refuse its edits.
They still allow deleting the element, and removing or replacing an object or value that
holds a blocker, because that is the repair. Custom editors should refuse other edits
while `Scene.IsElementLocked` is `true`, the same way they respect a locked clip.

Use the `HistoryManager` already observing the edited scene. Apply the repair through
the normal public property, keyframe, or collection APIs, call `TryCompleteRepair`,
then commit that same history transaction:

```csharp
shape.Transform.CurrentValue = new RotationTransform();
ElementRecoveryService.TryCompleteRepair(element, history);
history.Commit("Repair transform");
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
