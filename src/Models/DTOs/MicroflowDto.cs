// ============================================================================
// AIDE Lite - AI-powered IDE extension for Mendix Studio Pro
// Copyright (c) 2025-2026 Neel Desai / Golden Earth Software Consulting Inc.
// Microflow DTOs — activity sequences, parameters, return types, and annotations
// ============================================================================

namespace AideLite.Models.DTOs;

public class MicroflowDto
{
    public string Name { get; set; } = string.Empty;
    public string QualifiedName { get; set; } = string.Empty;
    public string? ReturnType { get; set; }
    public List<MicroflowParameterDto> Parameters { get; set; } = new();
    public List<MicroflowActivityDto> Activities { get; set; } = new();
    public List<FlowControlElementDto> FlowControlElements { get; set; } = new();
    public List<SequenceFlowDto> SequenceFlows { get; set; } = new();
    public List<string> Annotations { get; set; } = new();

    public string ToSummary()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Microflow: {QualifiedName}");
        if (!string.IsNullOrEmpty(ReturnType))
            sb.AppendLine($"  Returns: {ReturnType}");

        if (Parameters.Count > 0)
        {
            sb.AppendLine("  Parameters:");
            foreach (var p in Parameters)
                sb.AppendLine($"    - {p.Name}: {p.TypeName}");
        }

        if (Activities.Count > 0)
        {
            sb.AppendLine("  Activities:");
            foreach (var a in Activities)
                sb.AppendLine($"    - {a.ToDetailLine()}");
        }

        if (FlowControlElements.Count > 0)
        {
            sb.AppendLine("  Flow Control:");
            foreach (var fc in FlowControlElements)
                sb.AppendLine($"    - {fc.ToDetailLine()}");
        }

        if (SequenceFlows.Count > 0)
        {
            sb.AppendLine("  Sequence Flows:");
            foreach (var sf in SequenceFlows)
                sb.AppendLine($"    - {sf.ToDetailLine()}");
        }

        if (Annotations.Count > 0)
        {
            sb.AppendLine("  Annotations:");
            foreach (var annotation in Annotations)
                sb.AppendLine($"    - {annotation}");
        }

        return sb.ToString();
    }
}

public class MicroflowParameterDto
{
    public string Name { get; set; } = string.Empty;
    public string TypeName { get; set; } = string.Empty;
}

/// <summary>
/// Detailed activity representation. Index matches the 0-based position used by
/// edit_microflow_activity — Claude references these indices when editing.
/// </summary>
public class MicroflowActivityDto
{
    public int Index { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? OutputVariableName { get; set; }
    public string? EntityName { get; set; }
    public string? XPathConstraint { get; set; }
    public bool? RetrieveFirstOnly { get; set; }
    public string? ChangeVariableName { get; set; }
    public string? CommitSetting { get; set; }
    public string? CalledMicroflow { get; set; }
    public string? AggregateFunction { get; set; }
    public string? ListVariableName { get; set; }
    public bool? Disabled { get; set; }
    public string? Caption { get; set; }
    public bool? WithEvents { get; set; }
    public string? AssociationName { get; set; }
    public string? ListOperationType { get; set; }
    public List<MemberChangeDto>? MemberChanges { get; set; }

    public string ToDetailLine()
    {
        var parts = new List<string> { $"[{Index}] {Type}" };

        if (!string.IsNullOrEmpty(EntityName))
            parts.Add(EntityName);
        if (!string.IsNullOrEmpty(XPathConstraint))
            parts.Add($"xpath='{XPathConstraint}'");
        if (RetrieveFirstOnly == true)
            parts.Add("firstOnly=true");
        if (!string.IsNullOrEmpty(ChangeVariableName))
            parts.Add($"variable=${ChangeVariableName}");
        if (!string.IsNullOrEmpty(CalledMicroflow))
            parts.Add($"calls={CalledMicroflow}");
        if (!string.IsNullOrEmpty(AggregateFunction))
            parts.Add($"function={AggregateFunction}");
        if (!string.IsNullOrEmpty(ListVariableName))
            parts.Add($"list=${ListVariableName}");
        if (!string.IsNullOrEmpty(OutputVariableName))
            parts.Add($"output=${OutputVariableName}");
        if (!string.IsNullOrEmpty(CommitSetting))
            parts.Add($"commit={CommitSetting}");
        if (WithEvents != null)
            parts.Add($"withEvents={WithEvents}");
        if (!string.IsNullOrEmpty(AssociationName))
            parts.Add($"association={AssociationName}");
        if (!string.IsNullOrEmpty(ListOperationType))
            parts.Add($"operation={ListOperationType}");
        if (Disabled == true)
            parts.Add("DISABLED");
        if (!string.IsNullOrEmpty(Caption))
            parts.Add($"caption='{Caption}'");
        if (MemberChanges != null && MemberChanges.Count > 0)
            parts.Add($"changes=[{string.Join(", ", MemberChanges.Select(m => $"{m.AttributeName}={m.ValueExpression}"))}]");

        return string.Join(", ", parts);
    }
}

public class MemberChangeDto
{
    public string AttributeName { get; set; } = string.Empty;
    public string ValueExpression { get; set; } = string.Empty;
}

/// <summary>
/// Read-only representation of decisions, loops, merges, and other flow control.
/// These elements can be READ via the Untyped Model API but cannot be CREATED
/// through the Extensions API (confirmed absent v10.23.0-v11.6.2).
/// </summary>
public class FlowControlElementDto
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? Caption { get; set; }
    public string? Condition { get; set; }
    public string? Documentation { get; set; }
    public string? IteratedListVariable { get; set; }
    public string? LoopVariableName { get; set; }
    public string? SplitVariableName { get; set; }

    public string ToDetailLine()
    {
        var parts = new List<string> { $"[{Id}] {Type}" };
        if (!string.IsNullOrEmpty(Caption))
            parts.Add($"caption='{Caption}'");
        if (!string.IsNullOrEmpty(Condition))
            parts.Add($"condition='{Condition}'");
        if (!string.IsNullOrEmpty(IteratedListVariable))
            parts.Add($"iterates=${IteratedListVariable}");
        if (!string.IsNullOrEmpty(LoopVariableName))
            parts.Add($"as=${LoopVariableName}");
        if (!string.IsNullOrEmpty(SplitVariableName))
            parts.Add($"splitOn=${SplitVariableName}");
        if (!string.IsNullOrEmpty(Documentation))
            parts.Add($"doc='{Documentation}'");
        return string.Join(", ", parts);
    }
}

public class SequenceFlowDto
{
    public string OriginId { get; set; } = string.Empty;
    public string DestinationId { get; set; } = string.Empty;
    public string? CaseValue { get; set; }
    public bool IsErrorHandler { get; set; }

    public string ToDetailLine()
    {
        var parts = new List<string> { $"{OriginId} -> {DestinationId}" };
        if (!string.IsNullOrEmpty(CaseValue))
            parts.Add($"case='{CaseValue}'");
        if (IsErrorHandler)
            parts.Add("errorHandler");
        return string.Join(", ", parts);
    }
}
