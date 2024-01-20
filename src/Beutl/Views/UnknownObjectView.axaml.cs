using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;

using Beutl.Logging;
using Beutl.Services;
using Beutl.ViewModels;

using Microsoft.Extensions.Logging;

namespace Beutl.Views;

public partial class UnknownObjectView : UserControl
{
    private static readonly CrossFade s_transition = new(TimeSpan.FromMilliseconds(250));

    private readonly ILogger _logger = Log.CreateLogger<UnknownObjectView>();

    private CancellationTokenSource? _lastTransitionCts;

    private IDisposable? _textBindingRevoker;

    public UnknownObjectView()
    {
        InitializeComponent();
        editJsonToggle.GetObservable(ToggleButton.IsCheckedProperty)
            .Subscribe(v =>
            {
                _lastTransitionCts?.Cancel();
                _lastTransitionCts = new CancellationTokenSource();
                CancellationToken localToken = _lastTransitionCts.Token;

                if (v == true)
                {
                    Show(localToken);
                }
                else
                {
                    Hide(localToken);
                }
            });

        jsonSaveButton.Click += OnSaveClick;
    }

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is IUnknownObjectViewModel viewModel)
        {
            try
            {
                viewModel.SetJsonString(jsonTextBox.Text);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An exception has occurred.");
                NotificationService.ShowError(Message.OperationCouldNotBeExecuted, ex.Message);
            }
        }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        jsonTextBox.Text = null;
        _textBindingRevoker?.Dispose();
        _textBindingRevoker = null;
    }

    private async void Show(CancellationToken cts)
    {
        if (DataContext is IUnknownObjectViewModel viewModel
            && _textBindingRevoker == null)
        {
            _textBindingRevoker = jsonTextBox.Bind(TextBox.TextProperty, viewModel.GetJsonString().ToBinding());
        }

        await Task.WhenAll(s_transition.Start(null, jsonSaveButton, cts), s_transition.Start(null, jsonTextBox, cts));
    }

    private async void Hide(CancellationToken cts)
    {
        await Task.WhenAll(s_transition.Start(jsonSaveButton, null, cts), s_transition.Start(jsonTextBox, null, cts));
    }
}
