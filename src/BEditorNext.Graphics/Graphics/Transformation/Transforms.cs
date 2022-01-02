using System.Numerics;

namespace BEditorNext.Graphics.Transformation;

public sealed class Transforms : List<ITransform>
{
    public Matrix3x2 Calculate()
    {
        Transforms list = this;
        Matrix3x2 value = Matrix3x2.Identity;

        for (int i = 0; i < list.Count; i++)
        {
            ITransform item = list[i];
            value = item.Value * value;
        }

        return value;
    }
}
