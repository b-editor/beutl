using Microsoft.CodeAnalysis;

namespace ApiKeyGenerator
{
    [Generator]
    public sealed class Generator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            context.RegisterPostInitializationOutput(callback: GenerateInitialCode);
        }

        private void GenerateInitialCode(IncrementalGeneratorPostInitializationContext context)
        {
            CancellationToken token = context.CancellationToken;
            token.ThrowIfCancellationRequested();
            context.AddSource(hintName: "Constants.g.cs", source: $@"namespace BeUtl.Models;

public static partial class Constants
{{
    public const string FirebaseKey = ""{Environment.GetEnvironmentVariable("BEUTL_FIREBASE_KEY")}"";
}}
");
        }
    }
}
