// ============================================================================
// AIDE Lite - AI-powered IDE extension for Mendix Studio Pro
// Copyright (c) 2025-2026 Neel Desai / Golden Earth Software Consulting Inc.
// Tool: edit_microflow_activity — edits activity properties in-place
// ============================================================================
using System.Text.Json;
using System.Text.Json.Nodes;
using AideLite.ModelReaders;
using AideLite.Models;
using AideLite.Models.MicroflowInstructions;
using AideLite.ModelWriters;

namespace AideLite.Tools;

public class EditMicroflowActivityTool : IClaudeTool
{
    private readonly AppContextExtractor _extractor;
    private readonly MicroflowGenerator _generator;

    public EditMicroflowActivityTool(AppContextExtractor extractor, MicroflowGenerator generator)
    {
        _extractor = extractor;
        _generator = generator;
    }

    public string Name => "edit_microflow_activity";
    public bool IsWriteTool => true;
    public string Description => "Edit properties of an existing activity in a microflow in-place. Use get_microflow_details first to see the activity index and current values. Only provided properties are changed; omitted properties are left untouched. This preserves decisions, loops, and splits unlike replace_microflow.";
    public JsonObject InputSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["moduleName"] = new JsonObject { ["type"] = "string", ["description"] = "Name of the module containing the microflow" },
            ["microflowName"] = new JsonObject { ["type"] = "string", ["description"] = "Name of the microflow" },
            ["activityIndex"] = new JsonObject { ["type"] = "integer", ["description"] = "Index of the activity to edit (from get_microflow_details output)" },
            ["xpathConstraint"] = new JsonObject { ["type"] = "string", ["description"] = "For Retrieve: XPath constraint with brackets, e.g. '[Status = \\'Open\\']', '[IsActive = true()]', '[Module.Assoc/Module.Entity/Attr = \\'val\\']'. See system prompt for full syntax." },
            ["outputVariableName"] = new JsonObject { ["type"] = "string", ["description"] = "New output variable name" },
            ["entityName"] = new JsonObject { ["type"] = "string", ["description"] = "New entity (qualified name, e.g., 'MyModule.Customer')" },
            ["retrieveFirstOnly"] = new JsonObject { ["type"] = "boolean", ["description"] = "For Retrieve: whether to retrieve first only" },
            ["changeVariableName"] = new JsonObject { ["type"] = "string", ["description"] = "For ChangeObject/Commit/Delete: variable name to operate on" },
            ["commit"] = new JsonObject { ["type"] = "string", ["description"] = "Commit setting: 'Yes', 'No', or 'YesWithoutEvents'", ["enum"] = new JsonArray("Yes", "No", "YesWithoutEvents") },
            ["withEvents"] = new JsonObject { ["type"] = "boolean", ["description"] = "For Commit: whether to trigger event handlers" },
            ["disabled"] = new JsonObject { ["type"] = "boolean", ["description"] = "Set to true to disable the activity (skip during execution)" },
            ["caption"] = new JsonObject { ["type"] = "string", ["description"] = "Display caption for the activity" },
            ["calledMicroflowQualifiedName"] = new JsonObject { ["type"] = "string", ["description"] = "For MicroflowCall: qualified name of microflow to call (e.g., 'MyModule.SUB_Calculate')" },
            ["aggregateFunction"] = new JsonObject { ["type"] = "string", ["description"] = "For AggregateList: function to use", ["enum"] = new JsonArray("Count", "Sum", "Average", "Minimum", "Maximum") },
            ["listVariableName"] = new JsonObject { ["type"] = "string", ["description"] = "For AggregateList: input list variable name" },
            ["memberChanges"] = new JsonObject
            {
                ["type"] = "array",
                ["description"] = "For ChangeObject/CreateObject: attribute value changes",
                ["items"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["attributeName"] = new JsonObject { ["type"] = "string" },
                        ["valueExpression"] = new JsonObject { ["type"] = "string", ["description"] = "Mendix expression (e.g., '$Variable/Attribute + 1')" }
                    }
                }
            }
        },
        ["required"] = new JsonArray("moduleName", "microflowName", "activityIndex")
    };

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ToolResult Execute(JsonObject input)
    {
        var moduleName = input["moduleName"]?.GetValue<string>();
        var microflowName = input["microflowName"]?.GetValue<string>();
        var activityIndex = input["activityIndex"]?.GetValue<int>();

        if (string.IsNullOrEmpty(moduleName) || string.IsNullOrEmpty(microflowName) || activityIndex == null)
            return ToolResult.Fail("moduleName, microflowName, and activityIndex are required");

        var module = _extractor.FindModule(moduleName);
        if (module == null)
            return ToolResult.Fail($"Module '{moduleName}' not found");

        try
        {
            // Only properties present in the JSON are applied; omitted ones stay untouched
            var edits = JsonSerializer.Deserialize<EditActivityInstruction>(
                input.ToJsonString(), JsonOpts) ?? new EditActivityInstruction();

            var result = _generator.EditMicroflowActivity(module, microflowName, activityIndex.Value, edits);
            return ToolResult.Ok(
                $"Successfully edited activity in '{moduleName}.{microflowName}': {result}. Tell the user to press F4 to refresh.",
                $"Edited activity {activityIndex} in {moduleName}.{microflowName}");
        }
        catch (Exception ex)
        {
            return ToolResult.Fail($"Failed to edit activity: {ex.Message}");
        }
    }
}
