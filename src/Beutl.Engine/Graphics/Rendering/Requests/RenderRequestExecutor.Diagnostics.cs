using System.Collections.Immutable;
using System.Runtime.ExceptionServices;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.Rendering.Requests;

internal sealed partial class RenderRequestExecutor
{
    private sealed partial class RenderRequestExecutionState
    {
        public void PrepareBuiltInBackdropCaptures()
        {
            if (_backdropCaptures is not { Count: > 0 } captures)
                return;

            int publicationStart = _pendingBackdropPublications?.Count ?? 0;
            try
            {
                foreach ((IBuiltInBackdropCaptureSink sink, MaterializedRenderValue value) in captures)
                {
                    Bitmap bitmap = value.Target.Snapshot();
                    var publication = new PendingBackdropPublication(
                        sink,
                        bitmap,
                        value.EffectiveScale.Value);
                    try
                    {
                        (_pendingBackdropPublications ??= []).Add(publication);
                    }
                    catch
                    {
                        publication.Bitmap = null;
                        bitmap.Dispose();
                        throw;
                    }
                }
            }
            catch
            {
                if (_pendingBackdropPublications is { } publications)
                {
                    for (int index = publicationStart; index < publications.Count; index++)
                    {
                        publications[index].Bitmap?.Dispose();
                        publications[index].Bitmap = null;
                    }
                    publications.RemoveRange(publicationStart, publications.Count - publicationStart);
                    if (publications.Count == 0)
                        _pendingBackdropPublications = null;
                }
                throw;
            }
            finally
            {
                foreach ((_, MaterializedRenderValue value) in captures)
                    ReleaseValueReference(value);
                _backdropCaptures = null;
            }
        }

        public void PublishBuiltInBackdropCaptures()
        {
            // The sink outlives this frame, so a frame that dropped part of itself has nothing fit to commit.
            if (PreviewAllocationDropObserved)
            {
                RejectBuiltInBackdropCaptures();
                return;
            }

            if (_pendingBackdropPublications is not { } publications)
                return;

            foreach (PendingBackdropPublication publication in publications)
            {
                Bitmap bitmap = publication.Bitmap
                    ?? throw new InvalidOperationException("A backdrop capture was already discharged.");
                try
                {
                    bool accepted = publication.Sink.TryCommitBackdropCapture(
                        bitmap,
                        publication.Density);
                    publication.Bitmap = null;
                    if (!accepted)
                        bitmap.Dispose();
                }
                catch
                {
                    if (publication.Bitmap is not null)
                    {
                        publication.Bitmap = null;
                        bitmap.Dispose();
                    }
                    throw;
                }
            }

            _pendingBackdropPublications = null;
        }

        public void RejectBuiltInBackdropCaptures()
        {
            if (_pendingBackdropPublications is not { } publications)
                return;

            _pendingBackdropPublications = null;
            List<Exception>? failures = null;
            foreach (PendingBackdropPublication publication in publications)
            {
                Bitmap? bitmap = publication.Bitmap;
                publication.Bitmap = null;
                if (bitmap is null)
                    continue;
                try
                {
                    bitmap.Dispose();
                }
                catch (Exception ex)
                {
                    (failures ??= []).Add(ex);
                }
            }
            if (failures is null)
                return;
            if (failures.Count == 1)
                ExceptionDispatchInfo.Capture(failures[0]).Throw();
            throw new AggregateException("One or more staged backdrop captures failed to dispose.", failures);
        }

    }
}
