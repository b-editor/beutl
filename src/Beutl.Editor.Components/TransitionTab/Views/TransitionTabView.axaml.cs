using Avalonia.Controls;
using Avalonia.Interactivity;
using Beutl.Editor.Components.LibraryThumbnails;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Editor.Components.PropertyEditors.Views;
using Beutl.Editor.Components.TransitionTab.ViewModels;
using Beutl.Graphics.Transitions;
using Beutl.Media.Decoding;
using Beutl.Services;
using Microsoft.Extensions.DependencyInjection;
using ExtensionGuard = Beutl.Api.Services.ExtensionGuard;

namespace Beutl.Editor.Components.TransitionTab.Views;

public partial class TransitionTabView : UserControl
{
    private INotificationService Notifications => (DataContext as IServiceProvider)?.GetService<INotificationService>()
        ?? Beutl.Controls.ControlNotifications.GetOrDefault(this);

    public TransitionTabView()
    {
        InitializeComponent();
    }

    private async void OnTypeDropDownClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not TransitionTabViewModel viewModel) return;

        await PickerGuard.RunAsync(typeEditor, typeEditor, async () =>
        {
            try
            {
                var selectVm = new SelectLibraryItemDialogViewModel(viewModel.GetService<Beutl.Media.FontManager>() ?? Beutl.Controls.ControlFonts.GetOrCreate(this), viewModel.GetRequiredService<LibraryService>(),
                    KnownLibraryItemFormats.ClipTransition,
                    typeof(ClipTransition),
                    LibraryThumbnailCapture.ForEdge(viewModel.ShownElement, viewModel.ShownEdge, viewModel), decoders: ExtensionGuard.FindService<DecoderRegistry>(viewModel) ?? Beutl.Controls.ControlDecoders.GetDecoders(this));
                await selectVm.SelectTypeAsync(viewModel.Transition.Value?.GetType());
                Type? type = await LibraryItemPickerHelper.ShowAsync(
                    typeEditor,
                    selectVm,
                    KnownLibraryItemFormats.ClipTransition) as Type;
                if (type != null)
                {
                    viewModel.ChangeType(type);
                }
            }
            catch (Exception ex)
            {
                Notifications.ShowError(Strings.Error, ex.Message);
            }
        });
    }

    private void OnDeleteClick(object? sender, RoutedEventArgs e)
    {
        (DataContext as TransitionTabViewModel)?.ChangeType(null);
    }
}
