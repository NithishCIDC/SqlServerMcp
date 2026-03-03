using Microsoft.Data.SqlClient;
using System.Text.RegularExpressions;
using System.ComponentModel;
using ModelContextProtocol.Server;
using SqlServerMcp.Models;

namespace SqlServerMcp.Tools
{
    [McpServerToolType]
    public class SqlTools
    {
        private readonly SqlToolsOptions _options;

        public SqlTools(SqlToolsOptions options)
        {
            _options = options;
        }

        // -------------------------------------------------------------------------
        // 1. Connection Helper & Database Lister
        // -------------------------------------------------------------------------

        [McpServerTool, Description("List available database names configured in this server. Use these names in other tools to query specific databases.")]
        public List<string> ListDatabases()
        {
            return _options.Connections.Keys.ToList();
        }

        private string GetConnectionString(string? dbName)
        {
            // If no DB name provided, use the default
            if (string.IsNullOrWhiteSpace(dbName))
                return _options.Connections[_options.DefaultConnectionName];

            // Try to find the specific DB connection string
            if (_options.Connections.TryGetValue(dbName, out var connStr))
                return connStr;

            // Fail gracefully if the AI hallucinates a DB name
            throw new ArgumentException($"Database '{dbName}' not found. Available databases: {string.Join(", ", _options.Connections.Keys)}");
        }

        // -------------------------------------------------------------------------
        // 2. Schema Tools
        // -------------------------------------------------------------------------

        [McpServerTool, Description("List all user tables in the database.")]
        public async Task<List<string>> ListTables(
            [Description("Optional: The named database to query. Defaults to the primary database.")] string? databaseName = null)
        {
            var connStr = GetConnectionString(databaseName);

            const string sql = @"
            SELECT TABLE_SCHEMA + '.' + TABLE_NAME
            FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_TYPE='BASE TABLE'
            ORDER BY TABLE_SCHEMA, TABLE_NAME;";

            var results = new List<string>();

            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            await using var cmd = new SqlCommand(sql, conn);
            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
                results.Add(reader.GetString(0));

            return results;
        }

        [McpServerTool, Description("Describe columns of a specific table. Input format: schema.table (example: dbo.Users)")]
        public async Task<List<ColumnSchema>> DescribeTable(
            [Description("The table name in schema.table format (example: dbo.Users)")] string table,
            [Description("Optional: The named database to query.")] string? databaseName = null)
        {
            var connStr = GetConnectionString(databaseName);
            var (schema, name) = ParseTableName(table);

            const string sql = @"
            SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE, CHARACTER_MAXIMUM_LENGTH
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = @schema AND TABLE_NAME = @table
            ORDER BY ORDINAL_POSITION;";

            var results = new List<ColumnSchema>();

            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@schema", schema);
            cmd.Parameters.AddWithValue("@table", name);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                results.Add(new ColumnSchema
                {
                    Name = reader.GetString(0),
                    DataType = reader.GetString(1),
                    IsNullable = reader.GetString(2),
                    MaxLength = reader.IsDBNull(3) ? null : reader.GetInt32(3)
                });
            }

            if (results.Count == 0)
                throw new InvalidOperationException($"Table not found or has no columns: {schema}.{name} in database '{databaseName ?? "default"}'");

            return results;
        }

        [McpServerTool, Description("Return full database schema: tables, columns, primary keys and foreign keys. Use cautiously on large DBs.")]
        public async Task<DatabaseSchema> GetDatabaseSchema(
            [Description("Optional: The named database to query.")] string? databaseName = null)
        {
            var connStr = GetConnectionString(databaseName);
            var schema = new DatabaseSchema();

            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            // 1) Tables
            const string tablesSql = @"
            SELECT TABLE_SCHEMA, TABLE_NAME
            FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_TYPE = 'BASE TABLE'
            ORDER BY TABLE_SCHEMA, TABLE_NAME;";

            await using (var cmd = new SqlCommand(tablesSql, conn))
            await using (var reader = await cmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    schema.Tables.Add(new TableSchema
                    {
                        Schema = reader.GetString(0),
                        Name = reader.GetString(1)
                    });
                }
            }

            // 2) Columns
            const string columnsSql = @"
            SELECT TABLE_SCHEMA, TABLE_NAME, COLUMN_NAME, DATA_TYPE, IS_NULLABLE, CHARACTER_MAXIMUM_LENGTH
            FROM INFORMATION_SCHEMA.COLUMNS
            ORDER BY TABLE_SCHEMA, TABLE_NAME, ORDINAL_POSITION;";

            await using (var cmd = new SqlCommand(columnsSql, conn))
            await using (var reader = await cmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    var tableSchema = reader.GetString(0);
                    var tableName = reader.GetString(1);

                    var table = schema.Tables.FirstOrDefault(t => t.Schema == tableSchema && t.Name == tableName);
                    if (table == null) continue;

                    table.Columns.Add(new ColumnSchema
                    {
                        Name = reader.GetString(2),
                        DataType = reader.GetString(3),
                        IsNullable = reader.GetString(4),
                        MaxLength = reader.IsDBNull(5) ? null : reader.GetInt32(5)
                    });
                }
            }

            // 3) Primary Keys
            const string pkSql = @"
            SELECT
                s.name AS SchemaName,
                t.name AS TableName,
                c.name AS ColumnName
            FROM sys.indexes i
            INNER JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
            INNER JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
            INNER JOIN sys.tables t ON i.object_id = t.object_id
            INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
            WHERE i.is_primary_key = 1
            ORDER BY s.name, t.name, ic.key_ordinal;";

            await using (var cmd = new SqlCommand(pkSql, conn))
            await using (var reader = await cmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    var t = schema.Tables.FirstOrDefault(x =>
                        x.Schema == reader.GetString(0) &&
                        x.Name == reader.GetString(1));

                    if (t == null) continue;
                    t.PrimaryKeys.Add(reader.GetString(2));
                }
            }

            // 4) Foreign Keys
            const string fkSql = @"
            SELECT
                sch1.name AS FromSchema,
                tab1.name AS FromTable,
                col1.name AS FromColumn,
                sch2.name AS ToSchema,
                tab2.name AS ToTable,
                col2.name AS ToColumn
            FROM sys.foreign_key_columns fkc
            INNER JOIN sys.tables tab1 ON tab1.object_id = fkc.parent_object_id
            INNER JOIN sys.schemas sch1 ON tab1.schema_id = sch1.schema_id
            INNER JOIN sys.columns col1 ON col1.object_id = tab1.object_id AND col1.column_id = fkc.parent_column_id
            INNER JOIN sys.tables tab2 ON tab2.object_id = fkc.referenced_object_id
            INNER JOIN sys.schemas sch2 ON tab2.schema_id = sch2.schema_id
            INNER JOIN sys.columns col2 ON col2.object_id = tab2.object_id AND col2.column_id = fkc.referenced_column_id
            ORDER BY sch1.name, tab1.name;";

            await using (var cmd = new SqlCommand(fkSql, conn))
            await using (var reader = await cmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    schema.ForeignKeys.Add(new ForeignKeySchema
                    {
                        FromSchema = reader.GetString(0),
                        FromTable = reader.GetString(1),
                        FromColumn = reader.GetString(2),
                        ToSchema = reader.GetString(3),
                        ToTable = reader.GetString(4),
                        ToColumn = reader.GetString(5)
                    });
                }
            }

            return schema;
        }

        [McpServerTool, Description("Search tables and columns by keyword. Helps find correct schema quickly.")]
        public async Task<List<SchemaSearchResult>> SearchSchema(
            [Description("Keyword to search across table & column names")] string keyword,
            [Description("Optional: The named database to query.")] string? databaseName = null,
            [Description("Max number of results to return (default 30)")] int maxResults = 30)
        {
            var connStr = GetConnectionString(databaseName);
            if (string.IsNullOrWhiteSpace(keyword)) throw new ArgumentException("keyword cannot be empty.");
            keyword = keyword.Trim();

            const string sql = @"
            SELECT TOP (@maxResults)
                c.TABLE_SCHEMA,
                c.TABLE_NAME,
                c.COLUMN_NAME,
                c.DATA_TYPE,
                CAST(1 AS bit) AS IsColumnMatch
            FROM INFORMATION_SCHEMA.COLUMNS c
            WHERE c.COLUMN_NAME LIKE '%' + @keyword + '%'
               OR c.TABLE_NAME LIKE '%' + @keyword + '%'
               OR c.TABLE_SCHEMA LIKE '%' + @keyword + '%'

            UNION ALL

            SELECT TOP (@maxResults)
                t.TABLE_SCHEMA,
                t.TABLE_NAME,
                CAST(NULL AS nvarchar(128)) AS COLUMN_NAME,
                CAST(NULL AS nvarchar(128)) AS DATA_TYPE,
                CAST(0 AS bit) AS IsColumnMatch
            FROM INFORMATION_SCHEMA.TABLES t
            WHERE t.TABLE_TYPE = 'BASE TABLE'
              AND (t.TABLE_NAME LIKE '%' + @keyword + '%'
               OR t.TABLE_SCHEMA LIKE '%' + @keyword + '%')

            ORDER BY TABLE_SCHEMA, TABLE_NAME, COLUMN_NAME;";

            var results = new List<SchemaSearchResult>();

            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@keyword", keyword);
            cmd.Parameters.AddWithValue("@maxResults", Math.Clamp(maxResults, 1, 200));

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                results.Add(new SchemaSearchResult
                {
                    Schema = reader.GetString(0),
                    Table = reader.GetString(1),
                    Column = reader.IsDBNull(2) ? null : reader.GetString(2),
                    DataType = reader.IsDBNull(3) ? null : reader.GetString(3),
                    IsColumnMatch = reader.GetBoolean(4)
                });
            }

            return results;
        }

        // -------------------------------------------------------------------------
        // 3. Query Tools
        // -------------------------------------------------------------------------

        [McpServerTool, Description("Execute a SQL SELECT query and return results (read-only). Auto-limits rows.")]
        public async Task<List<Dictionary<string, object?>>> RunQuery(
            [Description("SQL SELECT query (only SELECT allowed)")] string query,
            [Description("Optional: The named database to query.")] string? databaseName = null)
        {
            var connStr = GetConnectionString(databaseName);

            if (string.IsNullOrWhiteSpace(query)) throw new ArgumentException("Query cannot be empty.");
            if (!IsSafeSelectQuery(query)) throw new InvalidOperationException("Only safe SELECT queries are allowed.");

            // ✅ Use options for Top limits
            query = EnsureTopLimit(query, _options.DefaultTop, _options.MaxTop);

            var results = new List<Dictionary<string, object?>>();

            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            await using var cmd = new SqlCommand(query, conn) { CommandTimeout = 30 };
            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var row = new Dictionary<string, object?>();
                for (int i = 0; i < reader.FieldCount; i++)
                    row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                results.Add(row);
            }

            return results;
        }

        [McpServerTool, Description("Explain a SQL SELECT query using estimated execution plan. Returns cost & plan warnings.")]
        public async Task<QueryExplanation> ExplainQuery(
            [Description("SQL SELECT query to explain (only SELECT allowed)")] string query,
            [Description("Optional: The named database to query.")] string? databaseName = null)
        {
            var connStr = GetConnectionString(databaseName);

            if (string.IsNullOrWhiteSpace(query)) throw new ArgumentException("Query cannot be empty.");
            if (!IsSafeSelectQuery(query)) throw new InvalidOperationException("Only safe SELECT queries are allowed.");

            var planXml = "";

            await using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            await using (var showPlanCmd = new SqlCommand("SET SHOWPLAN_XML ON;", conn))
                await showPlanCmd.ExecuteNonQueryAsync();

            try
            {
                await using var cmd = new SqlCommand(query, conn) { CommandTimeout = 30 };
                var result = await cmd.ExecuteScalarAsync();
                planXml = result?.ToString() ?? "";
            }
            finally
            {
                await using var offCmd = new SqlCommand("SET SHOWPLAN_XML OFF;", conn);
                await offCmd.ExecuteNonQueryAsync();
            }

            if (string.IsNullOrWhiteSpace(planXml))
                return new QueryExplanation { Summary = "No plan returned.", EstimatedCost = null, Warnings = new List<string>() };

            return QueryPlanParser.Parse(planXml);
        }


        // -------------------------------------------------------------------------
        // 4. Static Helpers
        // -------------------------------------------------------------------------

        private static (string schema, string name) ParseTableName(string input)
        {
            var parts = input.Split('.', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2)
                throw new ArgumentException("Table must be in format schema.table (example: dbo.Users)");
            return (parts[0], parts[1]);
        }

        private static bool IsSafeSelectQuery(string query)
        {
            var q = query.Trim().ToLowerInvariant();
            if (!Regex.IsMatch(q, @"^\s*select\b", RegexOptions.IgnoreCase)) return false;

            var blocked = new[] { "insert", "update", "delete", "drop", "alter", "truncate", "merge", "exec", "execute", "grant", "revoke", "create" };
            return !blocked.Any(k => Regex.IsMatch(q, $@"\b{k}\b", RegexOptions.IgnoreCase));
        }

        private static string EnsureTopLimit(string query, int defaultTop, int maxTop)
        {
            var q = query.Trim();
            var topMatch = Regex.Match(q, @"select\s+top\s+(?<n>\d+)", RegexOptions.IgnoreCase);
            if (topMatch.Success)
            {
                var n = int.Parse(topMatch.Groups["n"].Value);
                if (n > maxTop)
                    q = Regex.Replace(q, @"select\s+top\s+\d+", $"SELECT TOP {maxTop}", RegexOptions.IgnoreCase);
                return q;
            }

            return Regex.Replace(q, @"^\s*select\s+", $"SELECT TOP {defaultTop} ", RegexOptions.IgnoreCase);
        }
    }
}