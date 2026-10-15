using System.Text.Json.Serialization;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.IO;

namespace Beutl.Media.Source;

[JsonConverter(typeof(MediaSourceJsonConverter))]
[SuppressResourceClassGeneration]
public abstract class MediaSource : EngineObject, IFileSource
{
    private Uri? _uri;
    private long _reloadVersion;

    public new Uri Uri
    {
        get => _uri ?? throw new InvalidOperationException("URI is not set.");
        protected set
        {
            if (_uri == value) return;
            _uri = value;
            OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(Uri)));
            RaiseEdited();
        }
    }

    public bool HasUri => _uri != null;

    public virtual void ReadFrom(Uri uri)
    {
        if (HasUri && Uri != uri)
        {
            // 古い URI の共有参照を別 Resource が保持していると
            // TryAddRef が成功して新 URI でも古いリソースを返してしまうため、
            // URI が切り替わったタイミングで共有参照を破棄する。
            ClearSharedResource();
        }
        Uri = uri;
    }

    /// <summary>Drops the media that resources of this source share, so the next resource opens it again.</summary>
    protected abstract void ClearSharedResource();

    protected TResource CreateResource<TResource>(CompositionContext context)
        where TResource : Resource, new()
    {
        var resource = new TResource();
        bool versionBumped = true;
        resource.Reconcile(this, context, ref versionBumped);
        return resource;
    }

    internal void InvalidateResourceCache()
    {
        ClearSharedResource();
        Interlocked.Increment(ref _reloadVersion);
        RaiseEdited();
    }

    public new abstract class Resource : EngineObject.Resource
    {
        private long _loadedReloadVersion;
        protected bool ReloadRequested { get; private set; }

        public override void Reconcile(EngineObject obj, CompositionContext context, ref bool versionBumped)
        {
            base.Reconcile(obj, context, ref versionBumped);
            long reloadVersion = Volatile.Read(ref ((MediaSource)obj)._reloadVersion);
            ReloadRequested = _loadedReloadVersion != reloadVersion;
            _loadedReloadVersion = reloadVersion;
        }
    }
}
