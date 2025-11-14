using System.Reactive.Linq;
using Beutl;
using Beutl.Operators.Source;
using Beutl.ProjectSystem;
using Beutl.Protocol.Operations;
using Beutl.Protocol.Operations.Property;
using Beutl.Protocol.Queries;
using Beutl.Protocol.Synchronization;

// Create test data
var root = new Scene
{
    Name = "Test Scene",
    Duration = TimeSpan.FromMinutes(5),
    FileName = Path.GetTempFileName()
};

root.Children.Add(new Element
{
    Start = TimeSpan.FromSeconds(1),
    Length = TimeSpan.FromMinutes(1),
    Operation = { Children = { new RectOperator() } },
    FileName = Path.GetTempFileName()
});

var queryExecutor = new QueryExecutor();

// Simple query
var schema1 = new QuerySchema(new[] { new QueryField("Name"), new QueryField("Duration") });

var result1 = queryExecutor.Execute(root, schema1);

// Nested query
var schema2 = new QuerySchema(new[]
{
    new QueryField("Name"),
    new QueryField("Duration"),
    new QueryField("Children", new[] { new QueryField("Start"), new QueryField("Length") })
});

var result2 = queryExecutor.Execute(root, schema2);

var sequenceGenerator = new OperationSequenceGenerator();
var subscription = new QuerySubscription(root, schema2, sequenceGenerator);

// Skip the second publisher for now to debug
// var allOperations = new List<SyncOperation>();
// var testPublisher = new CoreObjectOperationPublisher(null, root, sequenceGenerator);

int updateCount = 0;
var disposable = subscription.Updates.Subscribe(update =>
{
    updateCount++;
    string? path = update.Operation switch
    {
        UpdatePropertyValueOperation updateOp => updateOp.PropertyPath,
        _ => null
    };
});

root.Name = "Changed Scene";
Thread.Sleep(100);

root.FrameSize = new(1080, 1920);
Thread.Sleep(100);

Thread.Sleep(100);

root.Children[0].Start = TimeSpan.FromSeconds(5);
Thread.Sleep(100);

disposable.Dispose();
// pubDisposable2.Dispose();
// testPublisher.Dispose();
subscription.Dispose();

var publisher = new CoreObjectOperationPublisher(null, root, sequenceGenerator);
var operations = new List<SyncOperation>();

var pubDisposable = publisher.Operations.Subscribe(op =>
{
    operations.Add(op);
    Console.WriteLine($"Published: {op.GetType().Name}");
});

root.Duration = TimeSpan.FromMinutes(10);
Thread.Sleep(100);

pubDisposable.Dispose();
publisher.Dispose();
