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

    public abstract void ReadFrom(Uri uri);

    internal virtual void InvalidateResourceCache()
    {
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
