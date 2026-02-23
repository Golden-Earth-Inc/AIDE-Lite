// ============================================================================
// AIDE Lite - AI-powered IDE extension for Mendix Studio Pro
// Copyright (c) 2025-2026 Neel Desai / Golden Earth Software Consulting Inc.
// Microflow reader — activity sequences, flow control, parameters, and annotations
// ============================================================================
using AideLite.Models.DTOs;
using Mendix.StudioPro.ExtensionsAPI.Model;
using Mendix.StudioPro.ExtensionsAPI.Model.DomainModels;
using Mendix.StudioPro.ExtensionsAPI.Model.Microflows;
using Mendix.StudioPro.ExtensionsAPI.Model.Microflows.Actions;
using Mendix.StudioPro.ExtensionsAPI.Model.Projects;
using Mendix.StudioPro.ExtensionsAPI.Model.UntypedModel;
using Mendix.StudioPro.ExtensionsAPI.Services;

namespace AideLite.ModelReaders;

public class MicroflowReader
{
    private readonly IModel _model;
    private readonly IMicroflowService _microflowService;
    private readonly IUntypedModelAccessService _untypedModelAccessService;
    private readonly ILogService _logService;

    public MicroflowReader(IModel model, IMicroflowService microflowService,
        IUntypedModelAccessService untypedModelAccessService, ILogService logService)
    {
        _model = model;
        _microflowService = microflowService;
        _untypedModelAccessService = untypedModelAccessService;
        _logService = logService;
    }

    public List<MicroflowSummaryDto> GetMicroflowSummaries(IModule module)
    {
        var summaries = new List<MicroflowSummaryDto>();
        var microflows = GetAllDocumentsRecursive<IMicroflow>(module);

        foreach (var mf in microflows)
        {
            if (mf.Excluded) continue;
            var parameters = _microflowService.GetParameters(mf);
            summaries.Add(new MicroflowSummaryDto
            {
                Name = mf.Name,
                ReturnType = mf.ReturnType != null ? DomainModelReader.GetDataTypeName(mf.ReturnType) : null,
                ParameterCount = parameters.Count
            });
        }
        return summaries;
    }

    /// <summary>
    /// Get enriched microflow summaries with parameter details and activity type sequence.
    /// Uses typed API only (no Untyped Model API), keeping it fast.
    /// </summary>
    public List<MicroflowSummaryDto> GetEnrichedMicroflowSummaries(IModule module)
    {
        var summaries = new List<MicroflowSummaryDto>();
        var microflows = GetAllDocumentsRecursive<IMicroflow>(module);

        foreach (var mf in microflows)
        {
            if (mf.Excluded) continue;

            var parameters = _microflowService.GetParameters(mf);
            var paramDtos = parameters.Select(p => new MicroflowParameterDto
            {
                Name = p.Name,
                TypeName = DomainModelReader.GetDataTypeName(p.Type)
            }).ToList();

            string? activitySummary = null;
            try
            {
                var activities = _microflowService.GetAllMicroflowActivities(mf);
                var activityTypes = new List<string>();
                foreach (var activity in activities)
                    activityTypes.Add(GetBriefActivityType(activity));

                if (activityTypes.Count > 0)
                {
                    activitySummary = activityTypes.Count <= 8
                        ? string.Join(" → ", activityTypes)
                        : string.Join(" → ", activityTypes.Take(7)) + $" → ...+{activityTypes.Count - 7} more";
                }
            }
            catch { /* activity reading failed, leave summary null */ }

            summaries.Add(new MicroflowSummaryDto
            {
                Name = mf.Name,
                ReturnType = mf.ReturnType != null ? DomainModelReader.GetDataTypeName(mf.ReturnType) : null,
                ParameterCount = parameters.Count,
                Parameters = paramDtos,
                ActivitySummary = activitySummary
            });
        }
        return summaries;
    }

    private static string GetBriefActivityType(IActivity activity)
    {
        if (activity is IActionActivity actionActivity)
        {
            var action = actionActivity.Action;
            if (action != null)
            {
                return action switch
                {
                    IRetrieveAction ra => ra.RetrieveSource is IAssociationRetrieveSource
                        ? "AssocRetrieve" : "Retrieve",
                    IChangeObjectAction => "ChangeObject",
                    ICreateObjectAction => "CreateObject",
                    ICommitAction => "Commit",
                    IDeleteAction => "Delete",
                    IMicroflowCallAction => "MicroflowCall",
                    IAggregateListAction => "AggregateList",
                    ICreateListAction => "CreateList",
                    IRollbackAction => "Rollback",
                    IChangeListAction => "ChangeList",
                    IListOperationAction => "ListOp",
                    IJavaActionCallAction => "JavaAction",
                    _ => "Action"
                };
            }
        }
        return SimplifyTypeName(activity.GetType().Name);
    }

    public MicroflowDto? GetMicroflowDetails(IModule module, string microflowName)
    {
        var microflows = GetAllDocumentsRecursive<IMicroflow>(module);

        foreach (var mf in microflows)
        {
            if (mf.Excluded || mf.Name != microflowName) continue;

            var dto = new MicroflowDto
            {
                Name = mf.Name,
                QualifiedName = $"{module.Name}.{mf.Name}",
                ReturnType = mf.ReturnType != null
                    ? DomainModelReader.GetDataTypeName(mf.ReturnType)
                    : null
            };

            // Get parameters
            var parameters = _microflowService.GetParameters(mf);
            foreach (var param in parameters)
            {
                dto.Parameters.Add(new MicroflowParameterDto
                {
                    Name = param.Name,
                    TypeName = DomainModelReader.GetDataTypeName(param.Type)
                });
            }

            // Get activities with detailed properties
            var activities = _microflowService.GetAllMicroflowActivities(mf);
            var index = 0;
            foreach (var activity in activities)
            {
                var activityDto = ExtractActivityDetails(activity, index);
                dto.Activities.Add(activityDto);
                index++;
            }

            // Enrich with flow control elements via Untyped Model API
            EnrichWithFlowControl(dto, mf);

            return dto;
        }
        return null;
    }

    // Typed Extensions API cannot read decisions, loops, merges, or sequence flows.
    // The Untyped Model API provides raw metamodel access to fill in these gaps.
    private void EnrichWithFlowControl(MicroflowDto dto, IMicroflow microflow)
    {
        try
        {
            var untypedRoot = _untypedModelAccessService.GetUntypedModel(_model);

            // Find the microflow unit by looking through all microflow units
            IModelUnit? microflowUnit = null;
            foreach (var unit in untypedRoot.GetUnitsOfType("Microflows$Microflow"))
            {
                if (unit.QualifiedName == dto.QualifiedName)
                {
                    microflowUnit = unit;
                    break;
                }
            }

            if (microflowUnit == null) return;

            // ID-to-label map translates opaque GUIDs into readable references like "Activity[2](Commit)"
            var idLabelMap = new Dictionary<string, string>();

            // Map ActionActivity elements to their indices
            var actionActivities = microflowUnit.GetElementsOfType("Microflows$ActionActivity").ToList();
            for (int i = 0; i < actionActivities.Count; i++)
            {
                var aa = actionActivities[i];
                var caption = SafeGetStringProperty(aa, "caption") ?? $"Activity{i}";
                idLabelMap[aa.ID.ToString()] = $"Activity[{i}]({caption})";
            }

            // Read StartEvent
            foreach (var startEvent in microflowUnit.GetElementsOfType("Microflows$StartEvent"))
            {
                idLabelMap[startEvent.ID.ToString()] = "Start";
            }

            // Read EndEvents
            var endIndex = 0;
            foreach (var endEvent in microflowUnit.GetElementsOfType("Microflows$EndEvent"))
            {
                var returnVal = SafeGetStringProperty(endEvent, "returnValue");
                var label = returnVal != null ? $"End(return={returnVal})" : "End";
                if (endIndex > 0) label = $"End{endIndex}";
                idLabelMap[endEvent.ID.ToString()] = label;
                endIndex++;
            }

            // Read ExclusiveSplits (decisions)
            foreach (var split in microflowUnit.GetElementsOfType("Microflows$ExclusiveSplit"))
            {
                var caption = SafeGetStringProperty(split, "caption");
                var documentation = SafeGetStringProperty(split, "documentation");

                // Try to read the split condition expression
                string? conditionExpr = null;
                try
                {
                    var conditionElements = split.GetElements().ToList();
                    foreach (var condEl in conditionElements)
                    {
                        if (condEl.Type == "Microflows$ExpressionSplitCondition")
                        {
                            conditionExpr = SafeGetStringProperty(condEl, "expression");
                            break;
                        }
                        if (condEl.Type == "Microflows$RuleSplitCondition")
                        {
                            conditionExpr = "[Rule-based]";
                            break;
                        }
                    }
                }
                catch { }

                var fcDto = new FlowControlElementDto
                {
                    Id = split.ID.ToString(),
                    Type = "ExclusiveSplit",
                    Caption = caption,
                    Condition = conditionExpr,
                    Documentation = documentation
                };
                dto.FlowControlElements.Add(fcDto);
                idLabelMap[split.ID.ToString()] = $"Decision({caption ?? "?"})";
            }

            // Read InheritanceSplits
            foreach (var split in microflowUnit.GetElementsOfType("Microflows$InheritanceSplit"))
            {
                var caption = SafeGetStringProperty(split, "caption");
                var splitVar = SafeGetStringProperty(split, "splitVariableName");
                var documentation = SafeGetStringProperty(split, "documentation");

                var fcDto = new FlowControlElementDto
                {
                    Id = split.ID.ToString(),
                    Type = "InheritanceSplit",
                    Caption = caption,
                    SplitVariableName = splitVar,
                    Documentation = documentation
                };
                dto.FlowControlElements.Add(fcDto);
                idLabelMap[split.ID.ToString()] = $"InheritanceSplit({caption ?? splitVar ?? "?"})";
            }

            // Read ExclusiveMerges
            foreach (var merge in microflowUnit.GetElementsOfType("Microflows$ExclusiveMerge"))
            {
                var fcDto = new FlowControlElementDto
                {
                    Id = merge.ID.ToString(),
                    Type = "ExclusiveMerge"
                };
                dto.FlowControlElements.Add(fcDto);
                idLabelMap[merge.ID.ToString()] = "Merge";
            }

            // Read LoopedActivities
            foreach (var loop in microflowUnit.GetElementsOfType("Microflows$LoopedActivity"))
            {
                var documentation = SafeGetStringProperty(loop, "documentation");
                string? listVar = null;
                string? loopVar = null;

                // Try to read loop source (IterableList or WhileLoopCondition)
                try
                {
                    foreach (var loopChild in loop.GetElements())
                    {
                        if (loopChild.Type == "Microflows$IterableList")
                        {
                            listVar = SafeGetStringProperty(loopChild, "listVariableName");
                            loopVar = SafeGetStringProperty(loopChild, "variableName");
                            break;
                        }
                    }
                }
                catch { }

                var fcDto = new FlowControlElementDto
                {
                    Id = loop.ID.ToString(),
                    Type = "LoopedActivity",
                    Documentation = documentation,
                    IteratedListVariable = listVar,
                    LoopVariableName = loopVar
                };
                dto.FlowControlElements.Add(fcDto);
                idLabelMap[loop.ID.ToString()] = $"Loop(${listVar} as ${loopVar})";
            }

            // Read ErrorEvents
            foreach (var errorEvent in microflowUnit.GetElementsOfType("Microflows$ErrorEvent"))
            {
                idLabelMap[errorEvent.ID.ToString()] = "ErrorEvent";
            }

            // Read Annotations
            foreach (var annotation in microflowUnit.GetElementsOfType("Microflows$Annotation"))
            {
                var caption = SafeGetStringProperty(annotation, "caption");
                if (!string.IsNullOrEmpty(caption))
                    dto.Annotations.Add(caption);
            }

            // Read SequenceFlows
            foreach (var flow in microflowUnit.GetElementsOfType("Microflows$SequenceFlow"))
            {
                var originId = SafeGetStringProperty(flow, "origin") ?? "";
                var destId = SafeGetStringProperty(flow, "destination") ?? "";
                var isErrorHandler = SafeGetBoolProperty(flow, "isErrorHandler");

                // Try to read case value
                string? caseValue = null;
                try
                {
                    foreach (var child in flow.GetElements())
                    {
                        if (child.Type.Contains("Case"))
                        {
                            caseValue = SafeGetStringProperty(child, "value");
                            break;
                        }
                    }
                }
                catch { }

                // Use labels instead of raw IDs when available
                var originLabel = idLabelMap.GetValueOrDefault(originId, originId);
                var destLabel = idLabelMap.GetValueOrDefault(destId, destId);

                dto.SequenceFlows.Add(new SequenceFlowDto
                {
                    OriginId = originLabel,
                    DestinationId = destLabel,
                    CaseValue = caseValue,
                    IsErrorHandler = isErrorHandler
                });
            }
        }
        catch (Exception ex)
        {
            _logService.Info($"AIDE Lite: Could not read flow control for {dto.QualifiedName}: {ex.Message}");
        }
    }

    // Untyped Model API can throw on missing/renamed properties — safe wrappers prevent cascading failures
    private static string? SafeGetStringProperty(IModelStructure structure, string propertyName)
    {
        try
        {
            var prop = structure.GetProperty(propertyName);
            return prop?.Value as string;
        }
        catch
        {
            return null;
        }
    }

    private static bool SafeGetBoolProperty(IModelStructure structure, string propertyName)
    {
        try
        {
            var prop = structure.GetProperty(propertyName);
            return prop?.Value is bool b && b;
        }
        catch
        {
            return false;
        }
    }

    private MicroflowActivityDto ExtractActivityDetails(IActivity activity, int index)
    {
        var dto = new MicroflowActivityDto
        {
            Index = index,
            Type = activity.GetType().Name,
            Description = activity.ToString() ?? activity.GetType().Name
        };

        // Try to extract detailed properties from IActionActivity
        if (activity is IActionActivity actionActivity)
        {
            try { dto.Caption = actionActivity.Caption; } catch { }
            try { dto.Disabled = actionActivity.Disabled; } catch { }

            var action = actionActivity.Action;
            if (action != null)
            {
                ExtractActionDetails(action, dto);
            }
        }

        // Clean up the Type name to be more readable
        dto.Type = SimplifyTypeName(dto.Type);

        return dto;
    }

    private void ExtractActionDetails(IMicroflowAction action, MicroflowActivityDto dto)
    {
        try
        {
            switch (action)
            {
                case IRetrieveAction retrieveAction:
                    dto.Type = "Retrieve";
                    ExtractRetrieveDetails(retrieveAction, dto);
                    break;

                case IChangeObjectAction changeAction:
                    dto.Type = "ChangeObject";
                    ExtractChangeObjectDetails(changeAction, dto);
                    break;

                case ICreateObjectAction createAction:
                    dto.Type = "CreateObject";
                    ExtractCreateObjectDetails(createAction, dto);
                    break;

                case ICommitAction commitAction:
                    dto.Type = "Commit";
                    ExtractCommitDetails(commitAction, dto);
                    break;

                case IDeleteAction deleteAction:
                    dto.Type = "Delete";
                    ExtractDeleteDetails(deleteAction, dto);
                    break;

                case IMicroflowCallAction callAction:
                    dto.Type = "MicroflowCall";
                    ExtractMicroflowCallDetails(callAction, dto);
                    break;

                case IAggregateListAction aggregateAction:
                    dto.Type = "AggregateList";
                    ExtractAggregateListDetails(aggregateAction, dto);
                    break;

                case ICreateListAction createListAction:
                    dto.Type = "CreateList";
                    ExtractCreateListDetails(createListAction, dto);
                    break;

                case IRollbackAction rollbackAction:
                    dto.Type = "Rollback";
                    ExtractRollbackDetails(rollbackAction, dto);
                    break;

                case IChangeListAction changeListAction:
                    dto.Type = "ChangeList";
                    ExtractChangeListDetails(changeListAction, dto);
                    break;

                case IListOperationAction listOpAction:
                    dto.Type = "ListOperation";
                    ExtractListOperationDetails(listOpAction, dto);
                    break;

                case IJavaActionCallAction javaAction:
                    dto.Type = "JavaActionCall";
                    break;
            }
        }
        catch
        {
            // If any extraction fails, we still have the basic Type and Description
        }
    }

    private void ExtractRetrieveDetails(IRetrieveAction action, MicroflowActivityDto dto)
    {
        try { dto.OutputVariableName = action.OutputVariableName; } catch { }

        try
        {
            if (action.RetrieveSource is IDatabaseRetrieveSource dbSource)
            {
                try { dto.XPathConstraint = dbSource.XPathConstraint; } catch { }
                try { dto.EntityName = dbSource.Entity?.FullName; } catch { }
                try { dto.RetrieveFirstOnly = dbSource.RetrieveJustFirstItem; } catch { }
            }
            else if (action.RetrieveSource is IAssociationRetrieveSource assocSource)
            {
                dto.Type = "Retrieve(Association)";
                try { dto.AssociationName = assocSource.Association?.Name; } catch { }
                try { dto.ChangeVariableName = assocSource.StartVariableName; } catch { }
            }
        }
        catch { }
    }

    private void ExtractChangeObjectDetails(IChangeObjectAction action, MicroflowActivityDto dto)
    {
        try { dto.ChangeVariableName = action.ChangeVariableName; } catch { }
        try { dto.CommitSetting = action.Commit.ToString(); } catch { }
        ExtractMemberChanges(action, dto);
    }

    private void ExtractCreateObjectDetails(ICreateObjectAction action, MicroflowActivityDto dto)
    {
        try { dto.OutputVariableName = action.OutputVariableName; } catch { }
        try { dto.EntityName = action.Entity?.FullName; } catch { }
        try { dto.CommitSetting = action.Commit.ToString(); } catch { }
        ExtractMemberChanges(action, dto);
    }

    private void ExtractMemberChanges(IChangeMembersAction action, MicroflowActivityDto dto)
    {
        try
        {
            var items = action.GetItems();
            if (items != null && items.Any())
            {
                dto.MemberChanges = new List<MemberChangeDto>();
                foreach (var item in items)
                {
                    try
                    {
                        var attrName = "unknown";
                        if (item.MemberType is AttributeMemberChangeType attrChange)
                            attrName = attrChange.Attribute?.Name ?? "unknown";

                        dto.MemberChanges.Add(new MemberChangeDto
                        {
                            AttributeName = attrName,
                            ValueExpression = item.Value?.ToString() ?? ""
                        });
                    }
                    catch { }
                }
            }
        }
        catch { }
    }

    private void ExtractCommitDetails(ICommitAction action, MicroflowActivityDto dto)
    {
        try { dto.ChangeVariableName = action.CommitVariableName; } catch { }
        try { dto.WithEvents = action.WithEvents; } catch { }
    }

    private void ExtractDeleteDetails(IDeleteAction action, MicroflowActivityDto dto)
    {
        try { dto.ChangeVariableName = action.DeleteVariableName; } catch { }
    }

    private void ExtractMicroflowCallDetails(IMicroflowCallAction action, MicroflowActivityDto dto)
    {
        try { dto.OutputVariableName = action.OutputVariableName; } catch { }
        try { dto.CalledMicroflow = action.MicroflowCall?.Microflow?.FullName; } catch { }
    }

    private void ExtractAggregateListDetails(IAggregateListAction action, MicroflowActivityDto dto)
    {
        try { dto.OutputVariableName = action.OutputVariableName; } catch { }
        try { dto.ListVariableName = action.InputListVariableName; } catch { }
        try { dto.AggregateFunction = action.AggregateFunction.ToString(); } catch { }
    }

    private void ExtractCreateListDetails(ICreateListAction action, MicroflowActivityDto dto)
    {
        try { dto.OutputVariableName = action.OutputVariableName; } catch { }
        try { dto.EntityName = action.Entity?.FullName; } catch { }
    }

    private void ExtractRollbackDetails(IRollbackAction action, MicroflowActivityDto dto)
    {
        try { dto.ChangeVariableName = action.RollbackVariableName; } catch { }
    }

    private void ExtractChangeListDetails(IChangeListAction action, MicroflowActivityDto dto)
    {
        try { dto.ListVariableName = action.ChangeVariableName; } catch { }
    }

    private void ExtractListOperationDetails(IListOperationAction action, MicroflowActivityDto dto)
    {
        try { dto.OutputVariableName = action.OutputVariableName; } catch { }
    }

    private static string SimplifyTypeName(string typeName)
    {
        // Strip C# interface prefix (I) and Mendix suffixes (Activity/Action) for cleaner display
        if (typeName.StartsWith("I") && typeName.Length > 1 && char.IsUpper(typeName[1]))
            typeName = typeName[1..];
        if (typeName.EndsWith("Activity"))
            typeName = typeName[..^8];
        if (typeName.EndsWith("Action"))
            typeName = typeName[..^6];
        return typeName;
    }

    /// <summary>
    /// Recursive traversal — GetModuleDocuments doesn't reliably find deeply nested items.
    /// Walk the folder tree manually to ensure all documents are found regardless of depth.
    /// </summary>
    internal static IEnumerable<T> GetAllDocumentsRecursive<T>(IFolderBase folder) where T : class
    {
        foreach (var doc in folder.GetDocuments())
        {
            if (doc is T typed)
                yield return typed;
        }
        foreach (var subfolder in folder.GetFolders())
        {
            foreach (var item in GetAllDocumentsRecursive<T>(subfolder))
                yield return item;
        }
    }
}
