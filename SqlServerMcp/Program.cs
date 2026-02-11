using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SqlServerMcp.Models;
using SqlServerMcp.Tools;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.AddConsole(options =>
{
    // MCP expects logs in stderr
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

// Load appsettings.json
builder.Configuration
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

// Load the section "ConnectionStrings" into a dictionary
var connections = builder.Configuration.GetSection("ConnectionStrings")
    .Get<Dictionary<string, string>>() ?? new Dictionary<string, string>();

if (connections.Count == 0)
    throw new Exception("No connection strings found in appsettings.json");

// ✅ Register SqlTools as singleton
var options = new SqlToolsOptions
{
    Connections = connections,
    DefaultConnectionName = connections.Keys.First(), // Default to the first one found
    DefaultTop = int.Parse(builder.Configuration["Query:DefaultTop"] ?? "100"),
    MaxTop = int.Parse(builder.Configuration["Query:MaxTop"] ?? "500")
};

builder.Services.AddSingleton(options);

// ✅ MCP server with STDIO transport + explicitly register SqlTools
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<SqlTools>();  // Changed from WithToolsFromAssembly()

await builder.Build().RunAsync();