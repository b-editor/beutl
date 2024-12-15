using System.Numerics;

namespace Beutl.Graphics3D.Transformation;

public interface ITransform3D
{
    Matrix4x4 Value { get; }
}
