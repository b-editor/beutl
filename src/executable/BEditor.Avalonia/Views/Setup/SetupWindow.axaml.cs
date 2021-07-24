using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

using BEditor.Drawing;

using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BEditor.Views.Setup
{
    public partial class SetupWindow : FluentWindow
    {
        public SetupWindow()
        {
            InitializeComponent();
#if DEBUG
            this.AttachDevTools();
#endif
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            var colorPalette = new ColorPalette()
            {
                Name = "Colors",
            };
            foreach (var (key, value) in Enumerate(typeof(Colors)))
            {
                colorPalette.Colors.Add(key, value);
            }

            var mPalette = new ColorPalette()
            {
                Name = "MaterialColors",
            };
            foreach (var (key, value) in Enumerate(typeof(MaterialColors)))
            {
                mPalette.Colors.Add(key, value);
            }

            PaletteRegistry.Register(colorPalette);
            PaletteRegistry.Register(mPalette);
            PaletteRegistry.Save();
        }

        private static IEnumerable<KeyValuePair<string, Color>> Enumerate(Type type)
        {
            return type.GetProperties(BindingFlags.Public | BindingFlags.Static | BindingFlags.GetProperty)
                .Where(i => i.PropertyType == typeof(Color))
                .Select(i => new KeyValuePair<string, Color>(i.Name, (Color)(i.GetValue(null) ?? Colors.White)));
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}