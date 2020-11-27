using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Markdig;

namespace BEditor.Page.Pages
{
    public partial class Index
    {
        public Index()
        {
            CardText = Markdown.ToHtml(@$"
# License

* [OpenCV]({Consts.OpenCV_License_Link})
* [OpenToolKit]({Consts.OpenTK_License_Link})
* [BEditor]({Consts.BEditor_License_Link})

# News

## BEditor 0.0.3

* [マイルストーン]({Consts.BEditor_003_MileStone_Link})

## BEditor 0.0.2

* [ダウンロード]({Consts.BEditor_002_Link})

## BEditor 0.0.1

* [ダウンロード]({Consts.BEditor_001_Link})

");
        }

        public string CardText { get; set; }
    }
}
