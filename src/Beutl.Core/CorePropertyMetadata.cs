using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

using Beutl.Validation;

namespace Beutl;

public abstract class CorePropertyMetadata : ICorePropertyMetadata
{
    protected CorePropertyMetadata(bool shouldSerialize = true, params Attribute[] attributes)
    {
        ShouldSerialize = shouldSerialize;
        Attributes = attributes;
        UpdatedAttributes();
    }

    public bool ShouldSerialize { get; private set; } = true;

    public bool Notifiable { get; private set; } = true;

    public bool Browsable { get; private set; } = true;

    public bool Tracked { get; private set; } = true;

    public DisplayAttribute? DisplayAttribute { get; private set; }

    public abstract Type PropertyType { get; }

    public Attribute[] Attributes { get; private set; }

    private void UpdatedAttributes()
    {
        if (Attributes != null)
        {
            ShouldSerialize = !Attributes.Any(x => x is NotAutoSerializedAttribute);
            Tracked = !Attributes.Any(x => x is NotTrackedAttribute);
            foreach (Attribute item in Attributes)
            {
                switch (item)
                {
                    case DisplayAttribute display:
                        DisplayAttribute = display;
                        break;

                    case NotifiableAttribute notifiable:
                        Notifiable = notifiable.Notifiable;
                        break;

                    case BrowsableAttribute browsableAttribute:
                        Browsable = browsableAttribute.Browsable;
                        break;
                }
            }
        }
    }

    public virtual void Merge(ICorePropertyMetadata baseMetadata, CoreProperty? property)
    {
        if (baseMetadata is CorePropertyMetadata metadata1)
        {
            var att = new Attribute[Attributes.Length + metadata1.Attributes.Length];
            Attributes.CopyTo(att, 0);
            metadata1.Attributes.CopyTo(att, Attributes.Length);
            Attributes = att;

            UpdatedAttributes();
        }
    }

    protected internal abstract object? GetDefaultValue();

    protected internal abstract IValidator? GetValidator();

    object? ICorePropertyMetadata.GetDefaultValue()
    {
        return GetDefaultValue();
    }

    IValidator? ICorePropertyMetadata.GetValidator()
    {
        return GetValidator();
    }
}
