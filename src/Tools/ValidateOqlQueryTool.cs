// ============================================================================
// AIDE Lite - AI-powered IDE extension for Mendix Studio Pro
// Copyright (c) 2025-2026 Neel Desai / Golden Earth Software Consulting Inc.
// Tool: validate_oql_query — static OQL validation against the loaded domain model
// ============================================================================
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AideLite.ModelReaders;
using AideLite.Models;

namespace AideLite.Tools;

public class ValidateOqlQueryTool : IClaudeTool
{
    private readonly AppContextExtractor _extractor;
    private readonly DomainModelReader _domainModelReader;

    private static readonly HashSet<string> ReservedWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "From", "To", "Date", "Day", "Group", "Status", "Order", "User",
        "Time", "Year", "Month", "Select", "Where", "And", "Or", "Not",
        "In", "Like", "Between", "Null", "True", "False", "As", "Join",
        "Inner", "Outer", "Left", "Right", "On", "By", "Having", "Limit",
        "Offset", "Distinct", "Count", "Sum", "Avg", "Min", "Max", "Asc", "Desc"
    };

    // Matches FROM/JOIN Module.Entity AS alias
    private static readonly Regex EntityPattern = new(
        @"(?:FROM|JOIN)\s+(\w+)\.(\w+)\s+AS\s+(\w+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Matches alias.Attribute or alias."Attribute"
    private static readonly Regex AttributePattern = new(
        @"(\w+)\.(""?\w+""?)",
        RegexOptions.Compiled);

    // Matches association paths: alias/Module.Association/Module.Entity
    private static readonly Regex AssociationPathPattern = new(
        @"(\w+)/(\w+)\.(\w+)/(\w+)\.(\w+)",
        RegexOptions.Compiled);

    // Matches column aliases: AS AliasName (not entity aliases after FROM/JOIN...Entity)
    private static readonly Regex ColumnAliasPattern = new(
        @"(?:""?\w+""?)\s+AS\s+(\w+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Matches SQL-style ON conditions in JOINs
    private static readonly Regex SqlStyleJoinPattern = new(
        @"JOIN\s+\w+\.\w+\s+AS\s+\w+\s+ON\s+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public ValidateOqlQueryTool(AppContextExtractor extractor, DomainModelReader domainModelReader)
    {
        _extractor = extractor;
        _domainModelReader = domainModelReader;
    }

    public string Name => "validate_oql_query";

    public string Description =>
        "Validate an OQL query by checking that all entity, attribute, and association references exist in the app model. " +
        "Does not execute the query — checks names only. Use this after generating OQL to catch typos and incorrect references.";

    public JsonObject InputSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["oqlQuery"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "The OQL query to validate"
            }
        },
        ["required"] = new JsonArray("oqlQuery")
    };

    public ToolResult Execute(JsonObject input)
    {
        var oqlQuery = input["oqlQuery"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(oqlQuery))
            return ToolResult.Fail("oqlQuery is required");

        var errors = new List<string>();
        var warnings = new List<string>();

        // 1. Check for SQL-style JOINs
        if (SqlStyleJoinPattern.IsMatch(oqlQuery))
            errors.Add("SQL-style JOIN with ON clause detected. Use Mendix association path syntax: alias/Module.Association/Module.Entity");

        // 2. Extract Module.Entity references and build alias map
        var aliasToEntity = new Dictionary<string, (string Module, string Entity)>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in EntityPattern.Matches(oqlQuery))
        {
            var moduleName = match.Groups[1].Value;
            var entityName = match.Groups[2].Value;
            var alias = match.Groups[3].Value;

            aliasToEntity[alias] = (moduleName, entityName);

            // Validate entity exists
            var module = _extractor.FindModule(moduleName);
            if (module == null)
            {
                errors.Add($"Module '{moduleName}' not found (referenced as {moduleName}.{entityName})");
                continue;
            }

            var entity = _domainModelReader.GetEntityDetails(module, entityName);
            if (entity == null)
            {
                var suggestions = SuggestEntityName(module, entityName);
                var suggestion = suggestions != null ? $" — did you mean '{suggestions}'?" : "";
                errors.Add($"Entity '{entityName}' not found in module '{moduleName}'{suggestion}");
            }
        }

        // 3. Validate attribute references
        foreach (Match match in AttributePattern.Matches(oqlQuery))
        {
            var alias = match.Groups[1].Value;
            var attrName = match.Groups[2].Value.Trim('"');

            // Skip if alias is not a known entity alias (could be a function or keyword)
            if (!aliasToEntity.TryGetValue(alias, out var entityRef))
                continue;

            // Skip module-qualified references (Module.Entity, not alias.Attribute)
            if (EntityPattern.IsMatch($"FROM {alias}.{attrName} AS x"))
                continue;

            var module = _extractor.FindModule(entityRef.Module);
            if (module == null) continue; // Already reported

            var entity = _domainModelReader.GetEntityDetails(module, entityRef.Entity);
            if (entity == null) continue; // Already reported

            var attrExists = entity.Attributes.Any(a =>
                string.Equals(a.Name, attrName, StringComparison.OrdinalIgnoreCase));
            if (!attrExists)
            {
                var suggestion = SuggestAttributeName(entity.Attributes.Select(a => a.Name).ToList(), attrName);
                var suggestionText = suggestion != null ? $" — did you mean '{suggestion}'?" : "";
                errors.Add($"Attribute '{attrName}' not found on {entityRef.Module}.{entityRef.Entity} (alias '{alias}'){suggestionText}");
            }
        }

        // 4. Validate association paths
        foreach (Match match in AssociationPathPattern.Matches(oqlQuery))
        {
            var sourceAlias = match.Groups[1].Value;
            var assocModule = match.Groups[2].Value;
            var assocName = match.Groups[3].Value;
            var targetModule = match.Groups[4].Value;
            var targetEntity = match.Groups[5].Value;

            // Verify source alias exists in context
            if (!aliasToEntity.ContainsKey(sourceAlias))
                errors.Add($"Association path uses unknown alias '{sourceAlias}' — entity must be in FROM or previous JOIN");

            // Verify target entity exists
            var tgtModule = _extractor.FindModule(targetModule);
            if (tgtModule == null)
            {
                errors.Add($"Target module '{targetModule}' not found in association path {sourceAlias}/{assocModule}.{assocName}/{targetModule}.{targetEntity}");
                continue;
            }

            var tgtEntity = _domainModelReader.GetEntityDetails(tgtModule, targetEntity);
            if (tgtEntity == null)
            {
                var suggestions = SuggestEntityName(tgtModule, targetEntity);
                var suggestion = suggestions != null ? $" — did you mean '{suggestions}'?" : "";
                errors.Add($"Target entity '{targetEntity}' not found in module '{targetModule}'{suggestion}");
                continue;
            }

            // Verify association exists on source or target entity
            if (aliasToEntity.TryGetValue(sourceAlias, out var srcRef))
            {
                var srcModule = _extractor.FindModule(srcRef.Module);
                if (srcModule != null)
                {
                    var srcEntity = _domainModelReader.GetEntityDetails(srcModule, srcRef.Entity);
                    if (srcEntity != null)
                    {
                        var assocExists = srcEntity.Associations.Any(a =>
                            string.Equals(a.Name, assocName, StringComparison.OrdinalIgnoreCase)) ||
                            tgtEntity.Associations.Any(a =>
                            string.Equals(a.Name, assocName, StringComparison.OrdinalIgnoreCase));

                        if (!assocExists)
                            errors.Add($"Association '{assocName}' not found between {srcRef.Module}.{srcRef.Entity} and {targetModule}.{targetEntity}");
                    }
                }
            }
        }

        // 5. Check for reserved words used as column aliases
        foreach (Match match in ColumnAliasPattern.Matches(oqlQuery))
        {
            var aliasName = match.Groups[1].Value;

            // Skip entity aliases (they appear after FROM/JOIN ... AS)
            if (aliasToEntity.ContainsKey(aliasName))
                continue;

            if (ReservedWords.Contains(aliasName))
                warnings.Add($"Column alias '{aliasName}' is a reserved word — use a descriptive name instead (e.g., '{aliasName}Value' or '{aliasName}Name')");
        }

        // 6. Build result
        var sb = new System.Text.StringBuilder();

        if (errors.Count == 0 && warnings.Count == 0)
        {
            sb.AppendLine("OQL validation passed. All entity, attribute, and association references are valid.");
            return ToolResult.Ok(sb.ToString(), "OQL valid — all references check out");
        }

        if (errors.Count > 0)
        {
            sb.AppendLine($"ERRORS ({errors.Count}):");
            foreach (var error in errors)
                sb.AppendLine($"  - {error}");
        }

        if (warnings.Count > 0)
        {
            sb.AppendLine($"WARNINGS ({warnings.Count}):");
            foreach (var warning in warnings)
                sb.AppendLine($"  - {warning}");
        }

        if (errors.Count > 0)
            return ToolResult.Fail(sb.ToString());

        // Warnings only — still valid
        sb.Insert(0, "OQL validation passed with warnings.\n");
        return ToolResult.Ok(sb.ToString(), $"OQL valid with {warnings.Count} warning(s)");
    }

    private string? SuggestEntityName(Mendix.StudioPro.ExtensionsAPI.Model.Projects.IModule module, string wrongName)
    {
        var entities = module.DomainModel.GetEntities();
        return FindClosestMatch(entities.Select(e => e.Name), wrongName);
    }

    private static string? SuggestAttributeName(List<string> attributeNames, string wrongName)
    {
        return FindClosestMatch(attributeNames, wrongName);
    }

    private static string? FindClosestMatch(IEnumerable<string> candidates, string target)
    {
        string? best = null;
        var bestDistance = int.MaxValue;

        foreach (var candidate in candidates)
        {
            var distance = LevenshteinDistance(candidate.ToLowerInvariant(), target.ToLowerInvariant());
            if (distance < bestDistance && distance <= 3)
            {
                bestDistance = distance;
                best = candidate;
            }
        }

        return best;
    }

    private static int LevenshteinDistance(string s, string t)
    {
        var n = s.Length;
        var m = t.Length;
        var d = new int[n + 1, m + 1];

        for (var i = 0; i <= n; i++) d[i, 0] = i;
        for (var j = 0; j <= m; j++) d[0, j] = j;

        for (var i = 1; i <= n; i++)
        {
            for (var j = 1; j <= m; j++)
            {
                var cost = s[i - 1] == t[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }
        }

        return d[n, m];
    }
}
