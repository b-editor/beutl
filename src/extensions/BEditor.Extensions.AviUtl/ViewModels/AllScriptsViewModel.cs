using Microsoft.Extensions.DependencyInjection;

namespace BEditor.Extensions.AviUtl.ViewModels
{
    public sealed class AllScriptsViewModel
    {
        public record Item();

        public AllScriptsViewModel()
        {
            var loader = ServicesLocator.Current.Provider.GetRequiredService<ScriptLoader>();
            Items = loader.Loaded!;
        }

        public ScriptEntry[] Items { get; }
    }
}
