using Beutl.Services;

if (args.Length != 2)
{
    return 2;
}

var store = new TelemetryIdentityStore(args[1]);
switch (args[0])
{
    case "get":
        Console.Write(store.GetOrCreate().InstallationId);
        return 0;
    case "read":
        Console.Write(store.TryRead()?.InstallationId ?? "null");
        return 0;
    case "reset":
        store.Reset();
        Console.Write("reset");
        return 0;
    default:
        return 2;
}
