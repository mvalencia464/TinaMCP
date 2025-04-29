using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Server;
using TinaMcpServer; // Namespace for our custom classes

var builder = Host.CreateApplicationBuilder(args);

// --- Configuration ---
// Reads appsettings.json + environment variables + command line args
builder.Configuration.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);

// --- Logging ---
builder.Logging.ClearProviders(); // Optional: Remove default providers
builder.Logging.AddConfiguration(builder.Configuration.GetSection("Logging"));
builder.Logging.AddConsole(options => 
{
    // Log everything to standard error as recommended for StdioTransport
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

// --- Dependency Injection ---

// Configure and register TinaProjectConfig from appsettings.json
builder.Services.Configure<TinaProjectConfig>(builder.Configuration.GetSection("TinaProject"));
// Register it as a singleton for injection into tools
builder.Services.AddSingleton(sp => sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<TinaProjectConfig>>().Value);

// --- MCP Server Setup ---
builder.Services
    .AddMcpServer(options => { })
    .WithStdioServerTransport() // Use standard I/O for communication
    .WithToolsFromAssembly();   // Discover tools (like TinaCmsContentTools) in this assembly
    // Alternatively, explicitly add tool types: .WithTool<TinaCmsContentTools>();

var app = builder.Build();

// Run the host, which starts the MCP server and listens for client connections
await app.RunAsync(); 