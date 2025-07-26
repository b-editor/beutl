using Beutl;
using Beutl.ProjectSystem;
using Beutl.Synchronization;
using Beutl.Synchronization.Extensions;
using Beutl.Synchronization.Transport;
using Microsoft.Extensions.Logging;

namespace Beutl.Examples.MultiClientSyncDemo;

/// <summary>
/// Interactive demo showing multi-client real-time synchronization
/// </summary>
class Program
{
    private static readonly string[] ClientNames = { "Alice", "Bob", "Charlie", "Diana" };
    private static readonly ConsoleColor[] ClientColors = 
    { 
        ConsoleColor.Cyan, ConsoleColor.Green, ConsoleColor.Yellow, ConsoleColor.Magenta 
    };

    static async Task Main(string[] args)
    {
        Console.Clear();
        WriteColoredLine("🎭 Beutl Multi-Client Synchronization Demo", ConsoleColor.White);
        Console.WriteLine();

        // Setup logging (quieter for demo)
        using var loggerFactory = LoggerFactory.Create(builder =>
            builder.AddConsole().SetMinimumLevel(LogLevel.Warning));

        try
        {
            // Get server URL
            string serverUrl = args.Length > 0 ? args[0] : "http://localhost:5234/synchub";
            WriteColoredLine($"🌐 Server: {serverUrl}", ConsoleColor.Gray);
            Console.WriteLine();

            // Choose demo mode
            Console.WriteLine("Choose demo mode:");
            Console.WriteLine("1. Memory Transport Demo (no server required)");
            Console.WriteLine("2. SignalR Transport Demo (requires server)");
            Console.Write("Enter choice (1 or 2): ");
            
            var choice = Console.ReadLine();
            Console.WriteLine();

            if (choice == "1")
            {
                await RunMemoryDemo(loggerFactory);
            }
            else
            {
                await RunSignalRDemo(serverUrl, loggerFactory);
            }
        }
        catch (Exception ex)
        {
            WriteColoredLine($"❌ Demo failed: {ex.Message}", ConsoleColor.Red);
        }

        WriteColoredLine("\nDemo completed! Press any key to exit...", ConsoleColor.Gray);
        Console.ReadKey();
    }

    static async Task RunMemoryDemo(ILoggerFactory loggerFactory)
    {
        WriteColoredLine("🧠 Memory Transport Demo - Simulating 4 Clients", ConsoleColor.White);
        WriteColoredLine("This demo shows how multiple clients can collaborate in real-time.", ConsoleColor.Gray);
        Console.WriteLine();

        var sessionId = Guid.NewGuid();
        var clients = new List<(string Name, ISyncManager SyncManager, ProjectSyncOrchestrator Orchestrator, Project Project)>();

        try
        {
            // Create 4 clients
            for (int i = 0; i < 4; i++)
            {
                var (syncManager, orchestrator, _) = SyncConfigurationExtensions
                    .ConfigureSync()
                    .UseMemoryTransport()
                    .WithLogging(loggerFactory)
                    .WithSourceId(ClientNames[i])
                    .Build();

                var project = new Project 
                { 
                    Name = "Collaborative Project",
                    Variables = 
                    {
                        ["LastEditor"] = ClientNames[i],
                        ["EditCount"] = "0"
                    }
                };

                // Setup change notification
                var clientIndex = i;
                syncManager.RemoteChanges.Subscribe(change =>
                {
                    if (change.ChangeSource != ClientNames[clientIndex])
                    {
                        WriteColoredLine(
                            $"  📥 {ClientNames[clientIndex]} received: {change.PropertyName} = \"{change.NewValue}\" from {change.ChangeSource}", 
                            ClientColors[clientIndex]);
                    }
                });

                clients.Add((ClientNames[i], syncManager, orchestrator, project));
            }

            // Start synchronization for all clients
            WriteColoredLine("🔗 Connecting all clients to session...", ConsoleColor.White);
            foreach (var (name, syncManager, orchestrator, project) in clients)
            {
                await syncManager.StartSyncAsync(sessionId);
                await orchestrator.SyncProjectAsync(project, name);
                WriteColoredLine($"  ✅ {name} connected", GetClientColor(name));
            }

            Console.WriteLine();
            WriteColoredLine("🎬 Starting collaborative editing simulation...", ConsoleColor.White);
            Console.WriteLine();

            // Simulate collaborative editing
            await SimulateCollaborativeEditing(clients);

            WriteColoredLine("\n✨ Memory demo completed!", ConsoleColor.Green);
        }
        finally
        {
            // Cleanup
            foreach (var (_, syncManager, orchestrator, _) in clients)
            {
                await syncManager.StopSyncAsync();
                syncManager.Dispose();
                orchestrator.Dispose();
            }
        }
    }

    static async Task RunSignalRDemo(string serverUrl, ILoggerFactory loggerFactory)
    {
        WriteColoredLine("📡 SignalR Transport Demo - Real Network Synchronization", ConsoleColor.White);
        WriteColoredLine("This demo connects to a real SignalR server for synchronization.", ConsoleColor.Gray);
        Console.WriteLine();

        var config = new SyncTransportConfig
        {
            ServerUrl = serverUrl,
            ConnectionTimeoutMs = 10000,
            MaxReconnectAttempts = 3
        };

        var sessionId = Guid.NewGuid();
        var clients = new List<(string Name, ISyncManager SyncManager, ProjectSyncOrchestrator Orchestrator, Project Project)>();

        try
        {
            // Create 2 clients for SignalR demo
            for (int i = 0; i < 2; i++)
            {
                var (syncManager, orchestrator, _) = SyncConfigurationExtensions
                    .ConfigureSync()
                    .UseSignalRTransport(config)
                    .WithLogging(loggerFactory)
                    .WithSourceId(ClientNames[i])
                    .Build();

                var project = new Project 
                { 
                    Name = "SignalR Collaborative Project",
                    Variables = { ["LastEditor"] = ClientNames[i] }
                };

                // Setup change notification
                var clientIndex = i;
                syncManager.RemoteChanges.Subscribe(change =>
                {
                    if (change.ChangeSource != ClientNames[clientIndex])
                    {
                        WriteColoredLine(
                            $"  📡 {ClientNames[clientIndex]} received via SignalR: {change.PropertyName} = \"{change.NewValue}\" from {change.ChangeSource}", 
                            ClientColors[clientIndex]);
                    }
                });

                clients.Add((ClientNames[i], syncManager, orchestrator, project));
            }

            // Connect to SignalR server
            WriteColoredLine("🔗 Connecting to SignalR server...", ConsoleColor.White);
            foreach (var (name, syncManager, orchestrator, project) in clients)
            {
                await syncManager.StartSyncAsync(sessionId);
                await orchestrator.SyncProjectAsync(project, name);
                WriteColoredLine($"  ✅ {name} connected to SignalR", GetClientColor(name));
            }

            Console.WriteLine();
            WriteColoredLine("🎬 Starting SignalR collaborative editing...", ConsoleColor.White);
            Console.WriteLine();

            // Simulate SignalR collaborative editing
            await SimulateCollaborativeEditing(clients);

            WriteColoredLine("\n🌐 SignalR demo completed!", ConsoleColor.Green);
        }
        catch (Exception ex)
        {
            WriteColoredLine($"❌ SignalR demo failed: {ex.Message}", ConsoleColor.Red);
            if (ex.Message.Contains("Unable to connect"))
            {
                WriteColoredLine("\n💡 To run the SignalR server:", ConsoleColor.Yellow);
                WriteColoredLine("   dotnet run --project src/Beutl.Synchronization.Server", ConsoleColor.Yellow);
            }
        }
        finally
        {
            // Cleanup
            foreach (var (_, syncManager, orchestrator, _) in clients)
            {
                await syncManager.StopSyncAsync();
                syncManager.Dispose();
                orchestrator.Dispose();
            }
        }
    }

    static async Task SimulateCollaborativeEditing(
        List<(string Name, ISyncManager SyncManager, ProjectSyncOrchestrator Orchestrator, Project Project)> clients)
    {
        var random = new Random();
        var editActions = new[]
        {
            "Renamed project",
            "Updated settings", 
            "Added new scene",
            "Modified timeline",
            "Adjusted effects",
            "Changed colors",
            "Updated audio",
            "Refined animation"
        };

        for (int round = 1; round <= 5; round++)
        {
            WriteColoredLine($"Round {round}:", ConsoleColor.White);

            foreach (var (name, _, _, project) in clients)
            {
                // Simulate editing delay
                await Task.Delay(random.Next(500, 1500));

                // Make a change
                var action = editActions[random.Next(editActions.Length)];
                var editCount = int.Parse(project.Variables.GetValueOrDefault("EditCount", "0")) + 1;
                
                project.Name = $"Project: {action} by {name}";
                project.Variables["LastEditor"] = name;
                project.Variables["EditCount"] = editCount.ToString();

                WriteColoredLine($"  ✏️  {name}: {action}", GetClientColor(name));

                // Wait for synchronization
                await Task.Delay(200);
            }

            Console.WriteLine();
            await Task.Delay(1000);
        }

        // Show final state
        WriteColoredLine("📊 Final Project States:", ConsoleColor.White);
        foreach (var (name, _, _, project) in clients)
        {
            WriteColoredLine(
                $"  {name}: \"{project.Name}\" (Last: {project.Variables["LastEditor"]}, Edits: {project.Variables["EditCount"]})", 
                GetClientColor(name));
        }
    }

    static ConsoleColor GetClientColor(string clientName)
    {
        var index = Array.IndexOf(ClientNames, clientName);
        return index >= 0 ? ClientColors[index] : ConsoleColor.White;
    }

    static void WriteColoredLine(string text, ConsoleColor color)
    {
        var originalColor = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.WriteLine(text);
        Console.ForegroundColor = originalColor;
    }
}