using System.Text.Json.Serialization;

using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.IO;
using Beutl.Serialization;

namespace Beutl.Media.Source;

[JsonConverter(typeof(CubeSourceJsonConverter))]
[SuppressResourceClassGeneration]
public sealed class CubeSource : MediaSource
{
    private WeakReference<CubeFile>? _cubeRef;

    public override void ReadFrom(Uri uri)
    {
        Uri = uri;
    }

    public override Resource ToResource(RenderContext context)
    {
        var resource = new Resource();
        bool updateOnly = true;
        resource.Update(this, context, ref updateOnly);
        return resource;
    }

    public new sealed class Resource : MediaSource.Resource
    {
        private CubeFile? _cube;
        private Uri? _loadedUri;

        public CubeFile? Cube => _cube;

        public override void Update(EngineObject obj, RenderContext context, ref bool updateOnly)
        {
            base.Update(obj, context, ref updateOnly);
            var cubeSource = (CubeSource)obj;

            if (_loadedUri != cubeSource.Uri && cubeSource.HasUri)
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
                if (!updateOnly)
                {
                    Version++;
                    updateOnly = true;
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
