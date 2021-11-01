using System;
using System.Runtime.InteropServices;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Platform;
using Avalonia.Rendering;
using Avalonia.Styling;

#nullable disable

namespace BEditor.Controls
{
    public class FluentWindow : Window, IStyleable
    {
        public static readonly StyledProperty<bool> DisableDefaultTitleBarProperty = AvaloniaProperty.Register<FluentWindow, bool>("DisableDefaultTitleBar", false);

        private static WindowIcon _icon;

        private Control _defaultTitleBar;
        private MinMaxCloseControl _systemCaptionButtons;

        public FluentWindow()
            : base(WindowImplSolver.GetWindowImpl())
        {
            Title = string.Empty;
            var assets = AvaloniaLocator.Current.GetService<IAssetLoader>();
            var uri = new Uri("avares://beditor/Assets/Images/icon.ico");
            _icon ??= new WindowIcon(assets.Open(uri));
            Icon = _icon;
            SetValue(WindowConfig.SaveProperty, true);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                PseudoClasses.Set(":windows", true);

                if (PlatformImpl is CoreWindowImpl cwi)
                {
                    cwi.SetOwner(this);
                }

                ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.NoChrome;
                ExtendClientAreaToDecorationsHint = true;
                TransparencyLevelHint = WindowTransparencyLevel.Mica;
            }
        }

        public bool DisableDefaultTitleBar
        {
            get => GetValue(DisableDefaultTitleBarProperty);
            set => SetValue(DisableDefaultTitleBarProperty, value);
        }

        Type IStyleable.StyleKey => typeof(Window);

        protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
        {
            base.OnApplyTemplate(e);
            _systemCaptionButtons = e.NameScope.Find<MinMaxCloseControl>("SystemCaptionButtons");
            if (_systemCaptionButtons != null)
            {
                _systemCaptionButtons.Height = 32;
            }

            _defaultTitleBar = e.NameScope.Find<Control>("DefaultTitleBar");
            if (_defaultTitleBar != null)
            {
                _defaultTitleBar.Margin = new Thickness(0, 0, 138 /* 46x3 */, 0);
                _defaultTitleBar.Height = 32;
            }
        }

        internal bool HitTestTitleBarRegion(Point windowPoint)
        {
            if (DisableDefaultTitleBar)
            {
                return false;
            }

            return _defaultTitleBar?.HitTestCustom(windowPoint) ?? false;
        }

        internal bool HitTestCaptionButtons(Point pos)
        {
            if (pos.Y < 1)
                return false;

            var result = _systemCaptionButtons?.HitTestCustom(pos) ?? false;
            return result;
        }

        internal bool HitTestMaximizeButton(Point pos)
        {
            return _systemCaptionButtons.HitTestMaxButton(pos);
        }

        internal void FakeMaximizeHover(bool hover)
        {
            _systemCaptionButtons.FakeMaximizeHover(hover);
        }

        internal void FakeMaximizePressed(bool pressed)
        {
            _systemCaptionButtons.FakeMaximizePressed(pressed);
        }

        internal void FakeMaximizeClick()
        {
            _systemCaptionButtons.FakeMaximizeClick();
        }
    }
}