using System.Runtime.ExceptionServices;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.NodeGraph.Composition;

namespace Beutl.NodeGraph.Nodes;

public sealed partial class VideoSourceNode : GraphNode
{
    public VideoSourceNode()
    {
        Output = AddOutput<VideoSourceRenderNode?>("Output");
        Source = AddInput<VideoSource?>("Source");
        Time = AddInput<TimeSpan>("Time");
    }

    public OutputPort<VideoSourceRenderNode?> Output { get; }

    public InputPort<VideoSource?> Source { get; }

    public InputPort<TimeSpan> Time { get; }

    public partial class Resource
    {
        private VideoSourceRenderNode? _cachedOutput;
        private VideoSource.Resource? _sourceResource;
        private VideoSource? _lastSource;

        protected override void UpdateCore(GraphCompositionContext context)
        {
            var source = Source;

            if (source == null)
            {
                ExceptionDispatchInfo? failure = ClearOwnedResource(ref _sourceResource);
                VideoSourceRenderNode? output = _cachedOutput;
                _cachedOutput = null;
                _lastSource = null;
                Output = null;
                try
                {
                    output?.Dispose();
                }
                catch (Exception ex)
                {
                    failure ??= ExceptionDispatchInfo.Capture(ex);
                }

                failure?.Throw();
                return;
            }

            ExceptionDispatchInfo? cleanupFailure = null;
            if (_lastSource != source)
            {
                VideoSource.Resource replacement = source.ToResource(context);
                cleanupFailure = ReplaceOwnedResource(ref _sourceResource, replacement);
                _lastSource = source;
            }
            else
            {
                bool updateOnly = false;
                _sourceResource!.Update(source, context, ref updateOnly);
            }

            VideoSource.Resource sourceResource = _sourceResource!;
            TimeSpan time = Time;
            Rational rate = sourceResource.FrameRate;
            double frameNum = time.Ticks * rate.Numerator / (double)(TimeSpan.TicksPerSecond * rate.Denominator);
            int frame = (int)Math.Round(frameNum, MidpointRounding.AwayFromZero);

            if (_cachedOutput == null)
            {
                _cachedOutput = new VideoSourceRenderNode(sourceResource, frame, Brushes.Resource.White, null);
            }
            else
            {
                _cachedOutput.Update(sourceResource, frame, Brushes.Resource.White, null);
            }

            Output = _cachedOutput;
            cleanupFailure?.Throw();
        }

        partial void PrepareResourceDispose(bool disposing, GeneratedResourceCleanupContext context)
        {
            if (disposing)
            {
                context.Reserve(_sourceResource);
            }
        }

        partial void PostDispose(bool disposing)
        {
            if (!disposing)
                return;

            _sourceResource = null;
            VideoSourceRenderNode? cachedOutput = _cachedOutput;
            _cachedOutput = null;
            _lastSource = null;
            Output = null;

            Exception? failure = null;
            DisposeOwnedResources(ref failure, cachedOutput);
            ThrowIfCleanupFailed(failure);
        }
    }
}
