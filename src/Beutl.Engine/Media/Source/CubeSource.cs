using System.Text.Json.Serialization;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Serialization;

namespace Beutl.Media.Source;

[JsonConverter(typeof(CubeSourceJsonConverter))]
[SuppressResourceClassGeneration]
public sealed class CubeSource : MediaSource
{
    private WeakReference<CubeFile>? _cubeRef;

    public override void ReadFrom(Uri uri)
    {
        if (HasUri && Uri != uri) Volatile.Write(ref _cubeRef, null);
        Uri = uri;
    }

    public override Resource ToResource(CompositionContext context)
    {
        var resource = new Resource();
        bool versionBumped = true;
        resource.Reconcile(this, context, ref versionBumped);
        return resource;
    }

    internal override void InvalidateResourceCache()
    {
        Volatile.Write(ref _cubeRef, null);
        base.InvalidateResourceCache();
    }

    public new sealed class Resource : MediaSource.Resource
    {
        private CubeFile? _cube;
        private Uri? _loadedUri;

        public CubeFile? Cube => _cube;

        public override void Reconcile(EngineObject obj, CompositionContext context, ref bool versionBumped)
        {
            base.Reconcile(obj, context, ref versionBumped);
            var cubeSource = (CubeSource)obj;

            if (cubeSource.HasUri && (_loadedUri != cubeSource.Uri || ReloadRequested))
            {
                _cube = null;
                var localRef = Volatile.Read(ref cubeSource._cubeRef);
                if (localRef?.TryGetTarget(out var cube) == true)
                {
                    _cube = cube;
                }
                else
                {
                    try
                    {
                        using var stream = UriHelper.ResolveStream(cubeSource.Uri);
                        _cube = CubeFile.FromStream(stream);
                        Volatile.Write(ref cubeSource._cubeRef, new WeakReference<CubeFile>(_cube));
                    }
                    catch
                    {
                        _cube = null;
                        _loadedUri = cubeSource.Uri;
                        return;
                    }
                }

                _loadedUri = cubeSource.Uri;
                if (!versionBumped)
                {
                    Version++;
                    versionBumped = true;
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            _cube = null;
        }
    }
}
