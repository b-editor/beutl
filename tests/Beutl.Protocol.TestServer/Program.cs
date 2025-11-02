using Beutl.Protocol.Transport;

var builder = WebApplication.CreateBuilder(args);

Console.WriteLine("=== Beutl Protocol Test Server ===");
Console.WriteLine("Stateless SignalR Hub - No server-side state maintained");

// Add SignalR services
builder.Services.AddSignalR();

// Add CORS for development
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

app.UseCors("AllowAll");

// Map SignalR hub (no state provider needed - fully stateless)
app.MapHub<SynchronizationHub>("/sync");

app.MapGet("/", () => "Beutl Protocol Test Server is running. Connect to /sync for SignalR. (Stateless)");

Console.WriteLine("Server is ready and listening...\n");
Console.WriteLine("Note: This server is stateless. Clients manage their own state.");
Console.WriteLine("The server only acts as a message broker between clients.\n");

app.Run();
