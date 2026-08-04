using System.Collections.Immutable;
using System.Runtime.ExceptionServices;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.Rendering;

internal sealed partial class RenderRequestExecutor
{
    private sealed partial class CompatibilityExecutionState
    {
        public void PrepareBuiltInBackdropCaptures()
        {
            var prepared = new List<PendingBackdropPublication>(_backdropCaptures.Count);
            try
            {
                foreach ((IBuiltInBackdropCaptureSink sink, CompatibilityRenderValue value) in _backdropCaptures)
                {
                    Bitmap bitmap = value.Target.Snapshot();
                    var publication = new PendingBackdropPublication(
                        sink,
                        bitmap,
                        value.EffectiveScale.Value);
                    prepared.Add(publication);
                    _pendingBackdropPublications.Add(publication);
                }
            }
            catch
            {
                foreach (PendingBackdropPublication publication in prepared)
                {
                    publication.Bitmap?.Dispose();
                    publication.Bitmap = null;
                    _pendingBackdropPublications.Remove(publication);
                }
                throw;
            }
            finally
            {
                foreach (CompatibilityRenderValue value in _backdropCaptures.Select(static item => item.Value))
                    ReleaseValueReference(value);
                _backdropCaptures.Clear();
            }
        }

        public void PublishBuiltInBackdropCaptures()
        {
            foreach (PendingBackdropPublication publication in _pendingBackdropPublications)
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

            _pendingBackdropPublications.Clear();
        }

        public void RejectBuiltInBackdropCaptures()
        {
            List<Exception>? failures = null;
            foreach (PendingBackdropPublication publication in _pendingBackdropPublications)
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
            _pendingBackdropPublications.Clear();

            if (failures is null)
                return;
            if (failures.Count == 1)
                ExceptionDispatchInfo.Capture(failures[0]).Throw();
            throw new AggregateException("One or more staged backdrop captures failed to dispose.", failures);
        }

    }
}
