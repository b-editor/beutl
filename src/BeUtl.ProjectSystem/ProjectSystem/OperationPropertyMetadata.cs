using System.Collections.Immutable;

namespace BeUtl.ProjectSystem;

#pragma warning disable IDE0032 // 自動プロパティを使用する
public interface IOperationPropertyMetadata : ICorePropertyMetadata
{
    public bool? IsAnimatable { get; init; }

    public ResourceReference<string> Header { get; init; }
}

public record class OperationPropertyMetadata<T> : CorePropertyMetadata<T>, IOperationPropertyMetadata
{
    private T? _maximum;
    private T? _minimum;
    private ResourceReference<string> _header;
    private bool? _isAnimatable;

    public bool? IsAnimatable
    {
        get => _isAnimatable;
        init => _isAnimatable = value;
    }

    public ResourceReference<string> Header
    {
        get => _header;
        init => _header = value;
    }

    public T? Minimum
    {
        get => _minimum;
        init
        {
            _minimum = value;
            HasMinimum = true;
        }
    }

    public T? Maximum
    {
        get => _maximum;
        init
        {
            _maximum = value;
            HasMinimum = true;
        }
    }

    public bool HasMinimum { get; private set; }

    public bool HasMaximum { get; private set; }

    public override void Merge(ICorePropertyMetadata baseMetadata, CoreProperty? property)
    {
        base.Merge(baseMetadata, property);
        if (baseMetadata is OperationPropertyMetadata<T> metadata)
        {
            if (_isAnimatable == null)
            {
                _isAnimatable = metadata.IsAnimatable;
            }

            if (_header.Key == null)
            {
                _header = metadata.Header;
            }

            if (!HasMinimum)
            {
                _minimum = metadata.Minimum;
                HasMinimum = true;
            }

            if (!HasMaximum)
            {
                _maximum = metadata.Maximum;
                HasMaximum = true;
            }
        }
    }
}

public record class FilePropertyMetadata : OperationPropertyMetadata<FileInfo?>
{
    private ImmutableArray<string> _extensions;

    public ImmutableArray<string> Extensions
    {
        get => _extensions;
        init => _extensions = value;
    }

    public override void Merge(ICorePropertyMetadata baseMetadata, CoreProperty? property)
    {
        base.Merge(baseMetadata, property);
        if (baseMetadata is FilePropertyMetadata metadata)
        {
            if (_extensions.IsEmpty)
            {
                _extensions = metadata.Extensions;
            }
        }
    }
}
