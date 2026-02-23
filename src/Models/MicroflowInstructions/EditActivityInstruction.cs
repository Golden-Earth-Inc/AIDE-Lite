// ============================================================================
// AIDE Lite - AI-powered IDE extension for Mendix Studio Pro
// Copyright (c) 2025-2026 Neel Desai / Golden Earth Software Consulting Inc.
// Edit activity instruction — property changes for an existing microflow activity
// ============================================================================

using System.Text.Json.Serialization;

namespace AideLite.Models.MicroflowInstructions;

/// <summary>
/// Only non-null fields are applied to the target activity.
/// Editable properties vary by action type — see CLAUDE.md for the full matrix.
/// </summary>
public class EditActivityInstruction
{
    [JsonPropertyName("xpathConstraint")]
    public string? XPathConstraint { get; set; }

    [JsonPropertyName("outputVariableName")]
    public string? OutputVariableName { get; set; }

    [JsonPropertyName("entityName")]
    public string? EntityName { get; set; }

    [JsonPropertyName("retrieveFirstOnly")]
    public bool? RetrieveFirstOnly { get; set; }

    [JsonPropertyName("changeVariableName")]
    public string? ChangeVariableName { get; set; }

    [JsonPropertyName("commit")]
    public string? Commit { get; set; }

    [JsonPropertyName("withEvents")]
    public bool? WithEvents { get; set; }

    [JsonPropertyName("disabled")]
    public bool? Disabled { get; set; }

    [JsonPropertyName("caption")]
    public string? Caption { get; set; }

    [JsonPropertyName("calledMicroflowQualifiedName")]
    public string? CalledMicroflowQualifiedName { get; set; }

    [JsonPropertyName("aggregateFunction")]
    public string? AggregateFunction { get; set; }

    [JsonPropertyName("listVariableName")]
    public string? ListVariableName { get; set; }

    [JsonPropertyName("memberChanges")]
    public List<MemberChangeInstruction>? MemberChanges { get; set; }
}
