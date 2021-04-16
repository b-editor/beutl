using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reactive.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows.Documents;

using BEditor.Models;

using Markdig;

using Microsoft.Extensions.DependencyInjection;

using Neo.Markdig.Xaml;

using Reactive.Bindings;

namespace BEditor.ViewModels.StartWindowControl
{
    public class LearnViewModel
    {
        public LearnViewModel()
        {
            IsNotLoaded = IsLoaded.Select(i => !i).ToReadOnlyReactivePropertySlim();

            DownloadItems();
        }

        public ReactiveCollection<Item> Items { get; } = new();
        public ReactivePropertySlim<Item> SelectedItem { get; } = new();
        public ReactivePropertySlim<bool> IsLoaded { get; } = new();
        public ReadOnlyReactivePropertySlim<bool> IsNotLoaded { get; }

        private Task DownloadItems()
        {
            return Task.Run(async () =>
            {
                string Base_Url = "https://raw.githubusercontent.com/b-editor/LearnBEditor/main/";

                Base_Url += CultureInfo.CurrentCulture.Name + "/";

                var client = AppData.Current.ServiceProvider.GetRequiredService<HttpClient>();
                await using var stream = await client.GetStreamAsync(Base_Url + "index.json");

                var items = await JsonSerializer.DeserializeAsync<IEnumerable<Item>>(stream);

                if (items is not null)
                {
                    var isFirst = true;
                    foreach (var item in items)
                    {
                        foreach (var page in item.Pages)
                        {
                            var md_uri = new Uri(new Uri(Base_Url), page.Markdown);
                            page.MarkdownString = await client.GetStringAsync(md_uri);
                        }

                        if (isFirst)
                        {
                            item.IsSelected.Value = true;
                            SelectedItem.Value = item;

                            isFirst = false;
                        }

                        Items.AddOnScheduler(item);
                    }
                }

                IsLoaded.Value = true;
            });
        }

        public class Item
        {
            private ReactivePropertySlim<bool>? _isSelected;

            public string Header { get; set; } = "";
            public ReactiveCollection<Page> Pages { get; set; } = new();
            [JsonIgnore]
            public ReactivePropertySlim<bool> IsSelected => _isSelected ??= new();
        }

        public class Page
        {
            public string Header { get; set; } = "";
            // .mdへのパス
            public string Markdown { get; set; } = "";
            [JsonIgnore]
            public string MarkdownString { get; set; } = "";
            [JsonIgnore]
            public FlowDocument Document
            {
                get
                {
                    var doc = MarkdownXaml.ToFlowDocument(
                        MarkdownString,
                        new MarkdownPipelineBuilder()
                            .UseXamlSupportedExtensions()
                            .Build());

                    doc.Foreground = (System.Windows.Media.Brush)App.Current.FindResource("MaterialDesignBody");

                    foreach (var block in doc.Blocks)
                    {
                        block.Foreground = doc.Foreground;
                    }

                    return doc;
                }
            }
        }
    }
}