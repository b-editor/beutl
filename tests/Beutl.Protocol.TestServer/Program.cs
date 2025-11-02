using Beutl.Protocol.Transport;

var builder = WebApplication.CreateBuilder(args);

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

// Map SignalR hub
app.MapHub<SynchronizationHub>("/sync");

app.MapGet("/", () => "Beutl Protocol Test Server is running. Connect to /sync for SignalR.");

app.Run();
