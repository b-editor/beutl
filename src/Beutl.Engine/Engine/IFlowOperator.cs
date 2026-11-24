namespace Beutl.Engine;

// このインターフェースをつけたオブジェクトが追加されたらPortalObjectも自動で追加される
public interface IFlowOperator { }

// Only operators that describe their inputs participate in the configuration check.
internal interface IFlowInputOperator : IFlowOperator
{
    FlowInputKind RequiredInput { get; }

    FlowInputKind ConsumedInputs => RequiredInput;

    bool HasNestedInput => false;

    string MissingInputMessage { get; }
}

[Flags]
internal enum FlowInputKind
{
    None = 0,
    Drawable = 1,
    Object3D = 2,
    Light3D = 4,
    Sound = 8,
    Any = Drawable | Object3D | Light3D | Sound
}
