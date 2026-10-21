using System;
using System.Collections.Generic;

using Beutl.Composition;

namespace Beutl.Engine;

public class EngineObject : CoreObject
{
    public virtual Resource ToResource(CompositionContext context)
    {
        throw null!;
    }

    public class Resource : IDisposable
    {
        private EngineObject? _original;

        public EngineObject? GetOriginal() => _original;

        public EngineObject RequireOriginal()
        {
            throw null!;
        }

        public virtual void Reconcile(EngineObject obj, CompositionContext context, ref bool versionBumped)
        {
            _original = obj;
        }

        public void Dispose()
        {
            Dispose(true);
        }

        protected virtual void Dispose(bool disposing)
        {
        }
    }
}

public static class ResourceReconciler
{
    public static void ReconcileValue<TValue>(EngineObject.Resource owner, CompositionContext context, IProperty<TValue> prop, ref TValue field, ref bool versionBumped)
    {
        throw null!;
    }

    public static void ReconcileChildren<TItem, TResource>(EngineObject.Resource owner, CompositionContext context, IList<TItem> prop, ref List<TResource> field, ref bool versionBumped) where TItem : EngineObject where TResource : EngineObject.Resource
    {
        throw null!;
    }

    public static void ReconcileChild<TObject, TResource>(EngineObject.Resource owner, CompositionContext context, IProperty<TObject> prop, ref TResource? field, ref bool versionBumped) where TObject : EngineObject? where TResource : EngineObject.Resource
    {
        throw null!;
    }
}
