using System;
using System.Windows.Input;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace BEditor.Controls
{
    public class HyperLink : TemplatedControl
    {
        public static readonly StyledProperty<string?> TextProperty = AvaloniaProperty.Register<HyperLink, string?>("Text");
        public static readonly StyledProperty<ICommand?> CommandProperty = AvaloniaProperty.Register<HyperLink, ICommand?>("Command");
        private bool _pressed;
        private TextBlock? _text;

        public string? Text
        {
            get => GetValue(TextProperty);
            set => SetValue(TextProperty, value);
        }

        public ICommand? Command
        {
            get => GetValue(CommandProperty);
            set => SetValue(CommandProperty, value);
        }

        protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
        {
            base.OnApplyTemplate(e);

            _text = e.NameScope.Find<TextBlock>("textBlock");

            _text.AddHandler(PointerPressedEvent, Text_PointerPressed, RoutingStrategies.Tunnel);
            _text.AddHandler(PointerReleasedEvent, Text_PointerReleased, RoutingStrategies.Tunnel);
        }

        private void Text_PointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (_pressed)
            {
                Command?.Execute(null);

                _pressed = false;
            }
        }

        private void Text_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            _pressed = true;
        }
    }
}