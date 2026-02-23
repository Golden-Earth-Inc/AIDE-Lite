// ============================================================================
// AIDE Lite - AI-powered IDE extension for Mendix Studio Pro
// Copyright (c) 2025-2026 Neel Desai / Golden Earth Software Consulting Inc.
// Transaction wrapper — ensures all model changes are atomic with rollback
// ============================================================================

using Mendix.StudioPro.ExtensionsAPI.Model;
using Mendix.StudioPro.ExtensionsAPI.Services;

namespace AideLite.ModelWriters;

public class TransactionManager
{
    private readonly IModel _model;
    private readonly ILogService _logService;

    public TransactionManager(IModel model, ILogService logService)
    {
        _model = model;
        _logService = logService;
    }

    /// <summary>
    /// Wraps a model mutation in a single transaction. Only one transaction
    /// at a time is allowed by Mendix — nested calls will throw.
    /// </summary>
    public T ExecuteInTransaction<T>(string description, Func<T> action)
    {
        using var transaction = _model.StartTransaction(description);
        try
        {
            var result = action();
            transaction.Commit();
            _logService.Info($"AIDE Lite: Transaction committed: {description}");
            return result;
        }
        catch (Exception ex)
        {
            transaction.Rollback();
            _logService.Error($"AIDE Lite: Transaction rolled back ({description}): {ex.Message}");
            throw;
        }
    }

    public void ExecuteInTransaction(string description, Action action)
    {
        using var transaction = _model.StartTransaction(description);
        try
        {
            action();
            transaction.Commit();
            _logService.Info($"AIDE Lite: Transaction committed: {description}");
        }
        catch (Exception ex)
        {
            transaction.Rollback();
            _logService.Error($"AIDE Lite: Transaction rolled back ({description}): {ex.Message}");
            throw;
        }
    }
}
