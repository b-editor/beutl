namespace BeUtl;

#pragma warning disable IDE0032
public class NumericalPropertyMetadata<T> : CorePropertyMetadata<T>
{
    private T? _minimum;
    private T? _maximum;

    public T? Minimum
    {
        get => _minimum;
        init => _minimum = value;
    }

    public T? Maximum
    {
        get => _maximum;
        init => _maximum = value;
    }

    public override void Merge(CorePropertyMetadata baseMetadata, CoreProperty property)
    {
        base.Merge(baseMetadata, property);
        if (baseMetadata is NumericalPropertyMetadata<T> baseT)
        {
            if (_minimum == null)
            {
                _minimum = baseT.Minimum;
            }

            if (_maximum == null)
            {
                _maximum = baseT.Maximum;
            }
        }
    }
}
