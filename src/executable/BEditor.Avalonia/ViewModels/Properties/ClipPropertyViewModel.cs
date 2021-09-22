
using BEditor.Data;

namespace BEditor.ViewModels.Properties
{
    public sealed class ClipPropertyViewModel
    {
        public ClipPropertyViewModel(ClipElement clip)
        {
            ClipElement = clip;
        }

        public ClipElement ClipElement { get; }
    }
}
