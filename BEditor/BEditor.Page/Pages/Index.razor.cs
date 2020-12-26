using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

using Markdig;

namespace BEditor.Page.Pages
{
    public partial class Index
    {
        protected override async Task OnInitializedAsync()
        {
            await base.OnInitializedAsync();

            using var client = new HttpClient();
            CardText = Markdown.ToHtml(await client.GetStringAsync("https://raw.githubusercontent.com/indigo-san/BEditor/main/README.md"));
        }

        public string CardText { get; set; }
    }
}
