// ============================================================================
// AIDE Lite - AI-powered IDE extension for Mendix Studio Pro
// Copyright (c) 2025-2026 Neel Desai / Golden Earth Software Consulting Inc.
// Shared document type detection from IDocument runtime type
// ============================================================================
using Mendix.StudioPro.ExtensionsAPI.Model.Constants;
using Mendix.StudioPro.ExtensionsAPI.Model.Enumerations;
using Mendix.StudioPro.ExtensionsAPI.Model.JavaActions;
using Mendix.StudioPro.ExtensionsAPI.Model.Microflows;
using Mendix.StudioPro.ExtensionsAPI.Model.Pages;
using Mendix.StudioPro.ExtensionsAPI.Model.Projects;

namespace AideLite.Services;

public static class DocumentTypeDetector
{
    public static string Detect(IDocument document) => document switch
    {
        IMicroflow => "microflow",
        IPage => "page",
        IConstant => "constant",
        IEnumeration => "enumeration",
        IJavaAction => "java_action",
        _ => DetectByRuntimeType(document)
    };

    private static string DetectByRuntimeType(IDocument document)
    {
        var typeName = document.GetType().Name;
        if (typeName.Contains("Nanoflow")) return "nanoflow";
        if (typeName.Contains("ScheduledEvent")) return "scheduled_event";
        if (typeName.Contains("PublishedRestService")) return "rest_service";
        if (typeName.Contains("PublishedODataService") ||
            typeName.Contains("PublishedOdataService")) return "odata_service";
        if (typeName.Contains("ImportMapping")) return "import_mapping";
        if (typeName.Contains("ExportMapping")) return "export_mapping";
        if (typeName.Contains("Snippet")) return "snippet";
        if (typeName.Contains("Layout") && !typeName.Contains("NavigationLayout")) return "layout";
        if (typeName.Contains("Rule") && !typeName.Contains("Validation")) return "rule";
        if (typeName.Contains("BuildingBlock")) return "building_block";
        if (typeName.Contains("DocumentTemplate")) return "document_template";
        return "document";
    }
}
