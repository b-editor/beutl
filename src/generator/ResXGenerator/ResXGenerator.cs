using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace ResXGenerator
{
    [Generator]
    public class ResXGenerator : ISourceGenerator
    {
        public void Execute(GeneratorExecutionContext context)
        {
            var resFiles = context.AdditionalFiles.Where(at => at.Path.EndsWith(".resx"));
            foreach (var resFile in resFiles)
            {
                ProcessFile(resFile, context);
            }
        }

        private void ProcessFile(AdditionalText file, GeneratorExecutionContext context)
        {
            // try and load the settings file
            var xmlDoc = new XmlDocument();
            string text = file.GetText(context.CancellationToken).ToString();
            try
            {
                xmlDoc.LoadXml(text);
            }
            catch
            {
                return;
            }

            if (!context.AnalyzerConfigOptions.GetOptions(file).TryGetValue("build_metadata.additionalfiles.NameSpace", out string nameSpace))
                return;

            var className = Path.GetFileNameWithoutExtension(file.Path);
            var sb = new StringBuilder($@"using System;

namespace {nameSpace}
{{
    public class {className}
    {{
        private static global::System.Resources.ResourceManager resourceMan;

        public static global::System.Resources.ResourceManager ResourceManager
        {{
            get
            {{
                if (object.ReferenceEquals(resourceMan, null))
                {{
                    resourceMan = new global::System.Resources.ResourceManager(""{nameSpace}.{className}"", typeof({className}).Assembly);
                }}
                return resourceMan;
            }}
            set
            {{
                resourceMan = value;
            }}
        }}

        public static global::System.Globalization.CultureInfo Culture {{ get; set; }}
");
            foreach (var item in ReadResources(xmlDoc))
            {
                sb.AppendLine();
                sb.AppendLine($@"        /// <summary>");
                sb.AppendLine($@"        /// {item.Value.Replace("\n", "").Replace("\r", "")} に類似しているローカライズされた文字列を検索します。");
                sb.AppendLine($@"        /// </summary>");
                sb.AppendLine($@"        public static string {item.Key} => GetString(""{item.Key}"");");
            }

            sb.Append(@"
        private static string GetString(string name)
        {
            try
            {
                return ResourceManager.GetString(name, Culture);
            }
            catch
            {
                return name;
            }
        }");

            sb.Append(@"
    }
}");
            context.AddSource($"{className}.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
        }

        public void Initialize(GeneratorInitializationContext context)
        {
        }

        private static IEnumerable<KeyValuePair<string, string>> ReadResources(XmlDocument document)
        {
            foreach (var element in document.DocumentElement.ChildNodes.OfType<XmlElement>())
            {
                if (element.Name == "data")
                {
                    var key = element.GetAttribute("name");
                    var value = element.ChildNodes.OfType<XmlElement>().Single(i => i.Name == "value").FirstChild.InnerText;

                    yield return new KeyValuePair<string, string>(key, value);
                }
            }
        }
    }
}
