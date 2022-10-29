using System.Reactive.Linq;

using BeUtl.Framework;
using BeUtl.ProjectSystem;
using BeUtl.Streaming;

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
