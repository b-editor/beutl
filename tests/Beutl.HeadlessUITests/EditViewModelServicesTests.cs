using Avalonia.Headless.NUnit;
using Beutl.Editor.Services;
using Beutl.Media.Proxy;
using Beutl.NodeGraph.Generative;
using Beutl.Testing.Headless;
using Beutl.ViewModels;

namespace Beutl.HeadlessUITests;

// The editor answers each registered type with an instance of that type, and nothing else.
[TestFixture]
public sealed class EditViewModelServicesTests
{
    // Absent in the test shell, which starts no proxy store and has no main window for the AI clients.
    private static readonly Type[] s_absentInTests =
    [
        typeof(IProxyStore), typeof(IProxyResolver), typeof(IProxyJobQueue), typeof(ProxyEvictionService),
        typeof(IProxyStoreCapInfo), typeof(IGenerativePromptLibrary), typeof(IGenerativeModelCatalog),
        typeof(IGenerativeNodeExecutor),
    ];

    [AvaloniaTest]
    public async Task Every_registered_service_is_an_instance_of_its_type()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync("service-registry");

        string[] wrong =
        [
            .. editor.ServiceTypes
                .Where(type => editor.GetService(type) is { } service
                    ? !type.IsInstanceOfType(service)
                    : !s_absentInTests.Contains(type))
                .Select(type => type.Name)
        ];

        Assert.Multiple(() =>
        {
            Assert.That(editor.ServiceTypes, Is.SupersetOf(s_absentInTests));
            Assert.That(wrong, Is.Empty);
            // The chain answered this concrete type with its write-admission wrapper.
            Assert.That(editor.GetService(typeof(ElementClipboardService)), Is.Null);
        });
    }
}
