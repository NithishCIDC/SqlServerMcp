# SqlServerMcp - MCP Server for Microsoft SQL Server

A **Model Context Protocol (MCP)** server built with **C# / .NET 10** that connects to **Microsoft SQL Server** and exposes database operations as MCP tools. It uses **STDIO transport** and works with any MCP-compatible client.

## Compatible Clients

- Claude Desktop
- Cursor
- VS Code (with MCP extensions)
- Any MCP-compatible client

## Features

### SQL Tools

| Tool | Description |
|------|-------------|
| `list_databases` | List all configured database connections |
| `list_tables` | List all user tables in a database |
| `describe_table` | Show columns for a table (format: `schema.table`) |
| `get_database_schema` | Full schema including tables, columns, primary keys, and foreign keys |
| `search_schema` | Search tables and columns by keyword |
| `run_query` | Execute read-only `SELECT` queries with automatic row limiting |
| `explain_query` | Show estimated execution plan with cost analysis and warnings |

### Safety

- Only `SELECT` queries are allowed
- Dangerous keywords are blocked (`DROP`, `DELETE`, `UPDATE`, `INSERT`, `ALTER`, `TRUNCATE`, `EXEC`, etc.)
- Automatically adds `TOP N` if missing to prevent unbounded reads
- Caps `TOP` value to a configurable maximum
- 30-second query timeout

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Microsoft SQL Server (any edition)

## Setup

### 1. Clone the repository

```bash
git clone https://github.com/NithishCIDC/SqlServerMcp.git
cd SqlServerMcp
```

### 2. Configure connection strings

Edit `SqlServerMcp/appsettings.json` and add your SQL Server connection strings:

```json
{
  "ConnectionStrings": {
    "MyDatabase": "Server=localhost;Database=MyDB;Trusted_Connection=True;TrustServerCertificate=True;"
  },
  "Query": {
    "DefaultTop": 1000,
    "MaxTop": 500000
  }
}
```

You can add multiple named connections. The first one is used as the default.

### 3. Build and run

```bash
cd SqlServerMcp
dotnet build
dotnet run
```

### 4. Configure your MCP client

Add the server to your MCP client configuration. Connection strings and query settings can be passed via environment variables using the `env` block (recommended to keep credentials out of source code).

**Claude Desktop** (`claude_desktop_config.json`):

```json
{
  "mcpServers": {
    "sqlserver": {
      "command": "dotnet",
      "args": ["run", "--project", "C:/path/to/SqlServerMcp/SqlServerMcp/SqlServerMcp.csproj"],
      "env": {
        "ConnectionStrings__MyDatabase": "Server=localhost;Database=MyDB;Trusted_Connection=True;TrustServerCertificate=True;",
        "Query__DefaultTop": "100",
        "Query__MaxTop": "5000"
      }
    }
  }
}
```

**Cursor** (`.cursor/mcp.json`):

```json
{
  "mcpServers": {
    "sqlserver": {
      "command": "dotnet",
      "args": ["run", "--project", "C:/path/to/SqlServerMcp/SqlServerMcp/SqlServerMcp.csproj"],
      "env": {
        "ConnectionStrings__MyDatabase": "Server=localhost;Database=MyDB;Trusted_Connection=True;TrustServerCertificate=True;"
      }
    }
  }
}
```

You can configure multiple databases by adding more `ConnectionStrings__<name>` entries. The `__` (double underscore) maps to `:` in .NET configuration (e.g., `ConnectionStrings__Prod` becomes `ConnectionStrings:Prod`).

## Configuration

| Setting | Default | Description |
|---------|---------|-------------|
| `Query:DefaultTop` | `1000` | Rows returned when query has no `TOP` clause |
| `Query:MaxTop` | `500000` | Maximum allowed `TOP` value (higher values get capped) |

## Project Structure

```
SqlServerMcp/
├── Program.cs                 # Host setup, DI, MCP server registration
├── appsettings.json           # Connection strings and query settings
├── Tools/
│   ├── SqlTools.cs            # MCP tool implementations
│   └── QueryPlanParser.cs     # Execution plan XML parser
└── Models/
    └── SchemaModels.cs        # Data models (schemas, tables, columns, etc.)
```

## Tech Stack

- .NET 10
- [ModelContextProtocol SDK](https://github.com/modelcontextprotocol/csharp-sdk) (v0.5.0-preview.1)
- Microsoft.Data.SqlClient
- Microsoft.Extensions.Hosting

## License

MIT
