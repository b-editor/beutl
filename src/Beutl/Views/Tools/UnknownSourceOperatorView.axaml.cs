using System.Text.Json.Nodes;

using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;

using Beutl.Operation;
using Beutl.Services;
using Beutl.ViewModels.Tools;

namespace Beutl.Views.Tools;
public partial class UnknownSourceOperatorView : UserControl
{
    private static readonly CrossFade s_transition = new(TimeSpan.FromMilliseconds(250));

    private CancellationTokenSource? _lastTransitionCts;

    public UnknownSourceOperatorView()
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
        if (DataContext is SourceOperatorViewModel viewModel)
        {
            try
            {
                viewModel.SetJson(jsonTextBox.Text);
            }
            catch (Exception ex)
            {
                NotificationService.ShowError(Message.OperationCouldNotBeExecuted, ex.Message);
            }
        }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        jsonTextBox.Text = null;
    }

    private async void Show(CancellationToken cts)
    {
        if (DataContext is SourceOperatorViewModel { Model: DummySourceOperator { Json: JsonObject json } }
            && jsonTextBox.Text == null)
        {
            jsonTextBox.Text = json.ToJsonString(JsonHelper.SerializerOptions);
        }

        await Task.WhenAll(s_transition.Start(null, jsonSaveButton, cts), s_transition.Start(null, jsonTextBox, cts));
    }

    private async void Hide(CancellationToken cts)
    {
        await Task.WhenAll(s_transition.Start(jsonSaveButton, null, cts), s_transition.Start(jsonTextBox, null, cts));
    }
}
