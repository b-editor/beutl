using System.Text.Json;
using Microsoft.JavaScript.NodeApi;
using Microsoft.JavaScript.NodeApi.Runtime;

namespace Beutl.Agents.Worker;

internal sealed class EmbeddedNode(NodeEmbeddingPlatform platform, NodeEmbeddingThreadRuntime runtime) : IDisposable
{
    private JSReference? _module;

    internal static async Task<EmbeddedNode> StartAsync(Action<string> emitEvent, Func<string, Task<string>> requestHost)
    {
        string directory = AppContext.BaseDirectory;
        string script = Path.Combine(directory, "bridge.mjs");
        if (!File.Exists(script))
            throw new FileNotFoundException("The agent Node bridge bundle is missing.", script);

        var platform = new NodeEmbeddingPlatform(new NodeEmbeddingPlatformSettings());
        NodeEmbeddingThreadRuntime runtime;
        try
        {
            runtime = platform.CreateThreadRuntime(directory, new NodeEmbeddingRuntimeSettings
            {
                MainScript = """
                    globalThis.require = require('node:module').createRequire(process.execPath);
                    // ESM can initialize node:process stdin even when the module only imports cwd.
                    // Let the managed protocol reader own the real pipe.
                    Object.defineProperty(process, 'stdin', { value: require('node:stream').Readable.from([]) });
                    globalThis.console = new (require('node:console').Console)({ stdout: process.stderr, stderr: process.stderr });
                    process.stdout.write = process.stderr.write.bind(process.stderr);
                    """
            });
        }
        catch
        {
            platform.Dispose();
            throw;
        }

        var node = new EmbeddedNode(platform, runtime);
        try
        {
            await runtime.RunAsync(async () =>
            {
                var module = await runtime.ImportAsync(script, esModule: true);
                var emit = JSValue.CreateFunction("emitEventJson", args =>
                {
                    emitEvent((string)args[0]);
                    return JSValue.Undefined;
                });
                var request = JSValue.CreateFunction("requestHostJson", args =>
                {
                    string json = (string)args[0];
                    return new JSPromise(async resolve =>
                    {
                        string response = await requestHost(json);
                        resolve((JSValue)response);
                    });
                });

                module.CallMethod("initialize", emit, request);
                node._module = new JSReference(module);
            }).ConfigureAwait(false);
            return node;
        }
        catch
        {
            node.Dispose();
            throw;
        }
    }

    internal Task<JsonElement> InvokeAsync(JsonElement command)
    {
        string json = command.GetRawText();
        return runtime.RunAsync(async () =>
        {
            var result = (JSPromise)_module!.GetValue().CallMethod("handleCommand", json);
            string response = await result.AsTask(value => (string)value);
            return JsonSerializer.Deserialize<JsonElement>(response);
        });
    }

    public void Dispose()
    {
        runtime.Run(() => _module?.Dispose());
        runtime.Dispose();
        platform.Dispose();
    }
}
