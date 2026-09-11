using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

using Beutl.Editor.Components.WebBrowserTab.ViewModels;

namespace Beutl.Editor.Components.WebBrowserTab.Views;

internal sealed class WebBrowserAddressBox : TextBox
{
    public static readonly StyledProperty<string?> AddressProperty =
        AvaloniaProperty.Register<WebBrowserAddressBox, string?>(nameof(Address));

    public static readonly StyledProperty<IReadOnlyList<string>?> SuggestionsProperty =
        AvaloniaProperty.Register<WebBrowserAddressBox, IReadOnlyList<string>?>(nameof(Suggestions));

    private bool _updatingText;
    private bool _completingText;
    private CancellationTokenSource? _suggestionRequest;

    internal Func<string, CancellationToken, Task<IReadOnlyList<string>>> SuggestionProvider { get; set; } =
        (query, token) => WebSearchSuggestions.Default.GetSuggestionsAsync(query, token);

    internal bool SuggestionsEnabled { get; set; } = true;

    internal TimeSpan SuggestionDelay { get; set; } = TimeSpan.FromMilliseconds(250);

    internal event Action<IReadOnlyList<string>>? SearchSuggestionsChanged;

    protected override Type StyleKeyOverride => typeof(TextBox);

    public string? Address
    {
        get => GetValue(AddressProperty);
        set => SetValue(AddressProperty, value);
    }

    public IReadOnlyList<string>? Suggestions
    {
        get => GetValue(SuggestionsProperty);
        set => SetValue(SuggestionsProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == AddressProperty && !_updatingText)
        {
            CancelSearchSuggestions();
            UpdateDisplay();
        }
        else if (change.Property == TextProperty && IsFocused && !_updatingText)
        {
            _updatingText = true;
            try
            {
                SetCurrentValue(AddressProperty, Text);
            }
            finally
            {
                _updatingText = false;
            }

            if (!_completingText)
            {
                _ = RefreshSearchSuggestionsAsync(Text ?? string.Empty);
            }
        }
    }

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        UpdateDisplay();
        SelectAll();
        string? address = Text;
        // Pointer handling may move the caret after focus has been raised.
        Dispatcher.UIThread.Post(() =>
        {
            if (IsFocused && Text == address)
            {
                SelectAll();
            }
        }, DispatcherPriority.Input);
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        CancelSearchSuggestions();
        UpdateDisplay();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        CancelSearchSuggestions();
        base.OnDetachedFromVisualTree(e);
    }

    internal void CancelSearchSuggestions()
    {
        _suggestionRequest?.Cancel();
        _suggestionRequest = null;
        SearchSuggestionsChanged?.Invoke([]);
    }

    internal async Task RefreshSearchSuggestionsAsync(string query)
    {
        CancelSearchSuggestions();
        if (!SuggestionsEnabled || !IsFocused || !WebSearchSuggestions.IsSearchQuery(query) || query.Trim().Length < 2)
        {
            return;
        }

        using var request = new CancellationTokenSource();
        _suggestionRequest = request;
        try
        {
            await Task.Delay(SuggestionDelay, request.Token);
            IReadOnlyList<string> suggestions = await SuggestionProvider(query, request.Token);
            if (IsFocused && ReferenceEquals(_suggestionRequest, request) && !request.IsCancellationRequested)
            {
                SearchSuggestionsChanged?.Invoke(suggestions);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or System.Net.Http.HttpRequestException
                                  or System.Text.Json.JsonException)
        {
            // Suggestions are optional: network errors must not interrupt address entry.
        }
        finally
        {
            if (ReferenceEquals(_suggestionRequest, request))
            {
                _suggestionRequest = null;
            }
        }
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        bool canComplete = !e.Handled && !IsReadOnly && !string.IsNullOrEmpty(e.Text);
        base.OnTextInput(e);
        if (!canComplete || Text is not { Length: > 0 } prefix
            || SelectionStart != SelectionEnd || CaretIndex != prefix.Length)
        {
            return;
        }

        string? completion = FindCompletion(prefix, Suggestions);
        if (completion != null)
        {
            _completingText = true;
            try
            {
                SetCurrentValue(TextProperty, completion);
            }
            finally
            {
                _completingText = false;
            }
            int schemeLength = prefix.Contains("://", StringComparison.Ordinal)
                ? 0
                : completion.IndexOf("://", StringComparison.Ordinal) + 3;
            SelectionStart = prefix.Length + schemeLength;
            SelectionEnd = completion.Length;
        }
    }

    internal static string? FindCompletion(string prefix, IReadOnlyList<string>? suggestions)
    {
        if (string.IsNullOrEmpty(prefix) || suggestions == null)
        {
            return null;
        }

        foreach (string suggestion in suggestions)
        {
            string candidate = suggestion;
            string scheme = string.Empty;
            if (!prefix.Contains("://", StringComparison.Ordinal))
            {
                int schemeEnd = candidate.IndexOf("://", StringComparison.Ordinal);
                if (schemeEnd >= 0)
                {
                    scheme = candidate[..(schemeEnd + 3)];
                    candidate = candidate[(schemeEnd + 3)..];
                }
            }

            if (candidate.Length > prefix.Length && candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                int pathStart = prefix.IndexOf('/', prefix.Contains("://", StringComparison.Ordinal)
                    ? prefix.IndexOf("://", StringComparison.Ordinal) + 3 : 0);
                if (pathStart >= 0 && !candidate.AsSpan(pathStart, prefix.Length - pathStart)
                        .SequenceEqual(prefix.AsSpan(pathStart)))
                {
                    continue;
                }

                return scheme + prefix + candidate[prefix.Length..];
            }
        }

        return null;
    }

    private void UpdateDisplay()
    {
        _updatingText = true;
        try
        {
            string? display = Address;
            if (!IsFocused && !WebSearchSuggestions.IsSearchQuery(Address)
                && WebBrowserTabViewModel.TryNormalizeAddress(Address, out Uri uri)
                && uri != WebBrowserTabViewModel.BlankPage)
            {
                display = uri.IsDefaultPort ? uri.Host : uri.Authority;
            }

            SetCurrentValue(TextProperty, display);
        }
        finally
        {
            _updatingText = false;
        }
    }
}
