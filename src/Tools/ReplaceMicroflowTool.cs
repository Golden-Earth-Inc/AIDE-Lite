// ============================================================================
// AIDE Lite - AI-powered IDE extension for Mendix Studio Pro
// Copyright (c) 2025-2026 Neel Desai / Golden Earth Software Consulting Inc.
// Tool: replace_microflow — replaces a microflow entirely with backup
// ============================================================================
using System.Text.Json;
using System.Text.Json.Nodes;
using AideLite.ModelReaders;
using AideLite.Models;
using AideLite.Models.MicroflowInstructions;
using AideLite.ModelWriters;

namespace AideLite.Tools;

public class ReplaceMicroflowTool : IClaudeTool
{
    private readonly AppContextExtractor _extractor;
    private readonly MicroflowGenerator _generator;

    public ReplaceMicroflowTool(AppContextExtractor extractor, MicroflowGenerator generator)
    {
        _extractor = extractor;
        _generator = generator;
    }

    public string Name => "replace_microflow";
    public bool IsWriteTool => true;
    public string Description => "Replace an existing microflow with a new version. The original is kept as a backup (renamed with _REPLACED_ prefix and marked as excluded). The new microflow gets the original name so all callers auto-resolve. WARNING: Any decisions, loops, or splits in the original will be lost. CRITICAL ORDERING: Activities are inserted in REVERSE order — the LAST item in the array becomes the FIRST activity after Start. So list activities in reverse execution order: what should run last goes first in the array.";
    public JsonObject InputSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["moduleName"] = new JsonObject { ["type"] = "string", ["description"] = "Name of the module containing the microflow" },
            ["currentName"] = new JsonObject { ["type"] = "string", ["description"] = "Name of the existing microflow to replace" },
            ["returnType"] = new JsonObject { ["type"] = "string", ["description"] = "Return type for the new version: Boolean, Integer, String, DateTime, Decimal, Void, Object, List" },
            ["returnEntityQualifiedName"] = new JsonObject { ["type"] = "string", ["description"] = "Required when returnType is Object or List. The qualified entity name (e.g., 'MyModule.Customer')" },
            ["parameters"] = new JsonObject
            {
                ["type"] = "array",
                ["description"] = "Parameters for the new microflow version",
                ["items"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["name"] = new JsonObject { ["type"] = "string" },
                        ["type"] = new JsonObject { ["type"] = "string" },
                        ["entityQualifiedName"] = new JsonObject { ["type"] = "string", ["description"] = "Required for Object/List types (e.g., MyModule.Customer)" }
                    }
                }
            },
            ["activities"] = new JsonObject
            {
                ["type"] = "array",
                ["description"] = "Activities in REVERSE execution order for the new version. The last item here runs first after Start. Example: to run Retrieve then Commit, pass [Commit, Retrieve].",
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
                        }
                    }
                }
            }
        },
        ["required"] = new JsonArray("moduleName", "currentName")
    };

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ToolResult Execute(JsonObject input)
    {
        var moduleName = input["moduleName"]?.GetValue<string>();
        var currentName = input["currentName"]?.GetValue<string>();

        if (string.IsNullOrEmpty(moduleName) || string.IsNullOrEmpty(currentName))
            return ToolResult.Fail("moduleName and currentName are required");

        var module = _extractor.FindModule(moduleName);
        if (module == null)
            return ToolResult.Fail($"Module '{moduleName}' not found");

        try
        {
            // Reuse CreateMicroflowInstruction — the generator renames the original
            // with a _REPLACED_ prefix and creates a fresh one with the original name
            var instruction = JsonSerializer.Deserialize<CreateMicroflowInstruction>(
                input.ToJsonString(), JsonOpts) ?? new CreateMicroflowInstruction();
            instruction.ModuleName = moduleName;
            instruction.Name = currentName;

            var qualifiedName = _generator.ReplaceMicroflow(module, currentName, instruction);
            return ToolResult.Ok(
                $"Microflow '{qualifiedName}' replaced successfully. The original has been backed up with a _REPLACED_ prefix and excluded. Tell the user to press F4 to refresh the App Explorer.",
                $"Replaced microflow {qualifiedName}");
        }
        catch (Exception ex)
        {
            return ToolResult.Fail($"Failed to replace microflow: {ex.Message}");
        }
    }
}
