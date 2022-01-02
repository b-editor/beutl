namespace BEditorNext.Graphics.Transformation;

public sealed class Transforms : List<ITransform>
{
    public Matrix Calculate()
    {
        Transforms list = this;
        Matrix value = Matrix.Identity;

        for (int i = 0; i < list.Count; i++)
        {
            ITransform item = list[i];
            value = item.Value * value;
        }

        return value;
    }
}
