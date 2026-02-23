// ============================================================================
// AIDE Lite - AI-powered IDE extension for Mendix Studio Pro
// Copyright (c) 2025-2026 Neel Desai / Golden Earth Software Consulting Inc.
// Microflow generator — creates and edits microflows with 21 activity types
// ============================================================================

using System.Text.Json;
using AideLite.ModelReaders;
using AideLite.Models.MicroflowInstructions;
using Mendix.StudioPro.ExtensionsAPI.Model;
using Mendix.StudioPro.ExtensionsAPI.Model.DataTypes;
using Mendix.StudioPro.ExtensionsAPI.Model.DomainModels;
using Mendix.StudioPro.ExtensionsAPI.Model.MicroflowExpressions;
using Mendix.StudioPro.ExtensionsAPI.Model.Microflows;
using Mendix.StudioPro.ExtensionsAPI.Model.Microflows.Actions;
using Mendix.StudioPro.ExtensionsAPI.Model.Projects;
using Mendix.StudioPro.ExtensionsAPI.Services;

namespace AideLite.ModelWriters;

public class MicroflowGenerator
{
    private readonly IModel _model;
    private readonly IMicroflowService _microflowService;
    private readonly IMicroflowActivitiesService _activitiesService;
    private readonly IMicroflowExpressionService _expressionService;
    private readonly IDomainModelService _domainModelService;
    private readonly TransactionManager _transactionManager;
    private readonly ILogService _logService;

    public MicroflowGenerator(
        IModel model,
        IMicroflowService microflowService,
        IMicroflowActivitiesService activitiesService,
        IMicroflowExpressionService expressionService,
        IDomainModelService domainModelService,
        TransactionManager transactionManager,
        ILogService logService)
    {
        _model = model;
        _microflowService = microflowService;
        _activitiesService = activitiesService;
        _expressionService = expressionService;
        _domainModelService = domainModelService;
        _transactionManager = transactionManager;
        _logService = logService;
    }

    public string CreateMicroflow(IModule module, CreateMicroflowInstruction instruction)
    {
        return _transactionManager.ExecuteInTransaction(
            $"Create microflow {instruction.Name}",
            () => CreateMicroflowInternal(module, instruction));
    }

    /// <summary>
    /// Core microflow creation logic without transaction wrapper.
    /// Can be called from within an existing transaction (e.g., ReplaceMicroflow).
    /// </summary>
    internal string CreateMicroflowInternal(IModule module, CreateMicroflowInstruction instruction)
    {
        // Build typed parameter list from instruction strings
        var parameters = new List<(string, DataType)>();
        if (instruction.Parameters != null)
        {
            foreach (var p in instruction.Parameters)
            {
                var dt = ResolveDataType(p.Type, p.EntityQualifiedName);
                parameters.Add((p.Name, dt));
            }
        }

        // Determine return type with a proper default expression
        MicroflowReturnValue? returnValue = null;
        if (!string.IsNullOrEmpty(instruction.ReturnType) &&
            !instruction.ReturnType.Equals("Void", StringComparison.OrdinalIgnoreCase))
        {
            var returnDataType = ResolveDataType(instruction.ReturnType, instruction.ReturnEntityQualifiedName);
            var defaultExpr = GetDefaultExpression(instruction.ReturnType);
            var returnExpr = _expressionService.CreateFromString(defaultExpr);
            returnValue = new MicroflowReturnValue(returnDataType, returnExpr);
        }

        // null returnValue = void return type (Mendix API convention)
        var microflow = _microflowService.CreateMicroflow(
            _model,
            module,
            instruction.Name,
            returnValue,
            parameters.ToArray());

        // Activities are inserted in REVERSE order by the Mendix API —
        // the last item in the array becomes the first activity after Start
        InsertActivities(microflow, instruction.Activities, module, instruction.Name);

        _logService.Info($"AIDE Lite: Created microflow '{module.Name}.{instruction.Name}'");
        return $"{module.Name}.{instruction.Name}";
    }

    /// <summary>
    /// Find a microflow by name within a module.
    /// </summary>
    internal IMicroflow? FindMicroflowInModule(IModule module, string microflowName)
    {
        return MicroflowReader.GetAllDocumentsRecursive<IMicroflow>(module)
            .FirstOrDefault(mf => string.Equals(mf.Name, microflowName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Rename an existing microflow. Mendix auto-updates all references.
    /// </summary>
    public string RenameMicroflow(IModule module, string currentName, string newName)
    {
        return _transactionManager.ExecuteInTransaction(
            $"Rename microflow {currentName} to {newName}",
            () =>
            {
                var microflow = FindMicroflowInModule(module, currentName);
                if (microflow == null)
                    throw new InvalidOperationException($"Microflow '{currentName}' not found in module '{module.Name}'");

                microflow.Name = newName;
                _logService.Info($"AIDE Lite: Renamed microflow '{module.Name}.{currentName}' to '{module.Name}.{newName}'");
                return $"{module.Name}.{newName}";
            });
    }

    /// <summary>
    /// Add activities to an existing microflow (inserted after start, before existing activities).
    /// </summary>
    public string AddActivitiesToMicroflow(IModule module, string microflowName, List<ActivityInstruction> activities)
    {
        return _transactionManager.ExecuteInTransaction(
            $"Add activities to microflow {microflowName}",
            () =>
            {
                var microflow = FindMicroflowInModule(module, microflowName);
                if (microflow == null)
                    throw new InvalidOperationException($"Microflow '{microflowName}' not found in module '{module.Name}'");

                InsertActivities(microflow, activities, module, microflowName);

                _logService.Info($"AIDE Lite: Added {activities.Count} activities to '{module.Name}.{microflowName}'");
                return $"{module.Name}.{microflowName}";
            });
    }

    /// <summary>
    /// Add activities to an existing microflow, inserted before a specific existing activity.
    /// </summary>
    public string AddActivitiesToMicroflowBefore(IModule module, string microflowName, int activityIndex, List<ActivityInstruction> activities)
    {
        return _transactionManager.ExecuteInTransaction(
            $"Add activities before index {activityIndex} in {microflowName}",
            () =>
            {
                var microflow = FindMicroflowInModule(module, microflowName);
                if (microflow == null)
                    throw new InvalidOperationException($"Microflow '{microflowName}' not found in module '{module.Name}'");

                var existingActivities = _microflowService.GetAllMicroflowActivities(microflow).ToList();
                if (activityIndex < 0 || activityIndex >= existingActivities.Count)
                    throw new InvalidOperationException($"Activity index {activityIndex} is out of range (microflow has {existingActivities.Count} activities)");

                var targetActivity = existingActivities[activityIndex];

                var newActivities = new List<IActionActivity>();
                foreach (var actInstr in activities)
                {
                    var activity = CreateActivity(actInstr, module, microflow);
                    if (activity != null)
                        newActivities.Add(activity);
                }

                if (newActivities.Count > 0)
                {
                    var inserted = _microflowService.TryInsertBeforeActivity(targetActivity, newActivities.ToArray());
                    if (!inserted)
                        _logService.Error($"AIDE Lite: TryInsertBeforeActivity failed for microflow '{microflowName}' at index {activityIndex}");
                }

                _logService.Info($"AIDE Lite: Added {activities.Count} activities before index {activityIndex} in '{module.Name}.{microflowName}'");
                return $"{module.Name}.{microflowName}";
            });
    }

    /// <summary>
    /// Replace a microflow: excludes the original (renamed as backup), creates a new one with the original name.
    /// All callers auto-resolve to the new version.
    /// </summary>
    public string ReplaceMicroflow(IModule module, string currentName, CreateMicroflowInstruction newInstruction)
    {
        return _transactionManager.ExecuteInTransaction(
            $"Replace microflow {currentName}",
            () =>
            {
                var existing = FindMicroflowInModule(module, currentName);
                if (existing == null)
                    throw new InvalidOperationException($"Microflow '{currentName}' not found in module '{module.Name}'");

                // Rename old microflow as backup and exclude it from the project.
                // Callers auto-resolve to the new microflow with the original name.
                var timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
                var backupName = $"_REPLACED_{currentName}_{timestamp}";
                existing.Name = backupName;
                existing.Excluded = true;
                _logService.Info($"AIDE Lite: Backed up original as '{module.Name}.{backupName}' (excluded)");

                // Create new microflow with the original name
                newInstruction.Name = currentName;
                newInstruction.ModuleName = module.Name;
                var qualifiedName = CreateMicroflowInternal(module, newInstruction);

                _logService.Info($"AIDE Lite: Replaced microflow '{qualifiedName}' (backup: {backupName})");
                return qualifiedName;
            });
    }

    /// <summary>
    /// Edit properties of an existing activity in-place. Only non-null fields in the instruction are applied.
    /// </summary>
    public string EditMicroflowActivity(IModule module, string microflowName, int activityIndex, EditActivityInstruction edits)
    {
        return _transactionManager.ExecuteInTransaction(
            $"Edit activity {activityIndex} in {microflowName}",
            () =>
            {
                var microflow = FindMicroflowInModule(module, microflowName);
                if (microflow == null)
                    throw new InvalidOperationException($"Microflow '{microflowName}' not found in module '{module.Name}'");

                var activities = _microflowService.GetAllMicroflowActivities(microflow).ToList();
                if (activityIndex < 0 || activityIndex >= activities.Count)
                    throw new InvalidOperationException($"Activity index {activityIndex} is out of range (microflow has {activities.Count} activities)");

                var activity = activities[activityIndex];
                if (activity is not IActionActivity actionActivity)
                    throw new InvalidOperationException($"Activity at index {activityIndex} is not an action activity and cannot be edited");

                // Apply common properties
                if (edits.Caption != null)
                    actionActivity.Caption = edits.Caption;
                if (edits.Disabled.HasValue)
                    actionActivity.Disabled = edits.Disabled.Value;

                var action = actionActivity.Action;
                if (action == null)
                    throw new InvalidOperationException($"Activity at index {activityIndex} has no action");

                var changes = new List<string>();

                switch (action)
                {
                    case IRetrieveAction retrieveAction:
                        ApplyRetrieveEdits(retrieveAction, edits, module, changes);
                        break;

                    case IChangeObjectAction changeAction:
                        ApplyChangeObjectEdits(changeAction, edits, module, changes);
                        break;

                    case ICreateObjectAction createAction:
                        ApplyCreateObjectEdits(createAction, edits, module, changes);
                        break;

                    case ICommitAction commitAction:
                        ApplyCommitEdits(commitAction, edits, changes);
                        break;

                    case IDeleteAction deleteAction:
                        ApplyDeleteEdits(deleteAction, edits, changes);
                        break;

                    case IMicroflowCallAction callAction:
                        ApplyMicroflowCallEdits(callAction, edits, changes);
                        break;

                    case IAggregateListAction aggregateAction:
                        ApplyAggregateListEdits(aggregateAction, edits, changes);
                        break;

                    case ICreateListAction createListAction:
                        ApplyCreateListEdits(createListAction, edits, module, changes);
                        break;

                    case IRollbackAction rollbackAction:
                        if (edits.ChangeVariableName != null)
                        {
                            rollbackAction.RollbackVariableName = edits.ChangeVariableName;
                            changes.Add($"variable=${edits.ChangeVariableName}");
                        }
                        break;

                    default:
                        throw new InvalidOperationException($"Activity at index {activityIndex} has unsupported action type '{action.GetType().Name}'");
                }

                if (edits.Caption != null) changes.Add($"caption='{edits.Caption}'");
                if (edits.Disabled.HasValue) changes.Add($"disabled={edits.Disabled.Value}");

                var summary = changes.Count > 0 ? string.Join(", ", changes) : "no changes applied";
                _logService.Info($"AIDE Lite: Edited activity {activityIndex} in '{module.Name}.{microflowName}': {summary}");
                return $"{module.Name}.{microflowName} activity[{activityIndex}]: {summary}";
            });
    }

    private void ApplyRetrieveEdits(IRetrieveAction action, EditActivityInstruction edits, IModule module, List<string> changes)
    {
        if (edits.OutputVariableName != null)
        {
            action.OutputVariableName = edits.OutputVariableName;
            changes.Add($"outputVariable=${edits.OutputVariableName}");
        }

        if (action.RetrieveSource is IDatabaseRetrieveSource dbSource)
        {
            if (edits.XPathConstraint != null)
            {
                dbSource.XPathConstraint = edits.XPathConstraint;
                changes.Add($"xpath='{edits.XPathConstraint}'");
            }
            if (edits.EntityName != null)
            {
                dbSource.Entity = _model.ToQualifiedName<IEntity>(edits.EntityName);
                changes.Add($"entity={edits.EntityName}");
            }
        }
    }

    private void ApplyChangeObjectEdits(IChangeObjectAction action, EditActivityInstruction edits, IModule module, List<string> changes)
    {
        if (edits.ChangeVariableName != null)
        {
            action.ChangeVariableName = edits.ChangeVariableName;
            changes.Add($"variable=${edits.ChangeVariableName}");
        }
        if (edits.Commit != null)
        {
            action.Commit = Enum.Parse<CommitEnum>(edits.Commit, true);
            changes.Add($"commit={edits.Commit}");
        }
    }

    private void ApplyCreateObjectEdits(ICreateObjectAction action, EditActivityInstruction edits, IModule module, List<string> changes)
    {
        if (edits.OutputVariableName != null)
        {
            action.OutputVariableName = edits.OutputVariableName;
            changes.Add($"outputVariable=${edits.OutputVariableName}");
        }
        if (edits.EntityName != null)
        {
            action.Entity = _model.ToQualifiedName<IEntity>(edits.EntityName);
            changes.Add($"entity={edits.EntityName}");
        }
        if (edits.Commit != null)
        {
            action.Commit = Enum.Parse<CommitEnum>(edits.Commit, true);
            changes.Add($"commit={edits.Commit}");
        }
    }

    private void ApplyCommitEdits(ICommitAction action, EditActivityInstruction edits, List<string> changes)
    {
        if (edits.ChangeVariableName != null)
        {
            action.CommitVariableName = edits.ChangeVariableName;
            changes.Add($"variable=${edits.ChangeVariableName}");
        }
        if (edits.WithEvents.HasValue)
        {
            action.WithEvents = edits.WithEvents.Value;
            changes.Add($"withEvents={edits.WithEvents.Value}");
        }
    }

    private void ApplyDeleteEdits(IDeleteAction action, EditActivityInstruction edits, List<string> changes)
    {
        if (edits.ChangeVariableName != null)
        {
            action.DeleteVariableName = edits.ChangeVariableName;
            changes.Add($"variable=${edits.ChangeVariableName}");
        }
    }

    private void ApplyMicroflowCallEdits(IMicroflowCallAction action, EditActivityInstruction edits, List<string> changes)
    {
        if (edits.OutputVariableName != null)
        {
            action.OutputVariableName = edits.OutputVariableName;
            action.UseReturnVariable = true;
            changes.Add($"outputVariable=${edits.OutputVariableName}");
        }
        if (edits.CalledMicroflowQualifiedName != null)
        {
            var parts = edits.CalledMicroflowQualifiedName.Split('.');
            if (parts.Length == 2)
            {
                var targetModule = _model.Root.GetModules().FirstOrDefault(m => m.Name == parts[0]);
                if (targetModule != null)
                {
                    var calledMf = MicroflowReader.GetAllDocumentsRecursive<IMicroflow>(targetModule)
                        .FirstOrDefault(mf => mf.Name == parts[1]);
                    if (calledMf != null)
                    {
                        action.MicroflowCall.Microflow = calledMf.QualifiedName;
                        changes.Add($"calls={edits.CalledMicroflowQualifiedName}");
                    }
                    else
                    {
                        _logService.Error($"AIDE Lite: Called microflow '{edits.CalledMicroflowQualifiedName}' not found");
                    }
                }
            }
        }
    }

    private void ApplyAggregateListEdits(IAggregateListAction action, EditActivityInstruction edits, List<string> changes)
    {
        if (edits.OutputVariableName != null)
        {
            action.OutputVariableName = edits.OutputVariableName;
            changes.Add($"outputVariable=${edits.OutputVariableName}");
        }
        if (edits.ListVariableName != null)
        {
            action.InputListVariableName = edits.ListVariableName;
            changes.Add($"list=${edits.ListVariableName}");
        }
        if (edits.AggregateFunction != null)
        {
            action.AggregateFunction = Enum.Parse<AggregateFunctionEnum>(edits.AggregateFunction, true);
            changes.Add($"function={edits.AggregateFunction}");
        }
    }

    private void ApplyCreateListEdits(ICreateListAction action, EditActivityInstruction edits, IModule module, List<string> changes)
    {
        if (edits.OutputVariableName != null)
        {
            action.OutputVariableName = edits.OutputVariableName;
            changes.Add($"outputVariable=${edits.OutputVariableName}");
        }
        if (edits.EntityName != null)
        {
            action.Entity = _model.ToQualifiedName<IEntity>(edits.EntityName);
            changes.Add($"entity={edits.EntityName}");
        }
    }

    /// <summary>
    /// Build activities from instructions and insert them into a microflow after start.
    /// </summary>
    private void InsertActivities(IMicroflow microflow, List<ActivityInstruction>? activityInstructions, IModule module, string microflowName)
    {
        if (activityInstructions == null || activityInstructions.Count == 0)
            return;

        var activities = new List<IActionActivity>();
        foreach (var actInstr in activityInstructions)
        {
            var activity = CreateActivity(actInstr, module, microflow);
            if (activity != null)
                activities.Add(activity);
        }

        if (activities.Count > 0)
        {
            var inserted = _microflowService.TryInsertAfterStart(microflow, activities.ToArray());
            if (!inserted)
                _logService.Error($"AIDE Lite: TryInsertAfterStart failed for microflow '{microflowName}'");
        }
    }

    internal IActionActivity? CreateActivity(ActivityInstruction instr, IModule module, IMicroflow microflow)
    {
        switch (instr.Type)
        {
            case "CreateObject": return CreateCreateObjectActivity(instr, module);
            case "ChangeObject": return CreateChangeObjectActivities(instr, module);
            case "Retrieve": return CreateRetrieveActivity(instr, module);
            case "Commit": return CreateCommitActivity(instr);
            case "DeleteObject": return CreateDeleteActivity(instr);
            case "CreateList": return CreateCreateListActivity(instr, module);
            case "AggregateList": return CreateAggregateListActivity(instr);
            case "MicroflowCall": return CreateMicroflowCallActivity(instr);
            case "Rollback": return CreateRollbackActivity(instr);
            case "AssociationRetrieve": return CreateAssociationRetrieveActivity(instr, module);
            case "Sort": return CreateSortActivity(instr, module);
            case "ChangeList": return CreateChangeListActivity(instr);
            case "ListOperation": return CreateListOperationActivity(instr);
            case "FindByExpression": return CreateFindByExpressionActivity(instr);
            case "ChangeAssociation": return CreateChangeAssociationActivity(instr, module);
            case "FilterByAssociation": return CreateFilterByAssociationActivity(instr, module);
            case "FilterByAttribute": return CreateFilterByAttributeActivity(instr, module);
            case "FindByAssociation": return CreateFindByAssociationActivity(instr, module);
            case "FindByAttribute": return CreateFindByAttributeActivity(instr, module);
            case "AggregateByExpression": return CreateAggregateByExpressionActivity(instr);
            case "AggregateByAttribute": return CreateAggregateByAttributeActivity(instr, module);
            default:
                _logService.Error($"AIDE Lite: Unsupported activity type '{instr.Type}' - skipping");
                return null;
        }
    }

    private IActionActivity CreateCreateObjectActivity(ActivityInstruction instr, IModule module)
    {
        var entity = FindEntity(module, instr.Entity!);
        var commitEnum = instr.Commit == true ? CommitEnum.Yes : CommitEnum.No;

        // Note: Member changes require IMicroflowExpression instances which are complex
        // to construct programmatically. Create the object without attribute assignments
        // for now - user can add them manually in Studio Pro.
        return _activitiesService.CreateCreateObjectActivity(
            _model, entity, instr.OutputVariableName ?? "NewObject",
            commitEnum, false, Array.Empty<(string, IMicroflowExpression)>());
    }

    private IActionActivity CreateRetrieveActivity(ActivityInstruction instr, IModule module)
    {
        var entity = FindEntity(module, instr.Entity!);
        var xpathConstraint = instr.XPathConstraint ?? "";
        var retrieveFirst = instr.RetrieveFirstOnly ?? false;

        return _activitiesService.CreateDatabaseRetrieveSourceActivity(
            _model, instr.OutputVariableName ?? "RetrievedList",
            entity, xpathConstraint, retrieveFirst, Array.Empty<AttributeSorting>());
    }

    private IActionActivity CreateCommitActivity(ActivityInstruction instr)
    {
        var withEvents = instr.WithEvents ?? true;
        return _activitiesService.CreateCommitObjectActivity(
            _model, instr.VariableName ?? "", withEvents, false);
    }

    private IActionActivity CreateDeleteActivity(ActivityInstruction instr)
    {
        return _activitiesService.CreateDeleteObjectActivity(
            _model, instr.VariableName ?? "");
    }

    private IActionActivity CreateCreateListActivity(ActivityInstruction instr, IModule module)
    {
        var entity = FindEntity(module, instr.Entity!);
        return _activitiesService.CreateCreateListActivity(
            _model, entity, instr.OutputVariableName ?? "NewList");
    }

    private IActionActivity CreateAggregateListActivity(ActivityInstruction instr)
    {
        var function = Enum.Parse<AggregateFunctionEnum>(instr.Function ?? "Count", true);
        return _activitiesService.CreateAggregateListActivity(
            _model, instr.ListVariableName ?? "",
            instr.OutputVariableName ?? "AggregateResult", function);
    }

    /// <summary>
    /// ChangeObject: No CreateChangeObjectActivity exists in the Extensions API,
    /// so we use CreateChangeAttributeActivity per member change instead.
    /// Returns the first activity (or a no-op commit if no member changes).
    /// </summary>
    private IActionActivity? CreateChangeObjectActivities(ActivityInstruction instr, IModule module)
    {
        if (instr.MemberChanges == null || instr.MemberChanges.Count == 0)
        {
            // If no member changes specified, just create a commit as a placeholder
            var commitEnum = instr.Commit == true ? CommitEnum.Yes : CommitEnum.No;
            return _activitiesService.CreateCommitObjectActivity(
                _model, instr.VariableName ?? "", true, false);
        }

        // For ChangeObject, we need the entity to resolve attributes.
        // The entity name should be provided, or we fall back to looking up the variable.
        IEntity? entity = null;
        if (!string.IsNullOrEmpty(instr.Entity))
        {
            entity = FindEntity(module, instr.Entity);
        }

        if (entity == null)
        {
            _logService.Error("AIDE Lite: ChangeObject requires entity name to resolve attributes");
            return null;
        }

        // Create a change activity for the first member change
        // (Claude API sees this as a single "ChangeObject" tool call)
        var firstChange = instr.MemberChanges[0];
        var attribute = entity.GetAttributes().FirstOrDefault(a => a.Name == firstChange.AttributeName);
        if (attribute == null)
        {
            _logService.Error($"AIDE Lite: Attribute '{firstChange.AttributeName}' not found on entity '{entity.Name}'");
            return null;
        }

        var expression = _expressionService.CreateFromString(firstChange.ValueExpression);
        var commitSetting = instr.Commit == true ? CommitEnum.Yes : CommitEnum.No;

        return _activitiesService.CreateChangeAttributeActivity(
            _model, attribute, ChangeActionItemType.Set, expression,
            instr.VariableName ?? "", commitSetting);
    }

    /// <summary>
    /// MicroflowCall: No CreateMicroflowCallAction factory exists, so we use the
    /// low-level IModel.Create&lt;T&gt;() pattern to build the action manually.
    /// </summary>
    private IActionActivity? CreateMicroflowCallActivity(ActivityInstruction instr)
    {
        if (string.IsNullOrEmpty(instr.CalledMicroflowQualifiedName))
        {
            _logService.Error("AIDE Lite: MicroflowCall requires calledMicroflowQualifiedName");
            return null;
        }

        // Find the called microflow by qualified name (e.g., "MyModule.SUB_Calculate")
        IMicroflow? calledMicroflow = null;
        var parts = instr.CalledMicroflowQualifiedName.Split('.');
        if (parts.Length == 2)
        {
            var targetModule = _model.Root.GetModules().FirstOrDefault(m => m.Name == parts[0]);
            if (targetModule != null)
            {
                calledMicroflow = MicroflowReader.GetAllDocumentsRecursive<IMicroflow>(targetModule)
                    .FirstOrDefault(mf => mf.Name == parts[1]);
            }
        }

        if (calledMicroflow == null)
        {
            _logService.Error($"AIDE Lite: Microflow '{instr.CalledMicroflowQualifiedName}' not found");
            return null;
        }

        // Manual construction via IModel.Create<T>() — three objects wired together
        var activity = _model.Create<IActionActivity>();
        var callAction = _model.Create<IMicroflowCallAction>();
        callAction.MicroflowCall = _model.Create<IMicroflowCall>();
        callAction.MicroflowCall.Microflow = calledMicroflow.QualifiedName;

        if (!string.IsNullOrEmpty(instr.OutputVariableName))
        {
            callAction.OutputVariableName = instr.OutputVariableName;
            callAction.UseReturnVariable = true;
        }

        activity.Action = callAction;

        // Map parameters if provided
        if (instr.ParameterMappings != null)
        {
            var calledParams = _microflowService.GetParameters(calledMicroflow);
            foreach (var mapping in instr.ParameterMappings)
            {
                var param = calledParams.FirstOrDefault(p => p.Name == mapping.ParamName);
                if (param == null) continue;

                var paramMapping = _model.Create<IMicroflowCallParameterMapping>();
                paramMapping.Argument = _expressionService.CreateFromString(mapping.ValueExpression);
                paramMapping.Parameter = param.QualifiedName;
                callAction.MicroflowCall.AddParameterMapping(paramMapping);
            }
        }

        return activity;
    }

    private IActionActivity CreateRollbackActivity(ActivityInstruction instr)
    {
        return _activitiesService.CreateRollbackObjectActivity(
            _model, instr.VariableName ?? "");
    }

    private IActionActivity CreateAssociationRetrieveActivity(ActivityInstruction instr, IModule module)
    {
        var association = FindAssociation(module, instr.AssociationName!);
        return _activitiesService.CreateAssociationRetrieveSourceActivity(
            _model, association, instr.OutputVariableName ?? "RetrievedObject",
            instr.StartingVariableName ?? "");
    }

    private IActionActivity CreateSortActivity(ActivityInstruction instr, IModule module)
    {
        var entity = FindEntity(module, instr.Entity!);
        var attribute = entity.GetAttributes().FirstOrDefault(a => a.Name == instr.AttributeName);
        if (attribute == null)
            throw new InvalidOperationException($"Attribute '{instr.AttributeName}' not found on entity '{entity.Name}'");

        var descending = !(instr.SortAscending ?? true);
        var sorting = new AttributeSorting(attribute, descending);
        return _activitiesService.CreateSortListActivity(
            _model, instr.ListVariableName ?? "", instr.OutputVariableName ?? "SortedList",
            sorting);
    }

    private IActionActivity CreateChangeListActivity(ActivityInstruction instr)
    {
        var operation = Enum.Parse<ChangeListActionOperation>(instr.Function ?? "Add", true);
        var expression = _expressionService.CreateFromString(instr.FilterExpression ?? "empty");
        return _activitiesService.CreateChangeListActivity(
            _model, operation, instr.ListVariableName ?? "", expression);
    }

    private IActionActivity CreateListOperationActivity(ActivityInstruction instr)
    {
        var operationType = (instr.Function ?? "Union").ToLowerInvariant();
        IListOperation listOp;
        switch (operationType)
        {
            case "union":
                var union = _model.Create<IUnion>();
                union.SecondListOrObjectVariableName = instr.SecondListVariableName ?? "";
                listOp = union;
                break;
            case "intersect":
                var intersect = _model.Create<IIntersect>();
                intersect.SecondListOrObjectVariableName = instr.SecondListVariableName ?? "";
                listOp = intersect;
                break;
            case "subtract":
                var subtract = _model.Create<ISubtract>();
                subtract.SecondListOrObjectVariableName = instr.SecondListVariableName ?? "";
                listOp = subtract;
                break;
            case "head":
                listOp = _model.Create<IHead>();
                break;
            case "tail":
                listOp = _model.Create<ITail>();
                break;
            case "contains":
                listOp = _model.Create<IContains>();
                break;
            case "equals":
                listOp = _model.Create<IListEquals>();
                break;
            default:
                throw new InvalidOperationException($"Unsupported list operation '{instr.Function}'. Supported: Union, Intersect, Subtract, Head, Tail, Contains, Equals");
        }

        return _activitiesService.CreateListOperationActivity(
            _model, instr.ListVariableName ?? "", instr.OutputVariableName ?? "ResultList", listOp);
    }

    private IActionActivity CreateFindByExpressionActivity(ActivityInstruction instr)
    {
        var expression = _expressionService.CreateFromString(instr.FilterExpression ?? "true");
        return _activitiesService.CreateFindByExpressionActivity(
            _model, instr.ListVariableName ?? "", instr.OutputVariableName ?? "FoundObject", expression);
    }

    private IActionActivity CreateChangeAssociationActivity(ActivityInstruction instr, IModule module)
    {
        var association = FindAssociation(module, instr.AssociationName!);
        var changeType = Enum.Parse<ChangeActionItemType>(instr.Function ?? "Set", true);
        var expression = _expressionService.CreateFromString(instr.FilterExpression ?? "empty");
        var commitSetting = instr.Commit == true ? CommitEnum.Yes : CommitEnum.No;

        return _activitiesService.CreateChangeAssociationActivity(
            _model, association, changeType, expression,
            instr.VariableName ?? "", commitSetting);
    }

    private IActionActivity CreateFilterByAssociationActivity(ActivityInstruction instr, IModule module)
    {
        var association = FindAssociation(module, instr.AssociationName!);
        var expression = _expressionService.CreateFromString(instr.FilterExpression ?? "true");

        return _activitiesService.CreateFilterListByAssociationActivity(
            _model, association, instr.ListVariableName ?? "",
            instr.OutputVariableName ?? "FilteredList", expression);
    }

    private IActionActivity CreateFilterByAttributeActivity(ActivityInstruction instr, IModule module)
    {
        var entity = FindEntity(module, instr.Entity!);
        var attribute = entity.GetAttributes().FirstOrDefault(a => a.Name == instr.AttributeName);
        if (attribute == null)
            throw new InvalidOperationException($"Attribute '{instr.AttributeName}' not found on entity '{entity.Name}'");

        var expression = _expressionService.CreateFromString(instr.FilterExpression ?? "true");

        return _activitiesService.CreateFilterListByAttributeActivity(
            _model, attribute, instr.ListVariableName ?? "",
            instr.OutputVariableName ?? "FilteredList", expression);
    }

    private IActionActivity CreateFindByAssociationActivity(ActivityInstruction instr, IModule module)
    {
        var association = FindAssociation(module, instr.AssociationName!);
        var expression = _expressionService.CreateFromString(instr.FilterExpression ?? "true");

        return _activitiesService.CreateFindByAssociationActivity(
            _model, association, instr.ListVariableName ?? "",
            instr.OutputVariableName ?? "FoundObject", expression);
    }

    private IActionActivity CreateFindByAttributeActivity(ActivityInstruction instr, IModule module)
    {
        var entity = FindEntity(module, instr.Entity!);
        var attribute = entity.GetAttributes().FirstOrDefault(a => a.Name == instr.AttributeName);
        if (attribute == null)
            throw new InvalidOperationException($"Attribute '{instr.AttributeName}' not found on entity '{entity.Name}'");

        var expression = _expressionService.CreateFromString(instr.FilterExpression ?? "true");

        return _activitiesService.CreateFindByAttributeActivity(
            _model, attribute, instr.ListVariableName ?? "",
            instr.OutputVariableName ?? "FoundObject", expression);
    }

    private IActionActivity CreateAggregateByExpressionActivity(ActivityInstruction instr)
    {
        var expression = _expressionService.CreateFromString(instr.FilterExpression ?? "$currentObject/Amount");
        var function = Enum.Parse<AggregateFunctionEnum>(instr.Function ?? "Sum", true);

        return _activitiesService.CreateAggregateListByExpressionActivity(
            _model, expression, instr.ListVariableName ?? "",
            instr.OutputVariableName ?? "AggregateResult", function);
    }

    private IActionActivity CreateAggregateByAttributeActivity(ActivityInstruction instr, IModule module)
    {
        var entity = FindEntity(module, instr.Entity!);
        var attribute = entity.GetAttributes().FirstOrDefault(a => a.Name == instr.AttributeName);
        if (attribute == null)
            throw new InvalidOperationException($"Attribute '{instr.AttributeName}' not found on entity '{entity.Name}'");

        var function = Enum.Parse<AggregateFunctionEnum>(instr.Function ?? "Sum", true);

        return _activitiesService.CreateAggregateListByAttributeActivity(
            _model, attribute, instr.ListVariableName ?? "",
            instr.OutputVariableName ?? "AggregateResult", function);
    }

    private IAssociation FindAssociation(IModule module, string associationName)
    {
        // Search all associations in the module via domain model service
        var associations = _domainModelService.GetAllAssociations(_model, new[] { module });
        var match = associations.FirstOrDefault(a => a.Association.Name == associationName);
        if (match != null) return match.Association;

        // If qualified name provided (Module.Association), search in target module
        if (associationName.Contains('.'))
        {
            var parts = associationName.Split('.');
            var targetModule = _model.Root.GetModules().FirstOrDefault(m => m.Name == parts[0]);
            if (targetModule != null)
            {
                var allAssocs = _domainModelService.GetAllAssociations(_model, new[] { targetModule });
                match = allAssocs.FirstOrDefault(a => a.Association.Name == parts[1]);
                if (match != null) return match.Association;
            }
        }

        throw new InvalidOperationException($"Association '{associationName}' not found");
    }

    private IEntity FindEntity(IModule module, string entityName)
    {
        // Try qualified name first (Module.Entity)
        if (entityName.Contains('.'))
        {
            var parts = entityName.Split('.');
            var targetModule = _model.Root.GetModules()
                .FirstOrDefault(m => m.Name == parts[0]);
            if (targetModule != null)
            {
                var entity = targetModule.DomainModel.GetEntities()
                    .FirstOrDefault(e => e.Name == parts[1]);
                if (entity != null) return entity;
            }
        }

        // Try in current module
        var localEntity = module.DomainModel.GetEntities()
            .FirstOrDefault(e => e.Name == entityName);
        if (localEntity != null) return localEntity;

        throw new InvalidOperationException($"Entity '{entityName}' not found");
    }

    /// <summary>
    /// Maps string type names (from Claude's JSON) to Mendix DataType values.
    /// Object/List types require a qualified entity name for the generic parameter.
    /// </summary>
    private DataType ResolveDataType(string typeName, string? entityQualifiedName)
    {
        var lower = typeName.ToLowerInvariant();
        return lower switch
        {
            "boolean" => DataType.Boolean,
            "integer" => DataType.Integer,
            "decimal" => DataType.Decimal,
            "string" => DataType.String,
            "datetime" => DataType.DateTime,
            "float" => DataType.Float,
            "void" => DataType.Void,
            "binary" => DataType.Binary,
            "object" when entityQualifiedName != null =>
                DataType.Object(_model.ToQualifiedName<IEntity>(entityQualifiedName)),
            "list" when entityQualifiedName != null =>
                DataType.List(_model.ToQualifiedName<IEntity>(entityQualifiedName)),
            _ => LogAndDefaultToString(typeName)
        };
    }

    private DataType LogAndDefaultToString(string typeName)
    {
        _logService.Error($"AIDE Lite: Unrecognized data type '{typeName}', defaulting to String");
        return DataType.String;
    }

    /// <summary>
    /// Get a sensible default return expression for a given type.
    /// </summary>
    private static string GetDefaultExpression(string typeName) => typeName.ToLowerInvariant() switch
    {
        "boolean" => "false",
        "integer" => "0",
        "decimal" => "0",
        "string" => "''",
        "datetime" => "[%CurrentDateTime%]",
        "float" => "0",
        "object" => "empty",
        "list" => "empty",
        _ => "''"
    };
}
