// ============================================================================
// AIDE Lite - AI-powered IDE extension for Mendix Studio Pro
// Copyright (c) 2025-2026 Neel Desai / Golden Earth Software Consulting Inc.
// Page reader — lists pages across modules with recursive folder traversal
// ============================================================================
using AideLite.Models.DTOs;
using Mendix.StudioPro.ExtensionsAPI.Model;
using Mendix.StudioPro.ExtensionsAPI.Model.Pages;
using Mendix.StudioPro.ExtensionsAPI.Model.Projects;

namespace AideLite.ModelReaders;

public class PageReader
{
    private readonly IModel _model;

    public PageReader(IModel model)
    {
        _model = model;
    }

    public List<PageSummaryDto> GetPageSummaries(IModule module)
    {
        var summaries = new List<PageSummaryDto>();

        // Delegates to MicroflowReader's recursive folder traversal to catch deeply nested pages
        foreach (var page in MicroflowReader.GetAllDocumentsRecursive<IPage>(module))
        {
            if (page.Excluded) continue;
            summaries.Add(new PageSummaryDto { Name = page.Name });
        }
        return summaries;
    }
}
