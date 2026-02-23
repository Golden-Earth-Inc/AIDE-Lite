// ============================================================================
// AIDE Lite - AI-powered IDE extension for Mendix Studio Pro
// Copyright (c) 2025-2026 Neel Desai / Golden Earth Software Consulting Inc.
// Tool: add_activities_to_microflow — adds activities to an existing microflow
// ============================================================================
using System.Text.Json;
using System.Text.Json.Nodes;
using AideLite.ModelReaders;
using AideLite.Models;
using AideLite.Models.MicroflowInstructions;
using AideLite.ModelWriters;

namespace AideLite.Tools;

public class AddActivitiesToMicroflowTool : IClaudeTool
{
    private readonly AppContextExtractor _extractor;
    private readonly MicroflowGenerator _generator;

    public AddActivitiesToMicroflowTool(AppContextExtractor extractor, MicroflowGenerator generator)
    {
        _extractor = extractor;
        _generator = generator;
    }

    public string Name => "add_activities_to_microflow";
    public bool IsWriteTool => true;
    public string Description => "Add new sequential activities to an existing microflow. By default, activities are inserted after the start event, BEFORE any existing activities (so they run first). Use insertBeforeIndex to insert before a specific existing activity instead. IMPORTANT: Cannot remove or reorder existing activities. CRITICAL ORDERING: Activities are inserted in REVERSE order — the LAST item in the array becomes the FIRST activity after Start. So list activities in reverse execution order: what should run last goes first in the array.";
    public JsonObject InputSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["moduleName"] = new JsonObject { ["type"] = "string", ["description"] = "Name of the module containing the microflow" },
            ["microflowName"] = new JsonObject { ["type"] = "string", ["description"] = "Name of the existing microflow to add activities to" },
            ["insertBeforeIndex"] = new JsonObject { ["type"] = "integer", ["description"] = "Optional: insert new activities before the existing activity at this index (0-based). Use get_microflow_details to see activity indices. If omitted, inserts after start (before all existing activities)." },
            ["activities"] = new JsonObject
            {
                ["type"] = "array",
                ["description"] = "Activities in REVERSE execution order (inserted after Start, before existing). The last item here runs first. Example: to run Retrieve then Commit, pass [Commit, Retrieve].",
                ["items"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["type"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["enum"] = new JsonArray("CreateObject", "ChangeObject", "Retrieve", "Commit", "DeleteObject", "CreateList", "AggregateList", "MicroflowCall", "Rollback", "AssociationRetrieve", "Sort", "ChangeList", "ListOperation", "FindByExpression", "ChangeAssociation", "FilterByAssociation", "FilterByAttribute", "FindByAssociation", "FindByAttribute", "AggregateByExpression", "AggregateByAttribute")
                        },
                        ["entity"] = new JsonObject { ["type"] = "string" },
                        ["outputVariableName"] = new JsonObject { ["type"] = "string" },
                        ["variableName"] = new JsonObject { ["type"] = "string" },
                        ["xpathConstraint"] = new JsonObject { ["type"] = "string", ["description"] = "XPath constraint with brackets, e.g. '[Status = \\'Open\\']' or '[Module.Assoc/Module.Entity/Attr = \\'val\\']'. Booleans: true()/false(). Empty: empty." },
                        ["retrieveFirstOnly"] = new JsonObject { ["type"] = "boolean" },
                        ["commit"] = new JsonObject { ["type"] = "boolean" },
                        ["withEvents"] = new JsonObject { ["type"] = "boolean" },
                        ["listVariableName"] = new JsonObject { ["type"] = "string" },
                        ["function"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("Count", "Sum", "Average", "Minimum", "Maximum") },
                        ["calledMicroflowQualifiedName"] = new JsonObject { ["type"] = "string", ["description"] = "For MicroflowCall: qualified name (e.g., MyModule.SUB_Calculate)" },
                        ["memberChanges"] = new JsonObject
                        {
                            ["type"] = "array",
                            ["description"] = "For ChangeObject: attribute changes to apply",
                            ["items"] = new JsonObject
                            {
                                ["type"] = "object",
                                ["properties"] = new JsonObject
                                {
                                    ["attributeName"] = new JsonObject { ["type"] = "string" },
                                    ["valueExpression"] = new JsonObject { ["type"] = "string", ["description"] = "Mendix expression (e.g., '$VariableName/Attribute + 1')" }
                                }
                            }
                        },
                        ["parameterMappings"] = new JsonObject
                        {
                            ["type"] = "array",
                            ["description"] = "For MicroflowCall: parameter mappings",
                            ["items"] = new JsonObject
                            {
                                ["type"] = "object",
                                ["properties"] = new JsonObject
                                {
                                    ["paramName"] = new JsonObject { ["type"] = "string" },
                                    ["valueExpression"] = new JsonObject { ["type"] = "string" }
                                }
                            }
                        },
                        ["associationName"] = new JsonObject { ["type"] = "string", ["description"] = "For AssociationRetrieve: association name" },
                        ["startingVariableName"] = new JsonObject { ["type"] = "string", ["description"] = "For AssociationRetrieve: starting variable" },
                        ["attributeName"] = new JsonObject { ["type"] = "string", ["description"] = "For Sort: attribute to sort by" },
                        ["sortAscending"] = new JsonObject { ["type"] = "boolean", ["description"] = "For Sort: ascending (default true)" },
                        ["secondListVariableName"] = new JsonObject { ["type"] = "string", ["description"] = "For ListOperation: second list variable" },
                        ["filterExpression"] = new JsonObject { ["type"] = "string", ["description"] = "For FindByExpression/ChangeList: Mendix expression" }
                    }
                }
            }
        },
        ["required"] = new JsonArray("moduleName", "microflowName", "activities")
    };

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ToolResult Execute(JsonObject input)
    {
        var moduleName = input["moduleName"]?.GetValue<string>();
        var microflowName = input["microflowName"]?.GetValue<string>();

        if (string.IsNullOrEmpty(moduleName) || string.IsNullOrEmpty(microflowName))
            return ToolResult.Fail("moduleName and microflowName are required");

        var activitiesNode = input["activities"];
        if (activitiesNode == null)
            return ToolResult.Fail("activities array is required");

        var module = _extractor.FindModule(moduleName);
        if (module == null)
            return ToolResult.Fail($"Module '{moduleName}' not found");

        try
        {
            var activities = JsonSerializer.Deserialize<List<ActivityInstruction>>(
                activitiesNode.ToJsonString(), JsonOpts);

            if (activities == null || activities.Count == 0)
                return ToolResult.Fail("At least one activity is required");

            // Optional: insert before a specific activity index instead of after Start
            int? insertBeforeIndex = null;
            if (input["insertBeforeIndex"] != null)
                insertBeforeIndex = input["insertBeforeIndex"]!.GetValue<int>();

            string qualifiedName;
            if (insertBeforeIndex.HasValue)
            {
                qualifiedName = _generator.AddActivitiesToMicroflowBefore(module, microflowName, insertBeforeIndex.Value, activities);
            }
            else
            {
                // Default: insert after Start, before all existing activities
                qualifiedName = _generator.AddActivitiesToMicroflow(module, microflowName, activities);
            }

            var positionDesc = insertBeforeIndex.HasValue
                ? $"before activity[{insertBeforeIndex.Value}]"
                : "after start";
            return ToolResult.Ok(
                $"Added {activities.Count} activities to microflow '{qualifiedName}' ({positionDesc}). Tell the user to press F4 to refresh the App Explorer.",
                $"Added {activities.Count} activities to {qualifiedName}");
        }
        catch (Exception ex)
        {
            return ToolResult.Fail($"Failed to add activities: {ex.Message}");
        }
    }
}
