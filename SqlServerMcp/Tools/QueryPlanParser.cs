using SqlServerMcp.Models;
using System.Globalization;
using System.Xml.Linq;

namespace SqlServerMcp.Tools
{
    public static class QueryPlanParser
    {
        public static QueryExplanation Parse(string planXml)
        {
            try
            {
                var doc = XDocument.Parse(planXml);

                // SQL Server plan XML uses namespaces
                XNamespace ns = doc.Root?.Name.Namespace ?? "";

                // Total cost is often in RelOp/@EstimatedTotalSubtreeCost
                var costs = doc.Descendants(ns + "RelOp")
                    .Select(x => x.Attribute("EstimatedTotalSubtreeCost")?.Value)
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .Select(v => decimal.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : (decimal?)null)
                    .Where(d => d != null)
                    .Select(d => d!.Value)
                    .ToList();

                var maxCost = costs.Count > 0 ? costs.Max() : (decimal?)null;

                var warnings = new List<string>();

                // Detect scans (common performance issue)
                var scans = doc.Descendants(ns + "RelOp")
                    .Select(x => x.Attribute("PhysicalOp")?.Value)
                    .Where(v => v != null && (v.Contains("Scan", StringComparison.OrdinalIgnoreCase)))
                    .Distinct()
                    .ToList();

                foreach (var s in scans)
                    warnings.Add($"Plan contains operation: {s}");

                // Missing index hints
                var missingIndexGroups = doc.Descendants(ns + "MissingIndexGroup").ToList();
                if (missingIndexGroups.Count > 0)
                    warnings.Add("Plan suggests missing indexes.");

                // Hash match (join) is not always bad, but can indicate big join
                var hashMatch = doc.Descendants(ns + "RelOp")
                    .Any(x => (x.Attribute("PhysicalOp")?.Value ?? "").Contains("Hash Match", StringComparison.OrdinalIgnoreCase));
                if (hashMatch)
                    warnings.Add("Plan uses Hash Match (may be heavy on large datasets).");

                // Create short snippet for AI (avoid huge XML)
                var snippet = planXml.Length <= 1500 ? planXml : planXml[..1500] + "...";

                var summary = maxCost is null
                    ? "Estimated plan parsed. No cost detected."
                    : $"Estimated plan parsed. Max subtree cost ≈ {maxCost}.";

                return new QueryExplanation
                {
                    Summary = summary,
                    EstimatedCost = maxCost,
                    Warnings = warnings,
                    PlanXmlSnippet = snippet
                };
            }
            catch
            {
                // fallback
                return new QueryExplanation
                {
                    Summary = "Failed to parse plan XML (but plan was generated).",
                    EstimatedCost = null,
                    Warnings = new List<string>(),
                    PlanXmlSnippet = planXml.Length <= 1500 ? planXml : planXml[..1500] + "..."
                };
            }
        }
    }
}
