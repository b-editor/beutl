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
        Uri = uri;
    }

    public override Resource ToResource(CompositionContext context)
    {
        var resource = new Resource();
        try
        {
            bool updateOnly = true;
            resource.Update(this, context, ref updateOnly);
            return resource;
        }
        catch
        {
            try
            {
                resource.Dispose();
            }
            catch
            {
                // Preserve the acquisition failure while reclaiming any partially initialized resource.
            }

            throw;
        }
    }

    public new sealed class Resource : MediaSource.Resource
    {
        private CubeFile? _cube;
        private Uri? _loadedUri;

        public CubeFile? Cube => ReadGeneratedResourceState(ref _cube);

        public override void Update(EngineObject obj, CompositionContext context, ref bool updateOnly)
        {
            var cubeSource = (CubeSource)obj;
            using GeneratedResourceOperationLease operation = BeginExclusiveResourceOperation(cubeSource);
            base.Update(obj, context, ref updateOnly);

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
            if (disposing)
            {
                _cube = null;
                _loadedUri = null;
            }

            Exception? failure = null;
            try
            {
                base.Dispose(disposing);
            }
            catch (Exception ex)
            {
                failure = ex;
            }

            ThrowIfCleanupFailed(failure);
        }
    }
}
