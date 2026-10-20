using System.Text.Json.Nodes;
using Avalonia.Headless.NUnit;
using Beutl.AgentHost;
using Beutl.Graphics.Transitions;
using Beutl.Services;
using ModelContextProtocol.Client;
using static Beutl.HeadlessUITests.AgentHostTestHelpers;

namespace Beutl.HeadlessUITests;

// get_schema in the editor lists what its library lists, as the installed server does (McpToolBindingTests).
[TestFixture]
public sealed class AgentHostSchemaTests
{
    [AvaloniaTest]
    public async Task Get_schema_lists_every_library_type()
    {
        string directory = CreateDirectory("schema-host-");
        try
        {
            await using AgentHostEndpoint host = CreateHost(TestShell.Editor, directory, "schema-token");
            await host.StartAsync();
            await using McpClient client = await ConnectAsync(host);

            JsonObject schema = Success(await client.CallToolAsync(
                "get_schema", new Dictionary<string, object?> { ["includeProperties"] = false }));
            string[] listed = [.. schema["types"]!.AsArray().Select(type => type!["type"]!.GetValue<string>())];
            string[] library =
            [
                .. typeof(KnownLibraryItemFormats).GetFields()
                    .SelectMany(field => LibraryService.Current.GetTypesFromFormat((string)field.GetValue(null)!))
                    .Select(type => type.FullName!)
            ];

            Assert.Multiple(() =>
            {
                Assert.That(library, Does.Contain(typeof(CrossDissolveTransition).FullName));
                Assert.That(library.Except(listed), Is.Empty);
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
