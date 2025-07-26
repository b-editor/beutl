using Beutl.Synchronization.Server.Hubs;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
    options.MaximumReceiveMessageSize = 1024 * 1024; // 1MB
    options.StreamBufferCapacity = 10;
});

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("http://localhost:5000", "https://localhost:5001")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

builder.Services.AddLogging(logging =>
{
    logging.ClearProviders();
    logging.AddConsole();
    
    if (builder.Environment.IsDevelopment())
    {
        logging.SetMinimumLevel(LogLevel.Debug);
    }
    else
    {
        logging.SetMinimumLevel(LogLevel.Information);
    }
});

var app = builder.Build();

// Configure pipeline
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.UseRouting();
app.UseCors();

// Map SignalR hub
app.MapHub<ProjectSyncHub>("/synchub");

// Health check endpoint
app.MapGet("/health", () => new { 
    Status = "Healthy", 
    Timestamp = DateTime.UtcNow,
    Version = "1.0.0"
});

// API endpoint to get server info
app.MapGet("/api/info", () => new
{
    ServerName = "Beutl Synchronization Server",
    Version = "1.0.0",
    SignalREndpoint = "/synchub",
    SupportedProtocols = new[] { "json", "messagepack" },
    MaxMessageSize = 1024 * 1024
});

app.Logger.LogInformation("Beutl Synchronization Server starting...");
app.Logger.LogInformation("SignalR Hub available at: /synchub");

app.Run();