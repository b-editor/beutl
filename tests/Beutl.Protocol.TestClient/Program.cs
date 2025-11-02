using Beutl.Protocol.TestClient;
using Beutl.Protocol.Operations;
using Beutl.Protocol.Synchronization;
using Beutl.Protocol.Transport;

Console.WriteLine("=== Beutl Protocol Test Client ===\n");

// Create test data model
var testData = new TestDataModel
{
    Name = "Test Object",
    Count = 0
};

testData.Items.Add("Item 1");
testData.Items.Add("Item 2");

// Create sequence number generator
var sequenceGenerator = new OperationSequenceGenerator();

// Create transport client
var hubUrl = args.Length > 0 ? args[0] : "http://localhost:5110/sync";
Console.WriteLine($"Connecting to: {hubUrl}");

var transport = new SignalRTransportClient(hubUrl);

// Subscribe to incoming operations
transport.IncomingOperations.Subscribe(
    operation =>
    {
        Console.WriteLine($"[RECEIVED] Operation: {operation.GetType().Name} (Seq: {operation.SequenceNumber})");
    },
    error => Console.WriteLine($"[ERROR] {error.Message}"),
    () => Console.WriteLine("[COMPLETED] Operation stream ended")
);

// Subscribe to state changes
transport.StateChanged += (sender, args) =>
{
    Console.WriteLine($"[STATE] {args.OldState} -> {args.NewState}");
};

try
{
    // Connect to server
    await transport.ConnectAsync();
    Console.WriteLine("[CONNECTED] Successfully connected to server\n");

    // Create operation publisher
    var publisher = new CoreObjectOperationPublisher(
        observer: null,
        obj: testData,
        sequenceNumberGenerator: sequenceGenerator
    );

    // Create operation applier for applying remote operations
    var applier = new OperationApplier();

    // Create remote synchronizer
    var remoteSynchronizer = new RemoteSynchronizer(
        publisher,
        transport,
        applier
    );

    Console.WriteLine("Test scenarios:");
    Console.WriteLine("1. Update Name property");
    Console.WriteLine("2. Increment Counter");
    Console.WriteLine("3. Add item to list");
    Console.WriteLine("4. Remove item from list");
    Console.WriteLine("5. Show current state");
    Console.WriteLine("q. Quit\n");

    bool running = true;
    while (running)
    {
        Console.Write("Enter command: ");
        var input = Console.ReadLine();

        switch (input?.ToLower())
        {
            case "1":
                Console.Write("Enter new name: ");
                var newName = Console.ReadLine();
                testData.Name = newName ?? "Default";
                Console.WriteLine($"[LOCAL] Name updated to: {testData.Name}");
                break;

            case "2":
                testData.Count++;
                Console.WriteLine($"[LOCAL] Count incremented to: {testData.Count}");
                break;

            case "3":
                Console.Write("Enter item to add: ");
                var newItem = Console.ReadLine();
                if (!string.IsNullOrEmpty(newItem))
                {
                    testData.Items.Add(newItem);
                    Console.WriteLine($"[LOCAL] Added item: {newItem}");
                }
                break;

            case "4":
                if (testData.Items.Count > 0)
                {
                    Console.Write($"Enter index to remove (0-{testData.Items.Count - 1}): ");
                    if (int.TryParse(Console.ReadLine(), out int index) && index >= 0 && index < testData.Items.Count)
                    {
                        var removed = testData.Items[index];
                        testData.Items.RemoveAt(index);
                        Console.WriteLine($"[LOCAL] Removed item: {removed}");
                    }
                    else
                    {
                        Console.WriteLine("[ERROR] Invalid index");
                    }
                }
                else
                {
                    Console.WriteLine("[INFO] No items to remove");
                }
                break;

            case "5":
                Console.WriteLine("\n=== Current State ===");
                Console.WriteLine($"Name: {testData.Name}");
                Console.WriteLine($"Count: {testData.Count}");
                Console.WriteLine($"Items ({testData.Items.Count}):");
                for (int i = 0; i < testData.Items.Count; i++)
                {
                    Console.WriteLine($"  [{i}] {testData.Items[i]}");
                }
                Console.WriteLine("====================\n");
                break;

            case "q":
                running = false;
                break;

            default:
                Console.WriteLine("[ERROR] Unknown command");
                break;
        }

        // Small delay to allow operations to propagate
        await Task.Delay(100);
    }

    // Cleanup
    Console.WriteLine("\n[CLEANUP] Disconnecting...");
    remoteSynchronizer.Dispose();
    publisher.Dispose();
    await transport.DisconnectAsync();
    transport.Dispose();

    Console.WriteLine("[DONE] Test client stopped");
}
catch (Exception ex)
{
    Console.WriteLine($"\n[FATAL ERROR] {ex.Message}");
    Console.WriteLine(ex.StackTrace);
}
