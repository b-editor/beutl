using System.Collections;
using System.Reactive.Disposables;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;

using Beutl.Reactive;

namespace Beutl.Controls.PropertyEditors;

[TemplatePart("PART_InnerAutoCompleteBox", typeof(AutoCompleteBox))]
public class AutoCompleteStringEditor : StringEditor
{
    public static readonly StyledProperty<IEnumerable> ItemsSourceProperty =
        AvaloniaProperty.Register<AutoCompleteStringEditor, IEnumerable>(nameof(ItemsSource));

    public static readonly StyledProperty<AutoCompleteFilterMode> FilterModeProperty =
        AvaloniaProperty.Register<AutoCompleteStringEditor, AutoCompleteFilterMode>(
            nameof(FilterMode));

    private readonly CompositeDisposable _acDisposables = [];
    private AutoCompleteBox _autoCompleteBox;
    private string _acOldValue;

    public IEnumerable ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public AutoCompleteFilterMode FilterMode
    {
        get => GetValue(FilterModeProperty);
        set => SetValue(FilterModeProperty, value);
    }

    protected override Type StyleKeyOverride => typeof(AutoCompleteStringEditor);

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        _acDisposables.Clear();
        base.OnApplyTemplate(e);

        _autoCompleteBox = e.NameScope.Find<AutoCompleteBox>("PART_InnerAutoCompleteBox");
        if (_autoCompleteBox != null)
        {
            _autoCompleteBox.GetPropertyChangedObservable(AutoCompleteBox.TextProperty)
                .Subscribe(args =>
                {
                    if (args is AvaloniaPropertyChangedEventArgs<string> typed)
                    {
                        string newVal = typed.NewValue.GetValueOrDefault() ?? "";
                        string oldVal = typed.OldValue.GetValueOrDefault() ?? "";
                        if (Text != newVal)
                        {
                            Text = newVal;
                        }

                        if (_autoCompleteBox.IsKeyboardFocusWithin)
                        {
                            RaiseEvent(new PropertyEditorValueChangedEventArgs<string>(
                                newVal, oldVal, ValueChangedEvent));
                        }
                    }
                })
                .DisposeWith(_acDisposables);

            _autoCompleteBox.AddDisposableHandler(GotFocusEvent, (_, _) =>
                {
                    _acOldValue = Text;
                })
                .DisposeWith(_acDisposables);

            _autoCompleteBox.AddDisposableHandler(LostFocusEvent, (_, _) =>
                {
                    if (Text != _acOldValue)
                    {
                        RaiseEvent(new PropertyEditorValueChangedEventArgs<string>(
                            Text, _acOldValue, ValueConfirmedEvent));
                    }
                }, handledEventsToo: true)
                .DisposeWith(_acDisposables);
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty && _autoCompleteBox != null)
        {
            string newText = change.GetNewValue<string>() ?? "";
            if (_autoCompleteBox.Text != newText)
            {
                _autoCompleteBox.Text = newText;
            }
        }
    }
}
