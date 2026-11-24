using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Language;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.Puppet_Pin), ResourceType = typeof(GraphicsStrings))]
public sealed partial class PuppetPin : EngineObject
{
    public PuppetPin()
    {
        ScanProperties<PuppetPin>();
        HideProperty(RestPosition);
    }

    /// <summary>The point attached to the undeformed picture, normalized to the effect's input bounds.</summary>
    [Browsable(false)]
    public IProperty<Point> RestPosition { get; } = Property.CreateValue<Point>();

    /// <summary>The pin's destination, normalized to the effect's input bounds.</summary>
    [Display(Name = nameof(GraphicsStrings.Position), ResourceType = typeof(GraphicsStrings),
        Description = nameof(GraphicsStrings.Puppet_PositionDescription))]
    [DisplayFormat(DataFormatString = "0.####")]
    [NumberStep(0.01, 0.001)]
    public IExpressionProperty<Point> Position { get; } = Property.CreateAnimatable<Point>();
}
