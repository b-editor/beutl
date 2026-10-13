using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Language;

namespace Beutl.Media;

[Display(Name = nameof(GraphicsStrings.Presenter), ResourceType = typeof(GraphicsStrings))]
public sealed partial class BrushPresenter : Brush, IPresenter<Brush>
{
    public BrushPresenter()
    {
        ScanProperties<BrushPresenter>();
    }

    [Display(Name = nameof(GraphicsStrings.Target), ResourceType = typeof(GraphicsStrings))]
    public IProperty<Brush?> Target { get; } = Property.Create<Brush?>();

    public partial class Resource
    {
        /// <summary>Follows the presenters from <paramref name="brush"/> to the brush they finally present.</summary>
        /// <returns><see langword="null"/> when the chain ends at a presenter without a target.</returns>
        /// <exception cref="InvalidOperationException">
        /// The chain loops back on itself; the exception carries <paramref name="cycleMessage"/>.
        /// </exception>
        internal static Brush.Resource? ResolveTarget(Brush.Resource? brush, string cycleMessage)
        {
            HashSet<Brush.Resource>? visited = null;
            while (brush is Resource presenter)
            {
                if (presenter.Target is not { } target)
                    return null;

                visited ??= new HashSet<Brush.Resource>(ReferenceEqualityComparer.Instance);
                if (!visited.Add(presenter))
                    throw new InvalidOperationException(cycleMessage);
                brush = target;
            }

            return brush;
        }
    }
}
