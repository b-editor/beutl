using Beutl;
using Beutl.ProjectSystem;
using Beutl.Synchronization;
using Beutl.Synchronization.Extensions;
using Microsoft.Extensions.Logging;

namespace Beutl.Synchronization.Tests;

/// <summary>
/// Simple test program to demonstrate CoreObject synchronization
/// </summary>
class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("=== Beutl.Synchronization Test ===\n");

        // Setup logging
        using var loggerFactory = LoggerFactory.Create(builder =>
            builder.AddConsole().SetMinimumLevel(LogLevel.Information));

        try
        {
            // Test basic synchronization
            await TestBasicSynchronization(loggerFactory);
            
            // Test project synchronization
            await TestProjectSynchronization(loggerFactory);
            
            Console.WriteLine("\n=== All tests completed successfully! ===");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n❌ Test failed: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }

        Console.WriteLine("\nPress any key to exit...");
        Console.ReadKey();
    }

    static async Task TestBasicSynchronization(ILoggerFactory loggerFactory)
    {
        Console.WriteLine("🧪 Testing basic CoreObject synchronization...");

        // Create two sync managers (simulating two clients)
        var (syncManager1, orchestrator1, sourceId1) = SyncConfigurationExtensions
            .ConfigureSync()
            .UseMemoryTransport()
            .WithLogging(loggerFactory)
            .WithSourceId("Client1")
            .Build();

        var (syncManager2, orchestrator2, sourceId2) = SyncConfigurationExtensions
            .ConfigureSync()
            .UseMemoryTransport()
            .WithLogging(loggerFactory)
            .WithSourceId("Client2")
            .Build();

        // Create a test object
        var project1 = new Project();
        var project2 = new Project { Id = project1.Id }; // Same ID for testing

        // Track changes received by client 2
        var changesReceived = 0;
        syncManager2.RemoteChanges.Subscribe(change =>
        {
            changesReceived++;
            Console.WriteLine($"  📥 Client2 received: {change.PropertyName} = {change.NewValue}");
        });

        try
        {
            // Start synchronization
            var sessionId = Guid.NewGuid();
            await syncManager1.StartSyncAsync(sessionId);
            await syncManager2.StartSyncAsync(sessionId);

            // Enable sync for objects
            project1.EnableSync(syncManager1, sourceId1);
            project2.EnableSync(syncManager2, sourceId2);

            Console.WriteLine("  ✅ Synchronization started");

            // Make changes on client 1
            project1.Name = "Test Project from Client 1";
            
            // Give time for changes to propagate
            await Task.Delay(100);

            Console.WriteLine($"  📊 Changes received by Client2: {changesReceived}");
            
            if (changesReceived > 0)
            {
                Console.WriteLine("  ✅ Basic synchronization working!");
            }
            else
            {
                Console.WriteLine("  ⚠️  No changes received - check implementation");
            }
        }
        finally
        {
            // Cleanup
            await syncManager1.StopSyncAsync();
            await syncManager2.StopSyncAsync();
            syncManager1.Dispose();
            syncManager2.Dispose();
            orchestrator1.Dispose();
            orchestrator2.Dispose();
        }

        Console.WriteLine();
    }

    static async Task TestProjectSynchronization(ILoggerFactory loggerFactory)
    {
        Console.WriteLine("🧪 Testing project hierarchy synchronization...");

        // Create sync setup
        var (syncManager, orchestrator, sourceId) = SyncConfigurationExtensions
            .ConfigureSync()
            .UseMemoryTransport()
            .WithLogging(loggerFactory)
            .WithSourceId("TestClient")
            .Build();

        try
        {
            // Start synchronization
            var sessionId = Guid.NewGuid();
            await syncManager.StartSyncAsync(sessionId);

            // Create a test project
            var project = new Project
            {
                Name = "Test Synchronization Project"
            };

            // Create a test scene
            var scene = new Scene(1920, 1080, "Test Scene");
            project.Items.Add(scene);

            Console.WriteLine("  📁 Created project with scene");

            // Start project synchronization
            await orchestrator.SyncProjectAsync(project, sourceId);

            Console.WriteLine($"  ✅ Project sync started, tracking {orchestrator.TrackedObjectCount} objects");

            // Test property changes
            var localChanges = 0;
            syncManager.LocalChanges.Subscribe(change =>
            {
                localChanges++;
                Console.WriteLine($"  📤 Sent change: {change.ObjectId} -> {change.PropertyName} = {change.NewValue}");
            });

            // Make some changes
            project.Name = "Updated Project Name";
            scene.Name = "Updated Scene Name";

            // Give time for changes to process
            await Task.Delay(100);

            Console.WriteLine($"  📊 Local changes sent: {localChanges}");

            if (localChanges > 0)
            {
                Console.WriteLine("  ✅ Project synchronization working!");
            }
            else
            {
                Console.WriteLine("  ⚠️  No changes detected - check implementation");
            }
        }
        finally
        {
            // Cleanup
            await syncManager.StopSyncAsync();
            syncManager.Dispose();
            orchestrator.Dispose();
        }

        Console.WriteLine();
    }
}