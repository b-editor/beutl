using Beutl;
using Beutl.ProjectSystem;
using Beutl.Synchronization;
using Beutl.Synchronization.Extensions;
using Beutl.Synchronization.Transport;
using Microsoft.Extensions.Logging;

namespace Beutl.Synchronization.SignalR.Tests;

/// <summary>
/// SignalR synchronization test program
/// </summary>
class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("=== Beutl SignalR Synchronization Test ===\n");

        // Setup logging
        using var loggerFactory = LoggerFactory.Create(builder =>
            builder.AddConsole().SetMinimumLevel(LogLevel.Information));

        try
        {
            // Check for server URL argument
            string serverUrl = args.Length > 0 ? args[0] : "http://localhost:5234/synchub";
            Console.WriteLine($"🌐 Using SignalR server: {serverUrl}");

            if (args.Length > 1 && args[1] == "--server")
            {
                await RunAsServer();
                return;
            }

            // Test SignalR connection
            await TestSignalRConnection(serverUrl, loggerFactory);
            
            // Test multi-client synchronization
            await TestMultiClientSync(serverUrl, loggerFactory);
            
            Console.WriteLine("\n=== All SignalR tests completed! ===");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n❌ Test failed: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }

        Console.WriteLine("\nPress any key to exit...");
        Console.ReadKey();
    }

    static async Task TestSignalRConnection(string serverUrl, ILoggerFactory loggerFactory)
    {
        Console.WriteLine("🔌 Testing SignalR connection...");

        var config = new SyncTransportConfig
        {
            ServerUrl = serverUrl,
            ConnectionTimeoutMs = 10000,
            MaxReconnectAttempts = 3,
            ReconnectDelayMs = 1000
        };

        var (syncManager, orchestrator, sourceId) = SyncConfigurationExtensions
            .ConfigureSync()
            .UseSignalRTransport(config)
            .WithLogging(loggerFactory)
            .WithSourceId("TestClient1")
            .Build();

        try
        {
            // Monitor connection status
            syncManager.ConnectionStatusChanged.Subscribe(status =>
                Console.WriteLine($"  📡 Connection status: {status}"));

            // Test connection
            var sessionId = Guid.NewGuid();
            Console.WriteLine($"  🔗 Connecting to session: {sessionId}");
            
            await syncManager.StartSyncAsync(sessionId);
            Console.WriteLine("  ✅ Successfully connected to SignalR server!");

            // Test basic sync
            var project = new Project { Name = "SignalR Test Project" };
            
            var changesSent = 0;
            syncManager.LocalChanges.Subscribe(change =>
            {
                changesSent++;
                Console.WriteLine($"  📤 Sent change: {change.PropertyName} = {change.NewValue}");
            });

            await orchestrator.SyncProjectAsync(project, sourceId);
            
            // Make some changes
            project.Name = "Updated via SignalR";
            await Task.Delay(500); // Give time for sync

            Console.WriteLine($"  📊 Changes sent: {changesSent}");

            if (changesSent > 0)
            {
                Console.WriteLine("  ✅ SignalR synchronization working!");
            }
            else
            {
                Console.WriteLine("  ⚠️  No changes detected - check server connection");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ SignalR connection failed: {ex.Message}");
            
            if (ex.Message.Contains("Unable to connect"))
            {
                Console.WriteLine("  💡 Make sure the SignalR server is running:");
                Console.WriteLine("     dotnet run --project src/Beutl.Synchronization.Server");
            }
        }
        finally
        {
            await syncManager.StopSyncAsync();
            syncManager.Dispose();
            orchestrator.Dispose();
        }

        Console.WriteLine();
    }

    static async Task TestMultiClientSync(string serverUrl, ILoggerFactory loggerFactory)
    {
        Console.WriteLine("👥 Testing multi-client synchronization...");

        var config = new SyncTransportConfig
        {
            ServerUrl = serverUrl,
            ConnectionTimeoutMs = 10000
        };

        // Create two clients
        var (syncManager1, orchestrator1, _) = SyncConfigurationExtensions
            .ConfigureSync()
            .UseSignalRTransport(config)
            .WithLogging(loggerFactory)
            .WithSourceId("Client1")
            .Build();

        var (syncManager2, orchestrator2, _) = SyncConfigurationExtensions
            .ConfigureSync()
            .UseSignalRTransport(config)
            .WithLogging(loggerFactory)
            .WithSourceId("Client2")
            .Build();

        try
        {
            // Setup change tracking
            var client1Changes = 0;
            var client2Changes = 0;

            syncManager1.RemoteChanges.Subscribe(change =>
            {
                client1Changes++;
                Console.WriteLine($"  📥 Client1 received: {change.PropertyName} = {change.NewValue} from {change.ChangeSource}");
            });

            syncManager2.RemoteChanges.Subscribe(change =>
            {
                client2Changes++;
                Console.WriteLine($"  📥 Client2 received: {change.PropertyName} = {change.NewValue} from {change.ChangeSource}");
            });

            // Connect both clients to same session
            var sessionId = Guid.NewGuid();
            Console.WriteLine($"  🔗 Both clients connecting to session: {sessionId}");

            await syncManager1.StartSyncAsync(sessionId);
            await syncManager2.StartSyncAsync(sessionId);

            // Create projects with same ID for testing
            var project1 = new Project { Name = "Multi-Client Project" };
            var project2 = new Project { Id = project1.Id, Name = "Multi-Client Project" };

            await orchestrator1.SyncProjectAsync(project1, "Client1");
            await orchestrator2.SyncProjectAsync(project2, "Client2");

            Console.WriteLine("  ✅ Both clients connected and synchronized");

            // Test Client1 → Client2 communication
            Console.WriteLine("  🔄 Testing Client1 → Client2 sync...");
            project1.Name = "Changed by Client1";
            await Task.Delay(1000);

            // Test Client2 → Client1 communication  
            Console.WriteLine("  🔄 Testing Client2 → Client1 sync...");
            project2.Name = "Changed by Client2";
            await Task.Delay(1000);

            Console.WriteLine($"  📊 Client1 received {client1Changes} changes");
            Console.WriteLine($"  📊 Client2 received {client2Changes} changes");

            if (client1Changes > 0 && client2Changes > 0)
            {
                Console.WriteLine("  ✅ Multi-client synchronization working!");
            }
            else
            {
                Console.WriteLine("  ⚠️  Multi-client sync issues detected");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Multi-client test failed: {ex.Message}");
        }
        finally
        {
            await syncManager1.StopSyncAsync();
            await syncManager2.StopSyncAsync();
            syncManager1.Dispose();
            syncManager2.Dispose();
            orchestrator1.Dispose();
            orchestrator2.Dispose();
        }

        Console.WriteLine();
    }

    static async Task RunAsServer()
    {
        Console.WriteLine("🖥️  Starting Beutl Synchronization Server...");
        Console.WriteLine("This would start the SignalR server if this was a full server implementation.");
        Console.WriteLine("To run the actual server:");
        Console.WriteLine("  dotnet run --project src/Beutl.Synchronization.Server");
        Console.WriteLine();
        Console.WriteLine("Server endpoints:");
        Console.WriteLine("  - SignalR Hub: http://localhost:5234/synchub");
        Console.WriteLine("  - Health Check: http://localhost:5234/health");
        Console.WriteLine("  - Server Info: http://localhost:5234/api/info");
        
        await Task.Delay(1000);
    }
}