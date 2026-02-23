// ============================================================================
// AIDE Lite - AI-powered IDE extension for Mendix Studio Pro
// Copyright (c) 2025-2026 Neel Desai / Golden Earth Software Consulting Inc.
// Tool: create_microflow — creates a new microflow with sequential activities
// ============================================================================
using System.Text.Json;
using System.Text.Json.Nodes;
using AideLite.ModelReaders;
using AideLite.Models;
using AideLite.Models.MicroflowInstructions;
using AideLite.ModelWriters;

namespace AideLite.Tools;

public class CreateMicroflowTool : IClaudeTool
{
    private readonly AppContextExtractor _extractor;
    private readonly MicroflowGenerator _generator;

    public CreateMicroflowTool(AppContextExtractor extractor, MicroflowGenerator generator)
    {
        _extractor = extractor;
        _generator = generator;
    }

    public string Name => "create_microflow";
    public bool IsWriteTool => true;
    public string Description => "Create a new microflow with sequential activities. IMPORTANT: Only sequential activities are supported (no decisions, loops, or splits). CRITICAL ORDERING: Activities are inserted in REVERSE order — the LAST item in the array becomes the FIRST activity after Start. So list activities in reverse execution order: what should run last goes first in the array.";
    public JsonObject InputSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["moduleName"] = new JsonObject { ["type"] = "string", ["description"] = "Name of the module to create the microflow in" },
            ["name"] = new JsonObject { ["type"] = "string", ["description"] = "Name of the microflow (use ACT_, SUB_, DS_, or VAL_ prefix)" },
            ["returnType"] = new JsonObject { ["type"] = "string", ["description"] = "Return type: Boolean, Integer, String, DateTime, Decimal, Void, Object, List" },
            ["returnEntityQualifiedName"] = new JsonObject { ["type"] = "string", ["description"] = "Required when returnType is Object or List. The qualified entity name (e.g., 'MyModule.Customer')" },
            ["parameters"] = new JsonObject
            {
                ["type"] = "array",
                ["description"] = "Microflow parameters",
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
                ["description"] = "Activities in REVERSE execution order. The last item here runs first after Start. Example: to run Retrieve then Commit, pass [Commit, Retrieve].",
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
                        ["xpathConstraint"] = new JsonObject { ["type"] = "string", ["description"] = "XPath constraint with brackets, e.g. '[Status = \\'Open\\']' or '[Module.Assoc/Module.Entity/Attr = \\'val\\']'. Booleans: true()/false(). Empty: empty. See system prompt for full syntax." },
                        ["retrieveFirstOnly"] = new JsonObject { ["type"] = "boolean" },
                        ["commit"] = new JsonObject { ["type"] = "boolean" },
                        ["withEvents"] = new JsonObject { ["type"] = "boolean" },
                        ["listVariableName"] = new JsonObject { ["type"] = "string" },
                        ["function"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("Count", "Sum", "Average", "Minimum", "Maximum") },
                        ["calledMicroflowQualifiedName"] = new JsonObject { ["type"] = "string", ["description"] = "For MicroflowCall: qualified name of the microflow to call (e.g., MyModule.SUB_Calculate)" },
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
                        ["associationName"] = new JsonObject { ["type"] = "string", ["description"] = "For AssociationRetrieve: name of the association to retrieve over" },
                        ["startingVariableName"] = new JsonObject { ["type"] = "string", ["description"] = "For AssociationRetrieve: variable to start the association retrieval from" },
                        ["attributeName"] = new JsonObject { ["type"] = "string", ["description"] = "For Sort: name of the attribute to sort by" },
                        ["sortAscending"] = new JsonObject { ["type"] = "boolean", ["description"] = "For Sort: true for ascending (default), false for descending" },
                        ["sortBy"] = new JsonObject { ["type"] = "string", ["description"] = "For Retrieve: attribute name to sort results by" },
                        ["secondListVariableName"] = new JsonObject { ["type"] = "string", ["description"] = "For ListOperation (Union/Intersect/Subtract): the second list variable" },
                        ["filterExpression"] = new JsonObject { ["type"] = "string", ["description"] = "For FindByExpression/ChangeList: Mendix expression (e.g., '$currentObject/Active = true')" }
                    }
                }
            }
        },
        ["required"] = new JsonArray("moduleName", "name")
    };

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ToolResult Execute(JsonObject input)
    {
        var moduleName = input["moduleName"]?.GetValue<string>();
        var name = input["name"]?.GetValue<string>();

        if (string.IsNullOrEmpty(moduleName) || string.IsNullOrEmpty(name))
            return ToolResult.Fail("moduleName and name are required");

        var module = _extractor.FindModule(moduleName);
        if (module == null)
            return ToolResult.Fail($"Module '{moduleName}' not found");

        try
        {
            // Deserialize the entire JSON input into our instruction DTO (camelCase mapping)
            var instruction = JsonSerializer.Deserialize<CreateMicroflowInstruction>(
                input.ToJsonString(), JsonOpts) ?? new CreateMicroflowInstruction();
            instruction.ModuleName = moduleName;
            instruction.Name = name;

            var qualifiedName = _generator.CreateMicroflow(module, instruction);
            return ToolResult.Ok(
                $"Microflow '{qualifiedName}' created successfully. Tell the user to press F4 to refresh the App Explorer.",
                $"Created microflow {qualifiedName}");
        }
        catch (Exception ex)
        {
            return ToolResult.Fail($"Failed to create microflow: {ex.Message}");
        }
    }
}
