using System.Numerics;

namespace BEditorNext.Graphics.Transformation;

public interface ITransform
{
    Matrix3x2 Value { get; }
}
