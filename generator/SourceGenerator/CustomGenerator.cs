using System.Collections.Generic;
using System.Linq;
using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace SourceGenerator
{
    [Generator]
    public class CustomGenerator : ISourceGenerator
    {
        public void Initialize(GeneratorInitializationContext context)
        {
            context.RegisterForSyntaxNotifications(() => new MySyntaxReceiver());
        }

        public void Execute(GeneratorExecutionContext context)
        {
            if (context.SyntaxContextReceiver is not MySyntaxReceiver receiver) return;

            context.AddSource("GenerateTargetAttribute.g.cs", @"
using System;

[AttributeUsage(AttributeTargets.Class)]
public class GenerateTargetAttribute : Attribute
{
}
");

            // group the fields by class, and generate the source
            foreach (IGrouping<INamedTypeSymbol, GeneratedEditingProperty> group in receiver.Fields.GroupBy(f => f.Field.ContainingType))
            {
                string classSource = ProcessClass(group.Key, group.ToList(), context);
                context.AddSource($"{group.Key.Name}.g.cs", SourceText.From(classSource, Encoding.UTF8));
            }
        }
        private string ProcessClass(INamedTypeSymbol classSymbol, List<GeneratedEditingProperty> fields, GeneratorExecutionContext context)
        {
            if (!classSymbol.ContainingSymbol.Equals(classSymbol.ContainingNamespace, SymbolEqualityComparer.Default))
            {
                return null;
            }

            var namespaceName = classSymbol.ContainingNamespace.ToDisplayString();
            // begin building the generated source
            var source = new StringBuilder($@"namespace {namespaceName}
{{
#nullable disable
    public partial class {classSymbol.Name}
    {{
");

            // create properties for each field 
            foreach (var property in fields)
            {
                ProcessField(source, property);
            }

            source.Append(@"    }
}");
            return source.ToString();
        }
        private void ProcessField(StringBuilder source, GeneratedEditingProperty property)
        {
            var propertyName = property.Name;

            if (property.IsDirect)
            {
                var field = $"_{propertyName.ToLowerInvariant()}";
                source.Append($@"        private {property.ValueType} {field};
");
                source.Append($@"        public {property.ValueType} {propertyName}
        {{
            get => {field};
            set => SetAndRaise({property.Field.Name}, ref {field}, value);
        }}
");
            }
            else
            {
                source.Append($@"        public {property.ValueType} {propertyName}
        {{
            get => GetValue({property.Field.Name});
            set => SetValue({property.Field.Name}, value);
        }}
");
            }
        }

        class MySyntaxReceiver : ISyntaxContextReceiver
        {
            public List<GeneratedEditingProperty> Fields { get; } = new();

            public void OnVisitSyntaxNode(GeneratorSyntaxContext context)
            {
                if (context.Node is FieldDeclarationSyntax fieldDeclarationSyntax)
                {
                    foreach (VariableDeclaratorSyntax variable in fieldDeclarationSyntax.Declaration.Variables)
                    {
                        IFieldSymbol fieldSymbol = context.SemanticModel.GetDeclaredSymbol(variable) as IFieldSymbol;

                        if (fieldSymbol.Type.AllInterfaces.Any(i => i.Name.Contains("IEditingProperty")) &&
                            fieldDeclarationSyntax.Declaration.Type is GenericNameSyntax &&
                            fieldSymbol.ContainingType.GetAttributes().Any(ad => ad.AttributeClass.ToDisplayString() == "GenerateTarget"))
                        {
                            Fields.Add(new(fieldSymbol.Name.Replace("Property", ""), fieldSymbol, fieldSymbol.Type.Name.Contains("Direct")));
                        }
                    }
                }
            }
        }
    }

    public class GeneratedEditingProperty
    {
        public GeneratedEditingProperty(string name, IFieldSymbol field, bool isDirect)
        {
            (Name, Field, IsDirect) = (name, field, isDirect);
            ValueType = (field.Type as INamedTypeSymbol).TypeArguments.Last();
        }

        public bool IsDirect { get; }
        public string Name { get; }
        public IFieldSymbol Field { get; }
        public ITypeSymbol ValueType { get; }
    }
}