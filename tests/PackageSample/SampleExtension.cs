using System.Reactive.Linq;

using Beutl.Framework;
using Beutl.ProjectSystem;
using Beutl.Streaming;

namespace PackageSample;

[Export]
public sealed class SampleExtension : LayerExtension
{
    public override string Name => "SampleExtension";

    public override string DisplayName => "SampleExtension";

    public override void Load()
    {
        OperatorRegistry.RegisterOperation<SampleOp>(Observable.Return("Sample Operator"));
    }
}
