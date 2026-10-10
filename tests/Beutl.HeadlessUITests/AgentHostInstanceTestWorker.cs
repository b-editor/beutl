using System.Text.Json;
using Avalonia.Headless;
using Beutl.AgentHost;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;

namespace Beutl.HeadlessUITests;

// Separate headless application processes exercise the same discovery and forwarding path as
// multiple desktop processes, while isolating configuration from the developer's profile.
internal static class AgentHostInstanceTestWorker
{
    internal const string ReadyPrefix = "BEUTL_INSTANCE_READY ";
    internal const string Token = "instance-test-token";

    public static int Run(string directory, string name, int preferredPort)
    {
        string home = BeutlHomeIsolation.Begin("beutl-instance-worker");
        try
        {
            using HeadlessUnitTestSession session = HeadlessUnitTestSession.GetOrStartForAssembly(
                typeof(AgentHostInstanceTestWorker).Assembly);
            session.Dispatch(async () =>
            {
                await TestReset.ResetShellAsync();
                string projectRoot = Path.Combine(home, "project");
                Directory.CreateDirectory(projectRoot);
                Project project = (await TestShell.Project.CreateProject(640, 480, 30, 44100, name, projectRoot))!;
                Scene scene = project.Items.OfType<Scene>().Single();
                TestShell.Editor.ActivateTabItem(scene);
                await using var endpoint = new AgentHostEndpoint(
                    TestShell.Project, TestShell.Editor, preferredPort, Token, registryDirectory: directory);
                await endpoint.StartAsync();
                Console.WriteLine(ReadyPrefix + JsonSerializer.Serialize(new
                {
                    endpoint.InstanceId,
                    EndpointUri = endpoint.EndpointUri!.ToString(),
                    ProcessId = Environment.ProcessId,
                    SceneId = scene.Id.ToString()
                }));
                await Task.Run(() => Console.In.ReadLine());
                await endpoint.StopAsync();
                await TestReset.ResetShellAsync();
                return 0;
            }, CancellationToken.None).GetAwaiter().GetResult();
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
        finally { BeutlHomeIsolation.End(); }
    }
}
