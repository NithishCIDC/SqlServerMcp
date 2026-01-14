# SqlServerMcp (MCP Server for Microsoft SQL Server)

This project is an **MCP (Model Context Protocol) server** written in **C#/.NET** that connects to **Microsoft SQL Server** and exposes database operations as MCP tools.

It runs using **STDIO transport**, so it works directly with MCP clients like:
- Cursor
- Claude Desktop
- other MCP-compatible clients

---

## ✅ Features

### 🔹 SQL Tools
- `list_tables` → list all user tables
- `describe_table` → show columns for a given table (`schema.table`)
- `get_database_schema` → full schema (tables + columns + PK + FK relationships)
- `search_schema` → search tables/columns by keyword (ex: customer, order, invoice)
- `run_query` → execute **read-only SELECT** queries with auto row limiting
- `explain_query` → generate **estimated execution plan** and show warnings/cost (no execution)

### 🔒 Safety
- Only `SELECT` queries are allowed
- Dangerous keywords are blocked (`DROP`, `DELETE`, `UPDATE`, etc.)
- Auto adds `TOP N` if missing (prevents huge reads)
- Limits maximum TOP value (default 500)

---

## 📁 Project Structure

SqlServerMcp/
│
├── SqlServerMcp.csproj
├── Program.cs
├── appsettings.json
├── README.md
│
├── Tools/
│ ├── SqlTools.cs
│ └── QueryPlanParser.cs
│
└── Models/
└── SchemaModels.cs