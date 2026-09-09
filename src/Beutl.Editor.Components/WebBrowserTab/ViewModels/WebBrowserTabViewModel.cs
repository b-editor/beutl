using System.Text.Json.Nodes;

using Reactive.Bindings;

namespace Beutl.Editor.Components.WebBrowserTab.ViewModels;

internal sealed class WebBrowserTabViewModel : IToolContext
{
    internal static readonly Uri BlankPage = new("about:blank");

    private static int s_lastInstanceNumber;

    private readonly IEditorContext _editorContext;
    private readonly List<string> _addressSuggestions = [];
    private readonly ReactivePropertySlim<Uri> _currentUri;
    private readonly ReactivePropertySlim<string> _address;
    private readonly ReactivePropertySlim<bool> _canGoBack = new();
    private readonly ReactivePropertySlim<bool> _canGoForward = new();
    private readonly ReactivePropertySlim<bool> _isLoading = new();
    private readonly ReactivePropertySlim<bool> _isLinuxRuntimeHelpVisible = new();
    private readonly ReactivePropertySlim<string?> _errorMessage = new();
    private readonly ReactivePropertySlim<string?> _pageTitle = new();
    private readonly ReadOnlyReactivePropertySlim<string> _header;
    private readonly ReadOnlyReactivePropertySlim<bool> _hasWebAddress;
    private readonly int _instanceNumber;
    private bool _disposed;
    private bool _navigationStopped;

    internal WebBrowserTabViewModel(IEditorContext editorContext)
        : this(editorContext, BlankPage)
    {
    }

    internal WebBrowserTabViewModel(IEditorContext editorContext, Uri initialUri)
    {
        _editorContext = editorContext;
        _instanceNumber = Interlocked.Increment(ref s_lastInstanceNumber);

        _currentUri = new ReactivePropertySlim<Uri>(initialUri);
        _address = new ReactivePropertySlim<string>(FormatAddress(initialUri));
        _header = _currentUri
            .CombineLatest(_pageTitle, BuildHeader)
            .ToReadOnlyReactivePropertySlim(BuildHeader(_currentUri.Value, _pageTitle.Value))!;
        _hasWebAddress = _currentUri
            .Select(static uri => uri != BlankPage)
            .ToReadOnlyReactivePropertySlim(initialUri != BlankPage)!;
    }

    public ToolTabExtension Extension => WebBrowserTabExtension.Instance;

    public IReactiveProperty<bool> IsSelected { get; } = new ReactivePropertySlim<bool>();

    public IReadOnlyReactiveProperty<string> Header => _header;

    internal Uri CurrentUri => _currentUri.Value;

    public IReactiveProperty<string> Address => _address;

    public IReadOnlyList<string> AddressSuggestions => _addressSuggestions;

    public IReadOnlyReactiveProperty<bool> CanGoBack => _canGoBack;

    public IReadOnlyReactiveProperty<bool> CanGoForward => _canGoForward;

    public IReadOnlyReactiveProperty<bool> IsLoading => _isLoading;

    public IReadOnlyReactiveProperty<bool> IsLinuxRuntimeHelpVisible => _isLinuxRuntimeHelpVisible;

    public IReadOnlyReactiveProperty<string?> ErrorMessage => _errorMessage;

    public IReadOnlyReactiveProperty<bool> HasWebAddress => _hasWebAddress;

    internal bool TryCreateNavigationUri(out Uri uri)
    {
        if (WebSearchSuggestions.IsSearchQuery(_address.Value))
        {
            uri = WebSearchSuggestions.CreateSearchUri(_address.Value);
            _errorMessage.Value = null;
            return true;
        }

        if (TryNormalizeAddress(_address.Value, out uri))
        {
            if (_errorMessage.Value == Strings.InvalidWebAddress)
            {
                _errorMessage.Value = null;
            }

            return true;
        }

        _errorMessage.Value = Strings.InvalidWebAddress;
        return false;
    }

    internal static bool TryNormalizeAddress(string? address, out Uri uri)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            uri = BlankPage;
            return true;
        }

        string candidate = address.Trim();
        bool hasHttpScheme = candidate.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || candidate.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        if (!hasHttpScheme)
        {
            int colonIndex = candidate.IndexOf(':');
            if (colonIndex > 0
                && Uri.CheckSchemeName(candidate[..colonIndex])
                && (colonIndex + 1 >= candidate.Length || !char.IsAsciiDigit(candidate[colonIndex + 1])))
            {
                uri = BlankPage;
                return false;
            }

            candidate = $"https://{candidate}";
        }

        if (Uri.TryCreate(candidate, UriKind.Absolute, out Uri? result)
            && (result.Scheme == Uri.UriSchemeHttp || result.Scheme == Uri.UriSchemeHttps)
            && !string.IsNullOrWhiteSpace(result.Host))
        {
            uri = result;
            return true;
        }

        uri = BlankPage;
        return false;
    }

    internal void BeginNavigation(Uri uri)
    {
        if (IsPersistableUri(uri))
        {
            _currentUri.Value = uri;
            _address.Value = FormatAddress(uri);
        }

        _isLoading.Value = true;
        _errorMessage.Value = null;
        _pageTitle.Value = null;
        _navigationStopped = false;
    }

    internal void CompleteNavigation(Uri uri, bool isSuccess, bool canGoBack, bool canGoForward)
    {
        if (isSuccess && uri != BlankPage && IsPersistableUri(uri) && string.IsNullOrEmpty(uri.UserInfo))
        {
            string address = FormatAddress(uri);
            _addressSuggestions.Remove(address);
            _addressSuggestions.Insert(0, address);
            if (_addressSuggestions.Count > 100)
            {
                _addressSuggestions.RemoveAt(100);
            }
        }

        if (IsPersistableUri(uri))
        {
            _currentUri.Value = uri;
            _address.Value = FormatAddress(uri);
        }

        _isLoading.Value = false;
        _canGoBack.Value = canGoBack;
        _canGoForward.Value = canGoForward;
        _errorMessage.Value = isSuccess || _navigationStopped ? null : Strings.WebPageLoadFailed;
        if (!isSuccess)
        {
            _pageTitle.Value = null;
        }
        _navigationStopped = false;
    }

    internal void SetWebViewUnavailable(string? detail, bool showLinuxRuntimeHelp)
    {
        _isLoading.Value = false;
        _canGoBack.Value = false;
        _canGoForward.Value = false;
        _isLinuxRuntimeHelpVisible.Value = showLinuxRuntimeHelp;
        _errorMessage.Value = string.IsNullOrWhiteSpace(detail)
            ? Strings.WebViewUnavailable
            : $"{Strings.WebViewUnavailable}{Environment.NewLine}{detail}";
    }

    internal void StopNavigation()
    {
        _isLoading.Value = false;
        _navigationStopped = true;
    }

    internal void UpdateHistoryState(bool canGoBack, bool canGoForward)
    {
        _canGoBack.Value = canGoBack;
        _canGoForward.Value = canGoForward;
    }

    internal void ReportActionFailure()
    {
        _errorMessage.Value = Strings.WebBrowserActionFailed;
    }

    internal void SetPageTitle(Uri uri, string? title)
    {
        if (_currentUri.Value == uri)
        {
            _pageTitle.Value = string.IsNullOrWhiteSpace(title) ? null : title.Trim();
        }
    }

    internal bool TryOpenNewTab(Uri uri)
    {
        if (!IsPersistableUri(uri))
        {
            return false;
        }

        var context = new WebBrowserTabViewModel(_editorContext, uri);
        if (_editorContext.OpenToolTab(context))
        {
            return true;
        }

        context.Dispose();
        return false;
    }

    internal static string FormatAddress(Uri uri)
    {
        return uri == BlankPage ? string.Empty : uri.AbsoluteUri;
    }

    private string BuildHeader(Uri uri, string? pageTitle)
    {
        if (uri == BlankPage)
        {
            return Strings.NewTab;
        }

        if (!string.IsNullOrWhiteSpace(pageTitle))
        {
            return pageTitle.Trim();
        }

        string numbered = $"{Strings.WebBrowser} {_instanceNumber.ToString(CultureInfo.CurrentCulture)}";
        return string.IsNullOrWhiteSpace(uri.Host)
            ? numbered
            : $"{numbered}: {uri.Host}";
    }

    private static bool IsPersistableUri(Uri uri)
    {
        return uri == BlankPage
            || uri.Scheme == Uri.UriSchemeHttp
            || uri.Scheme == Uri.UriSchemeHttps;
    }

    public void ReadFromJson(JsonObject json)
    {
        if (json.TryGetPropertyValue("source", out JsonNode? sourceNode)
            && sourceNode is JsonValue sourceValue
            && sourceValue.TryGetValue(out string? source)
            && TryNormalizeAddress(source, out Uri uri))
        {
            _currentUri.Value = uri;
            _address.Value = FormatAddress(uri);
        }
    }

    public void WriteToJson(JsonObject json)
    {
        json["source"] = FormatAddress(_currentUri.Value);
    }

    public object? GetService(Type serviceType) => _editorContext.GetService(serviceType);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        IsSelected.Dispose();
        _currentUri.Dispose();
        _address.Dispose();
        _canGoBack.Dispose();
        _canGoForward.Dispose();
        _isLoading.Dispose();
        _isLinuxRuntimeHelpVisible.Dispose();
        _errorMessage.Dispose();
        _pageTitle.Dispose();
        _header.Dispose();
        _hasWebAddress.Dispose();
    }
}
