using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;

using BEditor.Drawing;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BEditor.ViewModels
{
    public sealed class FontDialogViewModel : IDisposable
    {
        internal readonly CompositeDisposable _disposables = new();

        static FontDialogViewModel()
        {
            LoadUsedFonts();
        }
        public FontDialogViewModel(Font selected)
        {
            SelectedItem = new(new FontItem(selected));

            foreach (var font in FontItems)
            {
                font.IsChecked.Value = font.Font == selected;
                font.Visibility.Value = Visibility.Visible;
            }

            foreach (var font_ in UsedFonts)
            {
                font_.IsChecked.Value = false;
                font_.Visibility.Value = Visibility.Visible;
            }

            Search.Subscribe(str =>
            {
                if (str is null) return;

                SetVisibility();

                if (string.IsNullOrWhiteSpace(str)) return;

                var regexPattern = Regex.Replace(str, ".", m =>
                {
                    string s = m.Value;
                    if (s.Equals("?"))
                    {
                        return ".";
                    }
                    else if (s.Equals("*"))
                    {
                        return ".*";
                    }
                    else
                    {
                        return Regex.Escape(s);
                    }
                });
                var regex = new Regex(regexPattern.ToLowerInvariant());

                foreach (var item in FontItems.Where(item => !regex.IsMatch(item.Font.Name.ToLowerInvariant())).ToArray())
                {
                    item.Visibility.Value = Visibility.Collapsed;
                }
                foreach (var item in UsedFonts.Where(item => !regex.IsMatch(item.Font.Name.ToLowerInvariant())).ToArray())
                {
                    item.Visibility.Value = Visibility.Collapsed;
                }
            }).AddTo(_disposables);
            OKCommand.Subscribe(() =>
            {
                OKIsClicked = true;
                UsedFonts.Remove(SelectedItem.Value);
                UsedFonts.Insert(0, SelectedItem.Value);

                WindowClose.Execute();
            }).AddTo(_disposables);
            SelectCommand.Subscribe(font =>
            {
                SetIsChecked(false);

                font.IsChecked.Value = true;
                SelectedItem.Value = font;
            }).AddTo(_disposables);
        }
        ~FontDialogViewModel()
        {
            Dispose();
        }


        public static List<FontItem> FontItems { get; } = FontManager.Default.LoadedFonts.Select(i => new FontItem(i)).ToList();
        public static List<FontItem> UsedFonts { get; } = new();
        public ReactivePropertySlim<FontItem> SelectedItem { get; }
        public ReactivePropertySlim<string> Search { get; } = new();
        public ReactivePropertySlim<string> SampleText { get; } = new();
        public ReactiveCommand OKCommand { get; } = new();
        public ReactiveCommand<FontItem> SelectCommand { get; } = new();
        public ReactiveCommand WindowClose { get; } = new();
        public bool OKIsClicked { get; private set; }

        private static void SetVisibility()
        {
            foreach (var item in FontItems)
            {
                item.Visibility.Value = Visibility.Visible;
            }

            foreach (var item in UsedFonts)
            {
                item.Visibility.Value = Visibility.Visible;
            }
        }
        private static void SetIsChecked(bool value)
        {
            foreach (var item in FontItems)
            {
                item.IsChecked.Value = value;
            }

            foreach (var item in UsedFonts)
            {
                item.IsChecked.Value = value;
            }
        }
        public void Dispose()
        {
            _disposables.Dispose();

            GC.SuppressFinalize(this);
        }
        private static void LoadUsedFonts()
        {
            var jsonFile = Path.Combine(AppContext.BaseDirectory, "user", "usedFonts.json");

            if (!File.Exists(jsonFile))
            {
                using var writter = File.CreateText(jsonFile);
                writter.Write("[\n    \n]");
            }
            var json = File.ReadAllText(jsonFile);

            foreach (var item in JsonSerializer.Deserialize<IEnumerable<string>>(json)?.Select(i => new Font(i)) ?? Array.Empty<Font>())
            {
                UsedFonts.Add(new(item));
            }
        }


        public record FontItem(Font Font)
        {
            public ReactiveProperty<bool> IsChecked { get; } = new();
            public ReactiveProperty<Visibility> Visibility { get; } = new(System.Windows.Visibility.Visible);
        }
    }
}