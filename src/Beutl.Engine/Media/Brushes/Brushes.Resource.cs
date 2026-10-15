using Beutl.Composition;

namespace Beutl.Media;

public static partial class Brushes
{
    public static class Resource
    {
        /// <summary>
        /// Gets an <see cref="Colors.White"/> colored brush.
        /// </summary>
        public static SolidColorBrush.Resource White { get; } =
            KnownColor.White.ToBrush().ToResource(CompositionContext.Default);
    }
}
