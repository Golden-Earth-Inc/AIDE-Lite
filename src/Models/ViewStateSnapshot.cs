// ============================================================================
// AIDE Lite - AI-powered IDE extension for Mendix Studio Pro
// Copyright (c) 2025-2026 Neel Desai / Golden Earth Software Consulting Inc.
// State transfer DTO for toggling between pane and tab views
// ============================================================================
using AideLite.Services;

namespace AideLite.Models;

/// <summary>
/// Snapshot of display state captured before a view toggle so it can be
/// restored in the new view (pane → tab or tab → pane).
/// </summary>
public class ViewStateSnapshot
{
    public List<DisplayHistoryEntry> DisplayHistory { get; init; } = new();
    public string? ActiveDocumentName { get; init; }
    public string? ActiveDocumentType { get; init; }
    public string? ActiveDocumentQualifiedName { get; init; }
    public string? ConversationId { get; init; }
    public string? ConversationTitle { get; init; }
}
