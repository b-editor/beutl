# Resource reconcile API migration

`EngineObject.Resource.Update` is now `EngineObject.Resource.Reconcile`, and the
protected `CompareAndUpdate` helpers moved to the public static
`ResourceReconciler` class. The `ref bool updateOnly` parameter is now
`ref bool versionBumped`: it has always meant "`Version` already moved during
this pass", and the new name says so. Apart from the owning resource that the
`ResourceReconciler` methods now take first, only names changed; the
version-stepping behavior is the same. There are no compatibility shims, so
extensions must update their sources and rebuild.

| Before | After |
| --- | --- |
| `override void Update(EngineObject obj, CompositionContext context, ref bool updateOnly)` | `override void Reconcile(EngineObject obj, CompositionContext context, ref bool versionBumped)` |
| `resource.Update(obj, context, ref updateOnly)` | `resource.Reconcile(obj, context, ref versionBumped)` |
| `partial void PreUpdate(T obj, CompositionContext context)` | `partial void PreReconcile(T obj, CompositionContext context)` |
| `partial void PostUpdate(T obj, CompositionContext context)` | `partial void PostReconcile(T obj, CompositionContext context)` |
| `CompareAndUpdate(context, prop, ref field, ref updateOnly)` | `ResourceReconciler.ReconcileValue(this, context, prop, ref field, ref versionBumped)` |
| `CompareAndUpdateObject(context, prop, ref field, ref updateOnly)` | `ResourceReconciler.ReconcileChild(this, context, prop, ref field, ref versionBumped)` |
| `CompareAndUpdateList(context, list, ref field, ref updateOnly)` | `ResourceReconciler.ReconcileChildren(this, context, list, ref field, ref versionBumped)` |

The generated `Resource` classes already use the new names. A hook that still
implements `PreUpdate` or `PostUpdate` fails with CS0759, because the generator
no longer declares those partial methods; rename it to keep it running.

`ResourceReconciler` lives in the `Beutl.Engine` namespace. Its first argument is
the resource whose field is being reconciled; that resource's `Version` steps
when the value, child, or child list changed.
