using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.NodeGraph.Composition;

namespace Beutl.NodeGraph.Nodes;

public sealed partial class VideoSourceNode : GraphNode
{
    public VideoSourceNode()
    {
        Output = AddOutput<VideoSourceRenderNode>("Output");
        Source = AddInput<VideoSource?>("Source");
        Time = AddInput<TimeSpan>("Time");
    }

    public OutputPort<VideoSourceRenderNode> Output { get; }

    public InputPort<VideoSource?> Source { get; }

    public InputPort<TimeSpan> Time { get; }

    public partial class Resource
    {
        private VideoSourceRenderNode? _cachedOutput;
        private VideoSource.Resource? _sourceResource;
        private VideoSource? _lastSource;

        public override void Update(GraphCompositionContext context)
        {
            var source = Source;

            if (source == null)
            {
                if (_cachedOutput != null)
                {
                    _cachedOutput.Dispose();
                    _cachedOutput = null;
                }

                _sourceResource?.Dispose();
                _sourceResource = null;
                _lastSource = null;
                Output = null!;
                return;
            }

            if (_lastSource != source)
            {
                _sourceResource?.Dispose();
                _sourceResource = source.ToResource(context);
                _lastSource = source;
            }
            else
            {
                bool updateOnly = false;
                _sourceResource!.Update(source, context, ref updateOnly);
            }

            TimeSpan time = Time;
            Rational rate = _sourceResource.FrameRate;
            double frameNum = time.Ticks * rate.Numerator / (double)(TimeSpan.TicksPerSecond * rate.Denominator);
            int frame = (int)Math.Round(frameNum, MidpointRounding.AwayFromZero);

            if (_cachedOutput == null)
            {
                _cachedOutput = new VideoSourceRenderNode(_sourceResource, frame, Brushes.Resource.White, null);
            }
            else
            {
                _cachedOutput.Update(_sourceResource, frame, Brushes.Resource.White, null);
            }

            Output = _cachedOutput;
        }

        partial void PostDispose(bool disposing)
        {
            if (disposing)
            {
                _sourceResource?.Dispose();
                _sourceResource = null;
                _cachedOutput?.Dispose();
                _cachedOutput = null;
            }
        }
    }
}
