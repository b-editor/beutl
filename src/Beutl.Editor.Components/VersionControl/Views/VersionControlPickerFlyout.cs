using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Controls.Primitives;

namespace Beutl.Editor.Components.VersionControl.Views;

internal sealed class VersionControlPickerFlyout : PickerFlyoutBase
{
    private sealed record CancellationRequest(
        VersionControlPickerFlyout Flyout,
        TaskCompletionSource<bool> Completion,
        CancellationToken CancellationToken);

    private const double PresenterWidth = 320;
    private const double PresenterHorizontalPadding = 8;

    private readonly StackPanel _contentPanel;
    private TaskCompletionSource<bool>? _completion;
    private Func<bool>? _canConfirm;
    private bool _confirmOnEnter;

    public VersionControlPickerFlyout()
    {
        TitleTextBlock = new TextBlock
        {
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        };
        MessageTextBlock = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
        };
        PrimaryLabelTextBlock = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
        };
        PrimaryTextBox = new TextBox();
        SecondaryLabelTextBlock = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
        };
        SecondaryTextBox = new TextBox();
        _contentPanel = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                TitleTextBlock,
                MessageTextBlock,
                PrimaryLabelTextBlock,
                PrimaryTextBox,
                SecondaryLabelTextBlock,
                SecondaryTextBox,
            },
        };

        PrimaryTextBox.KeyDown += OnInputKeyDown;
        SecondaryTextBox.KeyDown += OnInputKeyDown;
        Closed += (_, _) => Complete(confirmed: false, hide: false);
    }

    internal TextBlock TitleTextBlock { get; }

    internal TextBlock MessageTextBlock { get; }

    internal TextBlock PrimaryLabelTextBlock { get; }

    internal TextBox PrimaryTextBox { get; }

    internal TextBlock SecondaryLabelTextBlock { get; }

    internal TextBox SecondaryTextBox { get; }

    internal PickerFlyoutPresenter? Presenter { get; private set; }

    public async Task<string?> ShowTextInputAsync(
        Control anchor,
        string title,
        string watermark,
        string? initialText)
    {
        ResetPendingRequest();
        ConfigureContent(title);
        PrimaryLabelTextBlock.IsVisible = false;
        PrimaryTextBox.IsVisible = true;
        PrimaryTextBox.Watermark = watermark;
        PrimaryTextBox.Text = initialText;
        _confirmOnEnter = true;

        bool confirmed = await ShowAsync(
            anchor,
            () => !string.IsNullOrWhiteSpace(PrimaryTextBox.Text));
        return confirmed ? PrimaryTextBox.Text : null;
    }

    public Task<bool> ShowConfirmationAsync(
        Control anchor,
        string title,
        string message)
    {
        ResetPendingRequest();
        ConfigureContent(title);
        MessageTextBlock.Text = message;
        MessageTextBlock.IsVisible = true;
        _confirmOnEnter = false;
        return ShowAsync(anchor, static () => true);
    }

    public async Task<VersionControlIdentityInput?> ShowIdentityAsync(
        Control anchor,
        string title,
        string nameLabel,
        string emailLabel,
        string? initialName,
        string? initialEmail,
        CancellationToken cancellationToken)
    {
        ResetPendingRequest();
        ConfigureContent(title);
        PrimaryLabelTextBlock.Text = nameLabel;
        PrimaryLabelTextBlock.IsVisible = true;
        PrimaryTextBox.IsVisible = true;
        PrimaryTextBox.Text = initialName;
        SecondaryLabelTextBlock.Text = emailLabel;
        SecondaryLabelTextBlock.IsVisible = true;
        SecondaryTextBox.IsVisible = true;
        SecondaryTextBox.Text = initialEmail;
        _confirmOnEnter = true;

        bool confirmed = await ShowAsync(
            anchor,
            () => !string.IsNullOrWhiteSpace(PrimaryTextBox.Text)
                  && !string.IsNullOrWhiteSpace(SecondaryTextBox.Text),
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return confirmed
            ? new VersionControlIdentityInput(
                PrimaryTextBox.Text!.Trim(),
                SecondaryTextBox.Text!.Trim())
            : null;
    }

    protected override Control CreatePresenter()
    {
        Presenter = new PickerFlyoutPresenter
        {
            Width = PresenterWidth,
            Padding = new(PresenterHorizontalPadding, 4),
            Content = _contentPanel,
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(
            Presenter,
            ScrollBarVisibility.Disabled);
        Presenter.Confirmed += OnPresenterConfirmed;
        Presenter.Dismissed += OnPresenterDismissed;
        return Presenter;
    }

    protected override void OnOpening(CancelEventArgs args)
    {
        base.OnOpening(args);
        Dispatcher.UIThread.Post(() =>
        {
            if (PrimaryTextBox.IsVisible)
            {
                PrimaryTextBox.Focus();
                PrimaryTextBox.SelectAll();
            }
        });
    }

    protected override void OnConfirmed()
    {
        if (_canConfirm?.Invoke() != true)
        {
            return;
        }

        Complete(confirmed: true, hide: true);
    }

    protected override bool ShouldShowConfirmationButtons() => true;

    private void ConfigureContent(string title)
    {
        TitleTextBlock.Text = title;
        MessageTextBlock.Text = null;
        MessageTextBlock.IsVisible = false;
        PrimaryLabelTextBlock.Text = null;
        PrimaryLabelTextBlock.IsVisible = false;
        PrimaryTextBox.Watermark = null;
        PrimaryTextBox.Text = null;
        PrimaryTextBox.IsVisible = false;
        SecondaryLabelTextBlock.Text = null;
        SecondaryLabelTextBlock.IsVisible = false;
        SecondaryTextBox.Watermark = null;
        SecondaryTextBox.Text = null;
        SecondaryTextBox.IsVisible = false;
        _confirmOnEnter = false;
    }

    private async Task<bool> ShowAsync(
        Control anchor,
        Func<bool> canConfirm,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        cancellationToken.ThrowIfCancellationRequested();

        _canConfirm = canConfirm;
        var completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _completion = completion;
        Task<bool> task = completion.Task;
        try
        {
            ShowAt(anchor);
        }
        catch
        {
            Complete(confirmed: false, hide: false);
            throw;
        }

        using CancellationTokenRegistration registration = cancellationToken.Register(
            static state =>
            {
                var request = (CancellationRequest)state!;
                if (Dispatcher.UIThread.CheckAccess())
                {
                    request.Flyout.CancelPendingRequest(
                        request.Completion,
                        request.CancellationToken);
                }
                else
                {
                    Dispatcher.UIThread.Post(
                        () => request.Flyout.CancelPendingRequest(
                            request.Completion,
                            request.CancellationToken));
                }
            },
            new CancellationRequest(this, completion, cancellationToken));
        return await task;
    }

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (!_confirmOnEnter
            || e.Key is not (Key.Enter or Key.Return)
            || e.KeyModifiers != KeyModifiers.None
            || _canConfirm?.Invoke() != true)
        {
            return;
        }

        e.Handled = true;
        OnConfirmed();
    }

    private void OnPresenterConfirmed(
        PickerFlyoutPresenter sender,
        object args)
    {
        OnConfirmed();
    }

    private void OnPresenterDismissed(
        PickerFlyoutPresenter sender,
        object args)
    {
        Complete(confirmed: false, hide: true);
    }

    private void ResetPendingRequest()
    {
        TaskCompletionSource<bool>? completion = _completion;
        _completion = null;
        _canConfirm = null;
        _confirmOnEnter = false;
        completion?.TrySetResult(false);
        if (IsOpen)
        {
            Hide();
        }
    }

    private void CancelPendingRequest(
        TaskCompletionSource<bool> completion,
        CancellationToken cancellationToken)
    {
        if (!ReferenceEquals(_completion, completion))
        {
            return;
        }

        _completion = null;
        _canConfirm = null;
        _confirmOnEnter = false;
        if (IsOpen)
        {
            Hide();
        }

        completion.TrySetCanceled(cancellationToken);
    }

    private void Complete(bool confirmed, bool hide)
    {
        TaskCompletionSource<bool>? completion = _completion;
        _completion = null;
        _canConfirm = null;
        _confirmOnEnter = false;
        completion?.TrySetResult(confirmed);
        if (hide && IsOpen)
        {
            Hide();
        }
    }
}

internal readonly record struct VersionControlIdentityInput(string Name, string Email);
