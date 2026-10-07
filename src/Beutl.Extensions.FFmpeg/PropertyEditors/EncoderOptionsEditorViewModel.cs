using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Reactive.Disposables;
using System.Text.Json.Nodes;
using Avalonia.Threading;
using Beutl.Collections;
using Beutl.Extensibility;
using Beutl.Extensions.FFmpeg.Encoding;
using Beutl.Extensions.FFmpeg.Properties;
using Beutl.FFmpegIpc;
using Beutl.FFmpegIpc.Protocol;
using Beutl.FFmpegIpc.Protocol.Messages;
using Beutl.Logging;
using Beutl.PropertyAdapters;
using Microsoft.Extensions.Logging;

namespace Beutl.Extensions.FFmpeg.PropertyEditors;

internal sealed class EncoderOptionsEditorViewModel : IPropertyEditorContext
{
    private static readonly ILogger s_logger = Log.CreateLogger<EncoderOptionsEditorViewModel>();
    private readonly IPropertyAdapter<CoreList<AdditionalOption>> _property;
    private readonly FFmpegVideoEncoderSettings _settings;
    private readonly CompositeDisposable _subscriptions = [];
    private readonly LatestRefreshTracker _refresh = new();
    private readonly HashSet<AdditionalOption> _observedOptions = [];
    private CoreList<AdditionalOption>? _list;
    private bool _mutating;
    private bool _disposed;

    public EncoderOptionsEditorViewModel(
        CorePropertyAdapter<CoreList<AdditionalOption>> property, PropertyEditorExtension extension)
    {
        _property = property;
        _settings = (FFmpegVideoEncoderSettings)property.Object;
        Extension = extension;
        _subscriptions.Add(property.GetObservable().Subscribe(ObserveList));
        _subscriptions.Add(_settings.GetObservable(FFmpegVideoEncoderSettings.CodecProperty).Subscribe(_ => RequestUpdate()));
        _subscriptions.Add(_settings.GetObservable(FFmpegVideoEncoderSettings.FormatProperty).Subscribe(_ => RequestUpdate()));
    }

    public PropertyEditorExtension Extension { get; }
    public IReadOnlyList<AdditionalOption> Options => _list ?? [];
    public EncoderOptionInfo[] Descriptors { get; private set; } = [];
    public string? Status { get; private set; }
    public bool IsReadOnly => _property.IsReadOnly;
    public event Action? SchemaChanged;
    public event Action? ValuesChanged;

    public static string BuildCacheKey(CodecQueryParams query, int pixelFormat)
        => $"{CodecOptionQuery.BuildCacheKey(query)}\0{pixelFormat.ToString(CultureInfo.InvariantCulture)}";

    public IEnumerable<EncoderOptionInfo> ActiveDescriptors
        => Descriptors.Where(d => Options.Any(o => o.Name == d.Name));

    public string? GetValue(string name) => Options.FirstOrDefault(o => o.Name == name)?.Value;

    public void SetValue(string name, string? value)
    {
        if (_disposed || IsReadOnly) return;
        Mutate(() =>
        {
            AdditionalOption[] matches = Options.Where(o => o.Name == name).ToArray();
            if (value == null)
            {
                foreach (AdditionalOption option in matches) _list?.Remove(option);
            }
            else if (matches.Length > 0)
            {
                matches[0].Value = value;
                foreach (AdditionalOption duplicate in matches.Skip(1)) _list?.Remove(duplicate);
            }
            else
            {
                EnsureList().Add(new AdditionalOption(name, value));
            }
        });
    }

    public bool AddOption(string? name)
    {
        name = name?.Trim();
        if (_disposed || IsReadOnly || string.IsNullOrEmpty(name) || Options.Any(o => o.Name == name)) return false;
        string? defaultValue = Descriptors.FirstOrDefault(o => o.Name == name)?.DefaultValue;
        SetValue(name, defaultValue ?? "");
        return true;
    }

    public void RemoveOption(AdditionalOption option)
    {
        if (!_disposed && !IsReadOnly) Mutate(() => _list?.Remove(option));
    }

    public void RenameOption(AdditionalOption option, string name)
    {
        if (_disposed || IsReadOnly) return;
        name = name.Trim();
        if (Options.Any(o => o != option && o.Name == name)) return;
        option.Name = name;
    }

    public void SetValue(AdditionalOption option, string value)
    {
        if (!_disposed && !IsReadOnly) option.Value = value;
    }

    public string? GetWarning(EncoderOptionInfo descriptor, string? value)
    {
        if (value == null) return null;
        bool valid = descriptor.Kind switch
        {
            EncoderOptionKind.Choice => descriptor.Choices.Any(c => Matches(c, value))
                || descriptor.AllowsNumericValues
                    && decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal choiceNumber)
                    && (!descriptor.RequiresInteger || decimal.Truncate(choiceNumber) == choiceNumber)
                    && WithinRange(descriptor, choiceNumber),
            EncoderOptionKind.Integer => decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal number)
                && decimal.Truncate(number) == number && WithinRange(descriptor, number),
            EncoderOptionKind.Number => decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal number)
                && WithinRange(descriptor, number),
            EncoderOptionKind.Boolean => value.ToLowerInvariant() is "0" or "1" or "true" or "false" or "yes" or "no" or "on" or "off"
                || value == "-1" && descriptor.Minimum < 0,
            _ => true,
        };
        return valid ? null : string.Format(CultureInfo.CurrentCulture, Strings.EncoderOptionInvalidValue, descriptor.Name, value);
    }

    public static bool Matches(EncoderOptionChoiceInfo choice, string value)
        => choice.Value.Equals(value, StringComparison.Ordinal)
            || decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal number)
                && (choice.NumericValue == number
                    || decimal.TryParse(choice.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal namedNumber)
                        && number == namedNumber);

    private static bool WithinRange(EncoderOptionInfo descriptor, decimal value)
        => (!descriptor.Minimum.HasValue || (double)value >= descriptor.Minimum.Value)
            && (!descriptor.Maximum.HasValue || (double)value <= descriptor.Maximum.Value);

    private CoreList<AdditionalOption> EnsureList()
    {
        if (_list == null) _property.SetValue(new CoreList<AdditionalOption>());
        return _list!;
    }

    private void Mutate(Action action)
    {
        _mutating = true;
        try { action(); }
        finally { _mutating = false; ValuesChanged?.Invoke(); }
    }

    private void ObserveList(CoreList<AdditionalOption>? list)
    {
        if (_list != null) _list.CollectionChanged -= OnCollectionChanged;
        foreach (AdditionalOption option in _observedOptions) option.PropertyChanged -= OnOptionChanged;
        _observedOptions.Clear();
        _list = list;
        if (_list != null) _list.CollectionChanged += OnCollectionChanged;
        ObserveItems();
        if (!_mutating) ValuesChanged?.Invoke();
    }

    private void ObserveItems()
    {
        foreach (AdditionalOption option in _observedOptions.Where(o => !Options.Contains(o)).ToArray())
        {
            option.PropertyChanged -= OnOptionChanged;
            _observedOptions.Remove(option);
        }
        foreach (AdditionalOption option in Options)
            if (_observedOptions.Add(option)) option.PropertyChanged += OnOptionChanged;
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ObserveItems();
        if (!_mutating) ValuesChanged?.Invoke();
    }

    private void OnOptionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_mutating) ValuesChanged?.Invoke();
    }

    private void RequestUpdate()
    {
        if (_disposed) return;
        CodecQueryParams query = CodecOptionQuery.Create(_settings.Codec, _settings.OutputFile);
        int pixelFormat = _settings.Format;
        string key = BuildCacheKey(query, pixelFormat);
        if (FFmpegOptionsCaches.EncoderOptions.TryGetCached(key, out EncoderOptionInfo[]? cached))
        {
            _refresh.Supersede();
            ApplySchema(cached, null);
            return;
        }
        CancellationToken ct = _refresh.StartNew();
        ApplySchema([], Strings.EncoderOptionsLoading);
        _ = UpdateAsync(query, pixelFormat, key, ct);
    }

    private async Task UpdateAsync(CodecQueryParams query, int pixelFormat, string key, CancellationToken ct)
    {
        OptionsQueryResult<EncoderOptionInfo> result;
        try
        {
            result = await FFmpegOptionsCaches.EncoderOptions.GetOrQueryAsync(key, async () =>
            {
                var connection = await FFmpegWorkerProcess.DecodingInstance.EnsureStartedAsync().ConfigureAwait(false);
                var response = await connection.RequestAsync<QueryEncoderOptionsRequest, QueryEncoderOptionsResponse>(
                    MessageType.QueryEncoderOptions, MessageType.QueryEncoderOptionsResult,
                    new QueryEncoderOptionsRequest { CodecName = query.CodecName, OutputFile = query.OutputFile, PixelFormat = pixelFormat })
                    .ConfigureAwait(false);
                return new OptionsQueryResult<EncoderOptionInfo>(response.Options, response.Degraded);
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            if (LatestRefreshTracker.IsCurrent(ct) && ex is not FFmpegLibrariesNotFoundException)
                s_logger.LogWarning(ex, "Failed to query encoder options from FFmpeg worker");
            result = new OptionsQueryResult<EncoderOptionInfo>([], Degraded: true);
        }
        if (!LatestRefreshTracker.IsCurrent(ct)) return;
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!_disposed && LatestRefreshTracker.IsCurrent(ct))
                    ApplySchema(result.Items, result.Degraded ? Strings.EncoderOptionsUnavailable : null);
            });
        }
        catch (Exception ex)
        {
            if (!_disposed) s_logger.LogDebug(ex, "Dispatcher unavailable while applying encoder options");
        }
    }

    private void ApplySchema(EncoderOptionInfo[] descriptors, string? status)
    {
        Descriptors = descriptors;
        Status = status;
        SchemaChanged?.Invoke();
    }

    public void Accept(IPropertyEditorContextVisitor visitor) => visitor.Visit(this);
    public void WriteToJson(JsonObject json) { }
    public void ReadFromJson(JsonObject json) { }

    public void Dispose()
    {
        _disposed = true;
        _refresh.Dispose();
        _subscriptions.Dispose();
        if (_list != null) _list.CollectionChanged -= OnCollectionChanged;
        foreach (AdditionalOption option in _observedOptions) option.PropertyChanged -= OnOptionChanged;
        _observedOptions.Clear();
        SchemaChanged = null;
        ValuesChanged = null;
    }
}
