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

using Neo.Markdig.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;

namespace BEditor.ViewModels.StartWindowControl
{
    public class LearnViewModel
    {
        public LearnViewModel()
        {
            IsNotLoaded = IsLoaded.Select(i => !i).ToReactiveProperty();

            DownloadItems();
        }

        public ReactiveCollection<Item> Items { get; } = new();
        public ReactiveProperty<Item> SelectedItem { get; } = new();
        public ReactiveProperty<bool> IsLoaded { get; } = new();
        public ReactiveProperty<bool> IsNotLoaded { get; }

        private Task DownloadItems()
        {
            return Task.Run(async () =>
            {
                string Base_Url = "https://raw.githubusercontent.com/b-editor/LearnBEditor/main/";

                Base_Url += CultureInfo.CurrentCulture.Name + "/";

                var client = AppData.Current.ServiceProvider.GetService<HttpClient>();
                if (client is null) return;
                await using var memory = new MemoryStream();
                var json = await client.GetStringAsync(Base_Url + "index.json");
                await memory.WriteAsync(Encoding.UTF8.GetBytes(json));

                var items = await JsonSerializer.DeserializeAsync<IEnumerable<Item>>(memory);

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
            private ReactiveProperty<bool>? _isSelected;

            public Item(string header, ReactiveCollection<Page> pages)
            {
                Header = header;
                Pages = pages;
            }

            public string Header { get; set; }
            public ReactiveCollection<Page> Pages { get; set; }
            [JsonIgnore]
            public ReactiveProperty<bool> IsSelected => _isSelected ??= new();
        }

        public class Page
        {
            public Page(string header, string markdown, string mdstr)
            {
                Header = header;
                Markdown = markdown;
                MarkdownString = mdstr;
            }

            public string Header { get; set; }
            // .mdへのパス
            public string Markdown { get; set; }
            [JsonIgnore]
            public string MarkdownString { get; set; }
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
