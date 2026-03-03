using System;
using System.Collections.Generic;
using System.Text;

namespace SqlServerMcp.Models
{
    public class SqlToolsOptions
    {
        // Dictionary to hold name -> connection string
        public Dictionary<string, string> Connections { get; set; } = new();

        // Fallback/Default connection (optional, usually the first one)
        public string DefaultConnectionName { get; set; } = "";
        public int DefaultTop { get; set; } = 100;
        public int MaxTop { get; set; } = 500;
    }

    public class DatabaseSchema
    {
        public List<TableSchema> Tables { get; set; } = new();
        public List<ForeignKeySchema> ForeignKeys { get; set; } = new();
    }

    public class TableSchema
    {
        public string Schema { get; set; } = "";
        public string Name { get; set; } = "";

        public List<ColumnSchema> Columns { get; set; } = new();
        public List<string> PrimaryKeys { get; set; } = new();
    }

    public class ColumnSchema
    {
        public string Name { get; set; } = "";
        public string DataType { get; set; } = "";
        public string IsNullable { get; set; } = "";
        public int? MaxLength { get; set; }
    }

    public class ForeignKeySchema
    {
        public string FromSchema { get; set; } = "";
        public string FromTable { get; set; } = "";
        public string FromColumn { get; set; } = "";

        public string ToSchema { get; set; } = "";
        public string ToTable { get; set; } = "";
        public string ToColumn { get; set; } = "";
    }

    public class SchemaSearchResult
    {
        public string Schema { get; set; } = "";
        public string Table { get; set; } = "";
        public string? Column { get; set; }
        public string? DataType { get; set; }
        public bool IsColumnMatch { get; set; }

        public string FullName => Column == null
            ? $"{Schema}.{Table}"
            : $"{Schema}.{Table}.{Column}";
    }

    public class QueryExplanation
    {
        public string Summary { get; set; } = "";
        public decimal? EstimatedCost { get; set; }
        public List<string> Warnings { get; set; } = new();
        public string? PlanXmlSnippet { get; set; } // small snippet only
    }

}
