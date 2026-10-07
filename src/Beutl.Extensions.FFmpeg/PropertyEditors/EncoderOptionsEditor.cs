using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Beutl.Controls;
using Beutl.Controls.PropertyEditors;
using Beutl.Extensions.FFmpeg.Encoding;
using Beutl.Extensions.FFmpeg.Properties;
using Beutl.FFmpegIpc.Protocol.Messages;
using FluentIcons.Avalonia.Fluent;
using Icon = FluentIcons.Common.Icon;

namespace Beutl.Extensions.FFmpeg.PropertyEditors;

internal sealed class EncoderOptionsEditor : UserControl
{
    private readonly EncoderOptionsEditorViewModel _model;
    private readonly StackPanel _primary = new();
    private readonly StackPanel _advancedRows = new();
    private readonly TextBlock _status = new() { Margin = new Thickness(8, 4), TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _warning = new() { Margin = new Thickness(8, 4), TextWrapping = TextWrapping.Wrap };
    private readonly AutoCompleteStringEditor _optionName = new()
    {
        Name = "AddEncoderOptionName",
        FilterMode = AutoCompleteFilterMode.Contains,
    };
    private readonly Button _add;
    private readonly List<Action> _primaryRefreshers = [];
    private readonly List<Action> _advancedRefreshers = [];
    private (AdditionalOption Option, string Name)[] _advancedSignature = [];
    private string[] _activeSignature = [];
    private (string Name, bool Invalid)[] _numericSignature = [];
    private bool _updating;

    public EncoderOptionsEditor(EncoderOptionsEditorViewModel model)
    {
        _model = model;
        IsEnabled = !model.IsReadOnly;
        PropertyEditorGrid.SetIsAlignmentScope(this, true);
        _status.Bind(ThemeProperty, new DynamicResourceExtension("LabelTextBlockStyle"));
        _warning.Bind(ThemeProperty, new DynamicResourceExtension("ErrorTextBlockStyle"));
        _add = IconButton(Icon.Add, Strings.EncoderOptionsAdd, "AddEncoderOption");
        _optionName.Header = Strings.EncoderOptionsOptionName;
        _optionName.MenuContent = _add;
        _optionName.PropertyChanged += (_, e) =>
        {
            if (e.Property == StringEditor.TextProperty) UpdateAddButton();
        };
        _add.Click += (_, _) =>
        {
            if (_model.AddOption(_optionName.Text))
            {
                _optionName.Text = "";
                _optionName.Focus();
            }
        };
        var advancedContent = new StackPanel { Children = { _advancedRows, _optionName } };
        var advancedBranch = Branch(advancedContent);
        var advancedToggle = Disclosure(Strings.EncoderOptionsAdvanced, advancedBranch, false, "AdvancedEncoderOptions");
        var body = new StackPanel
        {
            Children = { _status, _primary, _warning, advancedToggle, advancedBranch },
        };
        var branch = Branch(body);
        Content = new StackPanel
        {
            Children = { Disclosure(Strings.EncoderOptionsHeader, branch, true, "EncoderOptionsDisclosure"), branch },
        };
        _model.SchemaChanged += Rebuild;
        _model.ValuesChanged += RefreshValues;
        Rebuild();
    }

    private static ToggleButton Disclosure(string header, Control content, bool expanded, string name)
    {
        var toggle = new ToggleButton { Name = name, Content = header, Margin = new Thickness(8, 4), IsChecked = expanded };
        toggle.Bind(ThemeProperty, new DynamicResourceExtension("PropertyEditorMiniExpanderToggleButton"));
        content.IsVisible = expanded;
        toggle.IsCheckedChanged += (_, _) => content.IsVisible = toggle.IsChecked == true;
        return toggle;
    }

    private static TreeLineDecorator Branch(Control content)
    {
        var branch = new TreeLineDecorator { Child = content };
        branch.Bind(TreeLineDecorator.LineBrushProperty, new DynamicResourceExtension("DividerStrokeColorDefaultBrush"));
        return branch;
    }

    private static Button IconButton(Icon symbol, string label, string? name = null)
    {
        var button = new Button
        {
            Name = name,
            Content = new FluentIcon { Icon = symbol, FontSize = 14 },
            Padding = default,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        button.Classes.Add("size-24x24");
        button.Bind(ThemeProperty, new DynamicResourceExtension("TransparentButton"));
        ToolTip.SetTip(button, label);
        AutomationProperties.SetName(button, label);
        return button;
    }

    private void Rebuild()
    {
        _updating = true;
        try
        {
            _status.Text = _model.Status;
            _status.IsVisible = !string.IsNullOrEmpty(_model.Status);
            _primary.Children.Clear();
            _primaryRefreshers.Clear();
            _numericSignature = GetNumericSignature();
            _activeSignature = _model.ActiveDescriptors.Select(d => d.Name).ToArray();
            foreach (EncoderOptionInfo option in _model.ActiveDescriptors)
            {
                var input = CreateInput(option, () => _model.GetValue(option.Name),
                    value => _model.SetValue(option.Name, value), _primaryRefreshers);
                input.Name = "EncoderOption_" + option.Name;
                input.Header = GetLabel(option.Name);
                var reset = IconButton(Icon.ArrowUndo, Strings.EncoderOptionsAutomatic);
                reset.Click += (_, _) => _model.SetValue(option.Name, null);
                input.MenuContent = reset;
                _primary.Children.Add(input);
            }
            RebuildAdvanced();
        }
        finally { _updating = false; }
        RefreshValues();
    }

    private void RebuildAdvanced()
    {
        _advancedRows.Children.Clear();
        _advancedRefreshers.Clear();
        _advancedSignature = _model.Options.Select(o => (o, o.Name)).ToArray();
        foreach (AdditionalOption option in _model.Options)
        {
            EncoderOptionInfo? descriptor = _model.Descriptors.FirstOrDefault(d => d.Name == option.Name);
            var name = new StringEditor { Header = Strings.EncoderOptionsOptionName, Text = option.Name };
            var renameWarning = new TextBlock
            {
                Text = Strings.EncoderOptionsDuplicate,
                IsVisible = false,
                Margin = new Thickness(8, 4),
                TextWrapping = TextWrapping.Wrap,
            };
            renameWarning.Bind(ThemeProperty, new DynamicResourceExtension("ErrorTextBlockStyle"));
            CommitText(name, () => option.Name, value =>
            {
                _model.RenameOption(option, value);
                name.Text = option.Name;
                renameWarning.IsVisible = option.Name != value.Trim();
            });
            var remove = IconButton(Icon.Delete, Strings.EncoderOptionsRemove);
            remove.Click += (_, _) => _model.RemoveOption(option);
            name.MenuContent = remove;
            var input = CreateInput(descriptor ?? new EncoderOptionInfo { Name = option.Name },
                () => option.Value, value =>
                {
                    if (value == null) _model.RemoveOption(option);
                    else _model.SetValue(option, value);
                }, _advancedRefreshers);
            input.Header = Strings.EncoderOptionsValue;
            _advancedRows.Children.Add(new StackPanel { Margin = new Thickness(0, 0, 0, 4), Children = { name, renameWarning, input } });
        }
        _optionName.ItemsSource = _model.Descriptors.Select(d => d.Name)
            .Where(name => !_model.Options.Any(o => o.Name == name)).Order().ToArray();
    }

    private void RefreshValues()
    {
        if (_updating) return;
        if (!_numericSignature.SequenceEqual(GetNumericSignature())
            || !_activeSignature.SequenceEqual(_model.ActiveDescriptors.Select(d => d.Name)))
        {
            Rebuild();
            return;
        }
        _updating = true;
        try
        {
            if (!_advancedSignature.SequenceEqual(_model.Options.Select(o => (o, o.Name)))) RebuildAdvanced();
            foreach (Action refresh in _primaryRefreshers) refresh();
            foreach (Action refresh in _advancedRefreshers) refresh();
            var warnings = _model.Options.Select(option =>
                _model.Descriptors.FirstOrDefault(d => d.Name == option.Name) is { } descriptor
                    ? _model.GetWarning(descriptor, option.Value) : null).OfType<string>().Distinct().ToList();
            if (_model.Options.GroupBy(o => o.Name).Any(g => g.Count() > 1)) warnings.Add(Strings.EncoderOptionsDuplicate);
            if (_model.Options.Any(o => string.IsNullOrWhiteSpace(o.Name))) warnings.Add(Strings.EncoderOptionsEmptyName);
            _warning.Text = string.Join("\n", warnings);
            _warning.IsVisible = warnings.Count > 0;
            UpdateAddButton();
        }
        finally { _updating = false; }
    }

    private void UpdateAddButton()
    {
        string? name = _optionName.Text?.Trim();
        _add.IsEnabled = !string.IsNullOrEmpty(name) && !_model.Options.Any(o => o.Name == name);
    }

    private (string Name, bool Invalid)[] GetNumericSignature()
        => _model.ActiveDescriptors.Where(d => d.Kind is EncoderOptionKind.Integer or EncoderOptionKind.Number)
            .Select(d => (d.Name, _model.GetWarning(d, _model.GetValue(d.Name)) != null)).ToArray();

    private PropertyEditor CreateInput(EncoderOptionInfo descriptor, Func<string?> read, Action<string?> write, List<Action> refreshers)
    {
        PropertyEditor result;
        if (descriptor.Kind == EncoderOptionKind.Choice && !descriptor.AllowsNumericValues
            || descriptor.Kind == EncoderOptionKind.Boolean)
        {
            var editor = new EnumEditor();
            int selectableCount = 0;
            void Refresh()
            {
                string? current = read();
                if (descriptor.Kind == EncoderOptionKind.Boolean)
                    current = current?.ToLowerInvariant() switch
                    {
                        "true" or "yes" or "on" => "1",
                        "false" or "no" or "off" => "0",
                        _ => current,
                    };
                var choices = descriptor.Kind == EncoderOptionKind.Boolean ? BooleanChoices(descriptor) : descriptor.Choices;
                var items = new List<EnumItem> { new(Strings.EncoderOptionsAutomatic, null, "") };
                items.AddRange(choices.Select(c => new EnumItem(c.Value, c.Description, c.Value)));
                selectableCount = items.Count;
                int selected = current == null ? 0 : Array.FindIndex(choices, c => EncoderOptionsEditorViewModel.Matches(c, current)) + 1;
                if (current != null && selected == 0)
                {
                    items.Add(new EnumItem(current, _model.GetWarning(descriptor, current), current));
                    selected = items.Count - 1;
                }
                editor.Items = items;
                editor.SelectedIndex = selected;
            }
            Refresh();
            refreshers.Add(Refresh);
            editor.ValueConfirmed += (_, e) =>
            {
                if (!_updating && e is PropertyEditorValueChangedEventArgs<int> args
                    && args.NewValue >= 0 && args.NewValue < selectableCount)
                    write(args.NewValue == 0 ? null : editor.Items![args.NewValue].Value as string);
            };
            result = editor;
        }
        else if (descriptor.Kind is EncoderOptionKind.Integer or EncoderOptionKind.Number
                 && (read() == null || _model.GetWarning(descriptor, read()) == null))
        {
            var editor = new NumberEditor<decimal>
            {
                SmallChange = descriptor.Kind == EncoderOptionKind.Integer ? 1 : 0.5m,
                NumberFormat = descriptor.Kind == EncoderOptionKind.Integer ? "0" : "0.########",
            };
            void Refresh()
            {
                if (editor.IsKeyboardFocusWithin) return;
                if (decimal.TryParse(read(), NumberStyles.Float, CultureInfo.InvariantCulture, out decimal value)) editor.Value = value;
            }
            Refresh();
            refreshers.Add(Refresh);
            void WriteNumber(object? sender, PropertyEditorValueChangedEventArgs e)
            {
                if (_updating || e is not PropertyEditorValueChangedEventArgs<decimal> args) return;
                decimal value = descriptor.Kind == EncoderOptionKind.Integer ? decimal.Truncate(args.NewValue) : args.NewValue;
                write(value.ToString(CultureInfo.InvariantCulture));
            }
            editor.ValueConfirmed += WriteNumber;
            result = editor;
        }
        else
        {
            string[] suggestions = descriptor.Choices.Select(c => c.Value)
                .Concat([descriptor.DefaultValue, read()]).OfType<string>()
                .Where(v => v.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
            StringEditor editor = suggestions.Length > 0
                ? new AutoCompleteStringEditor { ItemsSource = suggestions, FilterMode = AutoCompleteFilterMode.Contains }
                : new StringEditor();
            editor.Text = read() ?? "";
            CommitText(editor, read, value => write(value));
            refreshers.Add(() => { if (!editor.IsKeyboardFocusWithin) editor.Text = read() ?? ""; });
            result = editor;
        }
        result.IsReadOnly = _model.IsReadOnly;
        result.Description = descriptor.Description;
        return result;
    }

    private void CommitText(StringEditor editor, Func<string?> read, Action<string> write)
    {
        void Commit()
        {
            if (!_updating && editor.Text != (read() ?? "")) write(editor.Text);
        }
        editor.ValueConfirmed += (_, e) => { if (e is PropertyEditorValueChangedEventArgs<string>) Commit(); };
        editor.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { Commit(); e.Handled = true; }
        };
    }

    private static EncoderOptionChoiceInfo[] BooleanChoices(EncoderOptionInfo descriptor)
        => descriptor.Minimum < 0
            ? [new() { Value = "-1", Description = Strings.EncoderOptionsAutomatic }, new() { Value = "1", Description = Strings.EncoderOptionsEnabled }, new() { Value = "0", Description = Strings.EncoderOptionsDisabled }]
            : [new() { Value = "1", Description = Strings.EncoderOptionsEnabled }, new() { Value = "0", Description = Strings.EncoderOptionsDisabled }];

    private static string GetLabel(string name) => name switch
    {
        "preset" => Strings.EncoderOptionsPreset,
        "profile" => Strings.EncoderOptionsProfile,
        "level" => Strings.EncoderOptionsLevel,
        "tune" => Strings.EncoderOptionsTune,
        "crf" or "cq" or "qp" or "quality" or "global_quality" => $"{Strings.EncoderOptionsQuality} ({name.ToUpperInvariant()})",
        _ => name,
    };
}
