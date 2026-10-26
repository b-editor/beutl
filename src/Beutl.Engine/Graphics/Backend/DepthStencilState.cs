namespace Beutl.Graphics.Backend;

public enum CompareFunction { Less, LessOrEqual, Equal, GreaterOrEqual, Greater, NotEqual, Always, Never }

public enum StencilOperation { Keep, Zero, Replace, IncrementClamp, DecrementClamp, Invert, IncrementWrap, DecrementWrap }

/// <summary>Stencil operations, masks and reference for one face orientation.</summary>
public readonly record struct StencilFaceState(
    CompareFunction Compare, StencilOperation Fail, StencilOperation DepthFail, StencilOperation Pass,
    uint CompareMask = uint.MaxValue, uint WriteMask = uint.MaxValue, uint Reference = 0);

/// <summary>Depth testing/writes and optional front/back stencil operations.</summary>
public readonly record struct DepthStencilState(bool TestEnabled, bool WriteEnabled,
    CompareFunction Compare = CompareFunction.Less,
    bool StencilEnabled = false, StencilFaceState Front = default, StencilFaceState Back = default)
{
    internal bool ReadsDepth => TestEnabled && Compare is not (CompareFunction.Always or CompareFunction.Never);
    internal bool WritesDepth => TestEnabled && WriteEnabled && Compare != CompareFunction.Never;
    internal bool ReadsStencil => StencilEnabled && (Reads(Front) || Reads(Back));
    internal bool WritesStencil => StencilEnabled && (Writes(Front) || Writes(Back));

    private bool Writes(StencilFaceState face) => (face.WriteMask & 255) != 0
        && Operations(face).Any(op => op != StencilOperation.Keep);

    private bool Reads(StencilFaceState face) =>
        face.Compare is not (CompareFunction.Always or CompareFunction.Never) && (face.CompareMask & 255) != 0
        || Writes(face) && ((face.WriteMask & 255) != 255 || Operations(face).Any(op => op is not
            (StencilOperation.Keep or StencilOperation.Zero or StencilOperation.Replace)));

    private IEnumerable<StencilOperation> Operations(StencilFaceState face)
    {
        bool? stencilPass = face.Compare switch
        {
            CompareFunction.Always => true,
            CompareFunction.Never => false,
            _ when (face.CompareMask & 255) == 0 => face.Compare is CompareFunction.Equal
                or CompareFunction.LessOrEqual or CompareFunction.GreaterOrEqual,
            _ => null,
        };
        if (stencilPass != true) yield return face.Fail;
        if (stencilPass == false) yield break;
        if (TestEnabled && Compare != CompareFunction.Always) yield return face.DepthFail;
        if (!TestEnabled || Compare != CompareFunction.Never) yield return face.Pass;
    }
}
