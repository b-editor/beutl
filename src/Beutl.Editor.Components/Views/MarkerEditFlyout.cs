using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Beutl.Editor.Components.Helpers;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Controls.Primitives;
using AvaColor = Avalonia.Media.Color;

namespace Beutl.Editor.Components.Views;

public sealed class MarkerEditFlyout : PickerFlyoutBase
{
    public static readonly StyledProperty<TimeSpan> TimeProperty
        = AvaloniaProperty.Register<MarkerEditFlyout, TimeSpan>(nameof(Time));

    public static readonly StyledProperty<string> MarkerNameProperty
        = AvaloniaProperty.Register<MarkerEditFlyout, string>(nameof(MarkerName), string.Empty);

    public static readonly StyledProperty<string> NoteProperty
        = AvaloniaProperty.Register<MarkerEditFlyout, string>(nameof(Note), string.Empty);

    public static readonly StyledProperty<AvaColor> ColorProperty
        = AvaloniaProperty.Register<MarkerEditFlyout, AvaColor>(nameof(Color));

    private TextBox? _nameBox;
    private TextBox? _noteBox;
    private ColorPickerButton? _colorButton;
    private Button? _deleteButton;
    private MarkerEditValues _initial;

    public TimeSpan Time
    {
        get => GetValue(TimeProperty);
        set => SetValue(TimeProperty, value);
    }

    public string MarkerName
    {
        get => GetValue(MarkerNameProperty);
        set => SetValue(MarkerNameProperty, value);
    }

    public string Note
    {
        get => GetValue(NoteProperty);
        set => SetValue(NoteProperty, value);
    }

    public AvaColor Color
    {
        get => GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    public event EventHandler<MarkerEditValues>? ValuesChanged;
    public event EventHandler? DeleteRequested;

    protected override Control CreatePresenter()
    {
        // 入力フィールド
        _nameBox = new TextBox();
        _nameBox.TextChanged += (_, _) => RaiseValuesChanged();

        _colorButton = new ColorPickerButton
        {
            UseColorPalette = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        _colorButton.ColorChanged += (_, _) => RaiseValuesChanged();

        _noteBox = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 72,
            MaxHeight = 140,
        };
        _noteBox.TextChanged += (_, _) => RaiseValuesChanged();

        // 削除ボタン: アクセントカラー(Critical)で右寄せ
        _deleteButton = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new SymbolIcon { Symbol = Symbol.Delete, FontSize = 14 },
                    new TextBlock { Text = Strings.Delete, VerticalAlignment = VerticalAlignment.Center },
                },
            },
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        _deleteButton.Click += (_, _) =>
        {
            DeleteRequested?.Invoke(this, EventArgs.Empty);
            Hide();
        };

        var content = new StackPanel
        {
            Spacing = 8,
            Margin = new Thickness(4),
            Children =
            {

                new TextBlock { Text = Strings.Name },
                _nameBox,
                new TextBlock { Text = Strings.Color },
                _colorButton,
                new TextBlock { Text = Strings.Note },
                _noteBox,
                _deleteButton,
            },
        };

        var pfp = new PickerFlyoutPresenter
        {
            Width = 240,
            Padding = new Thickness(8),
            Content = content,
            Cursor = Cursors.Arrow,
        };
        pfp.Confirmed += OnFlyoutConfirmed;
        pfp.Dismissed += OnFlyoutDismissed;

        return pfp;
    }

    protected override void OnOpening(CancelEventArgs args)
    {
        base.OnOpening(args);
        _initial = new MarkerEditValues(MarkerName, Note, Color);

        if (_nameBox != null)
            _nameBox.Text = MarkerName;
        if (_noteBox != null)
            _noteBox.Text = Note;
        if (_colorButton != null)
            _colorButton.Color = Color;
    }

    protected override void OnConfirmed()
    {
        SyncPropertiesFromControls();
        Hide();
    }

    protected override bool ShouldShowConfirmationButtons() => true;

    private void OnFlyoutConfirmed(PickerFlyoutPresenter sender, object args)
    {
        OnConfirmed();
    }

    private void OnFlyoutDismissed(PickerFlyoutPresenter sender, object args)
    {
        MarkerName = _initial.Name;
        Note = _initial.Note;
        Color = _initial.Color;
        ValuesChanged?.Invoke(this, _initial);
        Hide();
    }

    private void SyncPropertiesFromControls()
    {
        if (_nameBox != null)
            MarkerName = _nameBox.Text ?? string.Empty;
        if (_noteBox != null)
            Note = _noteBox.Text ?? string.Empty;
        if (_colorButton?.Color is { } c)
            Color = c;
    }

    private void RaiseValuesChanged()
    {
        SyncPropertiesFromControls();
        ValuesChanged?.Invoke(this, new MarkerEditValues(MarkerName, Note, Color));
    }
}

public readonly record struct MarkerEditValues(string Name, string Note, AvaColor Color);
