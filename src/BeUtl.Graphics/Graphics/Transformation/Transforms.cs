namespace BeUtl.Graphics.Transformation;

public sealed class Transforms : List<ITransform>
{
    public Transforms()
    {
    }

    public Transforms(IEnumerable<ITransform> transforms)
        : base(transforms)
    {
    }

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
