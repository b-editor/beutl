using Beutl.Configuration;
using Beutl.Controls.Navigation;
using Beutl.Media.Decoding;
using Beutl.ViewModels.ExtensionsPages;

using Reactive.Bindings;

using static Beutl.Configuration.ExtensionConfig;

namespace Beutl.ViewModels.SettingsPages;

public sealed class DecoderPriorityPageViewModel : BasePageViewModel
{
    private readonly ExtensionConfig _extensionConfig = GlobalConfiguration.Instance.ExtensionConfig;

    public record DecoderDetailViewModel(IDecoderInfo Model)
    {
        public string Name => Model.Name;

        public string ClrTypeName => Model.GetType().ToString();

        public string VideoExtensions
        {
            get
            {
                string[] arr = Model.VideoExtensions().ToArray();
                string str = arr.Length > 0 ? string.Join(';', arr) : Strings.Unsupported;
                return $"{Strings.Video}: {str}";
            }
        }

        public string AudioExtensions
        {
            get
            {
                string[] arr = Model.AudioExtensions().ToArray();
                string str = arr.Length > 0 ? string.Join(';', arr) : Strings.Unsupported;
                return $"{Strings.Audio}: {str}";
            }
        }
    }

    public DecoderPriorityPageViewModel()
    {
        Items = new(DecoderRegistry.EnumerateDecoder().Select(v => new DecoderDetailViewModel(v)));

        NavigateParent.Subscribe(async () =>
        {
            INavigationProvider nav = await GetNavigation();
            await nav.NavigateAsync<ExtensionsSettingsPageViewModel>();
        });
    }

    public CoreList<DecoderDetailViewModel> Items { get; }

    public AsyncReactiveCommand NavigateParent { get; } = new();

    public override void Dispose()
    {
    }

    public void MoveItem(int oldIndex, int newIndex)
    {
        Items.Move(oldIndex, newIndex);

        // Items -> ExtensionConfig.DecoderPriority
        _extensionConfig.DecoderPriority.Replace(Items.Select(v => new TypeLazy(TypeFormat.ToString(v.Model.GetType()))).ToArray());
    }
}
