// ============================================================================
// AIDE Lite - AI-powered IDE extension for Mendix Studio Pro
// Copyright (c) 2025-2026 Neel Desai / Golden Earth Software Consulting Inc.
// Tool: get_document_details — generic reader for any document type
// ============================================================================
using System.Text.Json.Nodes;
using AideLite.ModelReaders;
using AideLite.Models;

namespace AideLite.Tools;

public class GetDocumentDetailsTool : IClaudeTool
{
    private readonly AppContextExtractor _extractor;
    private readonly GenericDocumentReader _reader;

    public GetDocumentDetailsTool(AppContextExtractor extractor, GenericDocumentReader reader)
    {
        _extractor = extractor;
        _reader = reader;
    }

    public string Name => "get_document_details";
    public string Description => "Get details of any document type (nanoflow, scheduled event, REST service, snippet, layout, rule, mapping, etc.) using the generic model reader. Use for document types without a dedicated tool.";
    public JsonObject InputSchema => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["moduleName"] = new JsonObject { ["type"] = "string", ["description"] = "Name of the module" },
            ["documentName"] = new JsonObject { ["type"] = "string", ["description"] = "Name of the document" }
        },
        ["required"] = new JsonArray("moduleName", "documentName")
    };

    public ToolResult Execute(JsonObject input)
    {
        var moduleName = input["moduleName"]?.GetValue<string>();
        var documentName = input["documentName"]?.GetValue<string>();
        if (string.IsNullOrEmpty(moduleName) || string.IsNullOrEmpty(documentName))
            return ToolResult.Fail("moduleName and documentName are required");

        var module = _extractor.FindModule(moduleName);
        if (module == null)
            return ToolResult.Fail($"Module '{moduleName}' not found");

        var details = _reader.GetDocumentDetails(module, documentName);
        if (details == null)
            return ToolResult.Fail($"Document '{documentName}' not found in module '{moduleName}'");

        return ToolResult.Ok(details, $"Document {moduleName}.{documentName}");
    }
}
