using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

using BEditor.Drawing;
using BEditor.Packaging;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BEditor.ViewModels
{
    public sealed class FontDialogViewModel : IDisposable
    {
        internal readonly CompositeDisposable _disposables = new();

        static FontDialogViewModel()
        {
            LoadRecentFonts();
        }

        public FontDialogViewModel(Font selected)
        {
            SelectedItem = new(new FontItem(selected));

            foreach (var font in FontItems)
            {
                font.IsChecked.Value = font.Font == selected;
                font.IsVisible.Value = true;
            }

            foreach (var font_ in RecentFonts)
            {
                font_.IsChecked.Value = false;
                font_.IsVisible.Value = true;
            }

            Search.Subscribe(str =>
            {
                if (str is null) return;

                SetVisibility();

                if (string.IsNullOrWhiteSpace(str)) return;

                var regexPattern = Regex.Replace(str, ".", m =>
                {
                    var s = m.Value;
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
                    item.IsVisible.Value = false;
                }
                foreach (var item in RecentFonts.Where(item => !regex.IsMatch(item.Font.Name.ToLowerInvariant())).ToArray())
                {
                    item.IsVisible.Value = false;
                }
            }).AddTo(_disposables);
            OKCommand.Subscribe(() =>
            {
                OKIsClicked = true;
                RecentFonts.Remove(SelectedItem.Value);
                RecentFonts.Insert(0, SelectedItem.Value);

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

        public static List<FontItem> RecentFonts { get; } = new();

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
                item.IsVisible.Value = true;
            }

            foreach (var item in RecentFonts)
            {
                item.IsVisible.Value = true;
            }
        }

        private static void SetIsChecked(bool value)
        {
            foreach (var item in FontItems)
            {
                item.IsChecked.Value = value;
            }

            foreach (var item in RecentFonts)
            {
                item.IsChecked.Value = value;
            }
        }

        public void Dispose()
        {
            _disposables.Dispose();
            SaveRecentFonts();
            GC.SuppressFinalize(this);
        }

        private static void LoadRecentFonts()
        {
            var jsonFile = Path.Combine(ServicesLocator.GetUserFolder(), "recentFonts.json");

            if (!File.Exists(jsonFile))
            {
                using var writter = File.CreateText(jsonFile);
                writter.Write("[\n    \n]");
            }
            var json = File.ReadAllText(jsonFile);

            foreach (var item in JsonSerializer.Deserialize<IEnumerable<string>>(json, PackageFile._serializerOptions)?.Select(i => new Font(i)) ?? Array.Empty<Font>())
            {
                RecentFonts.Add(new(item));
            }
        }

        private static void SaveRecentFonts()
        {
            var jsonFile = Path.Combine(ServicesLocator.GetUserFolder(), "recentFonts.json");
            using var stream = new FileStream(jsonFile, FileMode.Create);
            using var writer = new Utf8JsonWriter(stream, Serialize._options);

            writer.WriteStartArray();

            foreach (var item in RecentFonts)
            {
                writer.WriteStringValue(item.Font.Filename);
            }

            writer.WriteEndArray();
        }

        public record FontItem(Font Font)
        {
            public ReactiveProperty<bool> IsChecked { get; } = new();
            public ReactiveProperty<bool> IsVisible { get; } = new(true);
        }
    }
}