// ============================================================================
// AIDE Lite - AI-powered IDE extension for Mendix Studio Pro
// Copyright (c) 2025-2026 Neel Desai / Golden Earth Software Consulting Inc.
// Activity instruction model — defines a single microflow activity to create
// ============================================================================

using System.Text.Json.Serialization;

namespace AideLite.Models.MicroflowInstructions;

/// <summary>
/// Deserialized from Claude's tool_use JSON. Each field maps to a property
/// that one or more of the 21 supported activity types may consume.
/// Unused fields for a given type are simply null.
/// </summary>
public class ActivityInstruction
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("entity")]
    public string? Entity { get; set; }

    [JsonPropertyName("outputVariableName")]
    public string? OutputVariableName { get; set; }

    [JsonPropertyName("variableName")]
    public string? VariableName { get; set; }

    [JsonPropertyName("memberChanges")]
    public List<MemberChangeInstruction>? MemberChanges { get; set; }

    [JsonPropertyName("commit")]
    public bool? Commit { get; set; }

    [JsonPropertyName("withEvents")]
    public bool? WithEvents { get; set; }

    [JsonPropertyName("xpathConstraint")]
    public string? XPathConstraint { get; set; }

    [JsonPropertyName("sortBy")]
    public string? SortBy { get; set; }

    [JsonPropertyName("retrieveFirstOnly")]
    public bool? RetrieveFirstOnly { get; set; }

    [JsonPropertyName("listVariableName")]
    public string? ListVariableName { get; set; }

    [JsonPropertyName("function")]
    public string? Function { get; set; }

    [JsonPropertyName("attributeName")]
    public string? AttributeName { get; set; }

    [JsonPropertyName("calledMicroflowQualifiedName")]
    public string? CalledMicroflowQualifiedName { get; set; }

    [JsonPropertyName("parameterMappings")]
    public List<ParameterMappingInstruction>? ParameterMappings { get; set; }

    [JsonPropertyName("associationName")]
    public string? AssociationName { get; set; }

    [JsonPropertyName("startingVariableName")]
    public string? StartingVariableName { get; set; }

    [JsonPropertyName("sortAscending")]
    public bool? SortAscending { get; set; }

    [JsonPropertyName("secondListVariableName")]
    public string? SecondListVariableName { get; set; }

    [JsonPropertyName("filterExpression")]
    public string? FilterExpression { get; set; }
}

public class MemberChangeInstruction
{
    [JsonPropertyName("attributeName")]
    public string AttributeName { get; set; } = string.Empty;

    [JsonPropertyName("valueExpression")]
    public string ValueExpression { get; set; } = string.Empty;
}

public class ParameterMappingInstruction
{
    [JsonPropertyName("paramName")]
    public string ParamName { get; set; } = string.Empty;

    [JsonPropertyName("valueExpression")]
    public string ValueExpression { get; set; } = string.Empty;
}
