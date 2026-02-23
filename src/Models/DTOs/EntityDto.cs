// ============================================================================
// AIDE Lite - AI-powered IDE extension for Mendix Studio Pro
// Copyright (c) 2025-2026 Neel Desai / Golden Earth Software Consulting Inc.
// Entity DTOs — attributes, associations, and generalization details
// ============================================================================

namespace AideLite.Models.DTOs;

public class EntityDto
{
    public string Name { get; set; } = string.Empty;
    public string QualifiedName { get; set; } = string.Empty;
    public string? Generalization { get; set; }
    public List<AttributeDto> Attributes { get; set; } = new();
    public List<AssociationDto> Associations { get; set; } = new();

    public string ToSummary()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Entity: {QualifiedName}");
        if (!string.IsNullOrEmpty(Generalization))
            sb.AppendLine($"  Generalizes: {Generalization}");

        if (Attributes.Count > 0)
        {
            sb.AppendLine("  Attributes:");
            foreach (var attr in Attributes)
                sb.AppendLine($"    - {attr.Name}: {attr.TypeName}");
        }

        if (Associations.Count > 0)
        {
            sb.AppendLine("  Associations:");
            foreach (var assoc in Associations)
                sb.AppendLine($"    - {assoc.Name}: {assoc.Parent} → {assoc.Child} ({assoc.Type})");
        }

        return sb.ToString();
    }
}

public class AttributeDto
{
    public string Name { get; set; } = string.Empty;
    public string TypeName { get; set; } = string.Empty;
}

public class AssociationDto
{
    public string Name { get; set; } = string.Empty;
    public string Parent { get; set; } = string.Empty;
    public string Child { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Owner { get; set; } = string.Empty;
}
