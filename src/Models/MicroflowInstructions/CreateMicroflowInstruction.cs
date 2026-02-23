// ============================================================================
// AIDE Lite - AI-powered IDE extension for Mendix Studio Pro
// Copyright (c) 2025-2026 Neel Desai / Golden Earth Software Consulting Inc.
// Create microflow instruction — full specification for a new microflow
// ============================================================================

using System.Text.Json.Serialization;

namespace AideLite.Models.MicroflowInstructions;

/// <summary>
/// Top-level instruction deserialized from Claude's create_microflow tool call.
/// Activities array must be in REVERSE execution order (Mendix API inserts from the end).
/// </summary>
public class CreateMicroflowInstruction
{
    [JsonPropertyName("moduleName")]
    public string ModuleName { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("returnType")]
    public string? ReturnType { get; set; }

    [JsonPropertyName("returnEntityQualifiedName")]
    public string? ReturnEntityQualifiedName { get; set; }

    [JsonPropertyName("parameters")]
    public List<MicroflowParameterInstruction>? Parameters { get; set; }

    [JsonPropertyName("activities")]
    public List<ActivityInstruction>? Activities { get; set; }
}

public class MicroflowParameterInstruction
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("entityQualifiedName")]
    public string? EntityQualifiedName { get; set; }
}
