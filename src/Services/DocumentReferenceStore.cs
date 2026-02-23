// ============================================================================
// AIDE Lite - AI-powered IDE extension for Mendix Studio Pro
// Copyright (c) 2025-2026 Neel Desai / Golden Earth Software Consulting Inc.
// Static bridge between context menu extensions and the chat pane ViewModel
// ============================================================================
using System.Collections.Concurrent;

namespace AideLite.Services;

public enum DocumentAction { Explain, AddContext }

public record DocumentReference(DocumentAction Action, string ElementType, string QualifiedName);

/// <summary>
/// Thread-safe static store that context menu extensions enqueue into and the
/// pane ViewModel dequeues from.  The event fires on the enqueue thread;
/// the ViewModel must marshal to its own context if needed.
/// </summary>
public static class DocumentReferenceStore
{
    private static readonly ConcurrentQueue<DocumentReference> Queue = new();

    public static event Action<DocumentReference>? OnDocumentReferenced;

    public static void Enqueue(DocumentReference reference)
    {
        Queue.Enqueue(reference);
        OnDocumentReferenced?.Invoke(reference);
    }

    public static bool TryDequeue(out DocumentReference? reference)
    {
        return Queue.TryDequeue(out reference);
    }

    public static void DrainAll(Action<DocumentReference> handler)
    {
        while (Queue.TryDequeue(out var reference))
        {
            handler(reference);
        }
    }
}
