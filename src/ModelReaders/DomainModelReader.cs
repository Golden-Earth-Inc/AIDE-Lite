// ============================================================================
// AIDE Lite - AI-powered IDE extension for Mendix Studio Pro
// Copyright (c) 2025-2026 Neel Desai / Golden Earth Software Consulting Inc.
// Domain model reader — entities, attributes, associations, and enumerations
// ============================================================================
using AideLite.Models.DTOs;
using Mendix.StudioPro.ExtensionsAPI.Model;
using Mendix.StudioPro.ExtensionsAPI.Model.DomainModels;
using Mendix.StudioPro.ExtensionsAPI.Model.Enumerations;
using Mendix.StudioPro.ExtensionsAPI.Model.Projects;
using Mendix.StudioPro.ExtensionsAPI.Services;

namespace AideLite.ModelReaders;

public class DomainModelReader
{
    private readonly IModel _model;
    private readonly IDomainModelService _domainModelService;

    public DomainModelReader(IModel model, IDomainModelService domainModelService)
    {
        _model = model;
        _domainModelService = domainModelService;
    }

    public List<EntitySummaryDto> GetEntitySummaries(IModule module)
    {
        var entities = new List<EntitySummaryDto>();
        var domainModel = module.DomainModel;

        foreach (var entity in domainModel.GetEntities())
        {
            entities.Add(new EntitySummaryDto
            {
                Name = entity.Name,
                AttributeCount = entity.GetAttributes().Count
            });
        }
        return entities;
    }

    public EntityDto? GetEntityDetails(IModule module, string entityName)
    {
        var domainModel = module.DomainModel;

        foreach (var entity in domainModel.GetEntities())
        {
            if (entity.Name != entityName) continue;

            var dto = new EntityDto
            {
                Name = entity.Name,
                QualifiedName = $"{module.Name}.{entity.Name}",
                Generalization = GetGeneralizationName(entity.Generalization)
            };

            foreach (var attr in entity.GetAttributes())
            {
                dto.Attributes.Add(new AttributeDto
                {
                    Name = attr.Name,
                    TypeName = GetAttributeTypeName(attr.Type)
                });
            }

            // Get associations for this entity
            var associations = _domainModelService.GetAssociationsOfEntity(
                _model, entity, AssociationDirection.Both);

            foreach (var ea in associations)
            {
                dto.Associations.Add(new AssociationDto
                {
                    Name = ea.Association.Name,
                    Parent = ea.Parent.Name,
                    Child = ea.Child.Name,
                    Type = ea.Association.Type.ToString(),
                    Owner = ea.Association.Owner.ToString()
                });
            }

            return dto;
        }
        return null;
    }

    /// <summary>
    /// Get full details for ALL entities in a module in a single pass.
    /// Used to front-load entity details into the cached system prompt.
    /// </summary>
    public List<EntityDto> GetAllEntityDetails(IModule module)
    {
        var result = new List<EntityDto>();
        var domainModel = module.DomainModel;

        foreach (var entity in domainModel.GetEntities())
        {
            var dto = new EntityDto
            {
                Name = entity.Name,
                QualifiedName = $"{module.Name}.{entity.Name}",
                Generalization = GetGeneralizationName(entity.Generalization)
            };

            foreach (var attr in entity.GetAttributes())
            {
                dto.Attributes.Add(new AttributeDto
                {
                    Name = attr.Name,
                    TypeName = GetAttributeTypeName(attr.Type)
                });
            }

            var associations = _domainModelService.GetAssociationsOfEntity(
                _model, entity, AssociationDirection.Both);

            foreach (var ea in associations)
            {
                dto.Associations.Add(new AssociationDto
                {
                    Name = ea.Association.Name,
                    Parent = ea.Parent.Name,
                    Child = ea.Child.Name,
                    Type = ea.Association.Type.ToString(),
                    Owner = ea.Association.Owner.ToString()
                });
            }

            result.Add(dto);
        }

        return result;
    }

    // GetAllAssociations requires passing the model + module array — a quirk of the Extensions API
    public List<AssociationSummaryDto> GetAssociationSummaries(IModule module)
    {
        var summaries = new List<AssociationSummaryDto>();
        var associations = _domainModelService.GetAllAssociations(_model, new[] { module });

        foreach (var ea in associations)
        {
            summaries.Add(new AssociationSummaryDto
            {
                Name = ea.Association.Name,
                Parent = ea.Parent.Name,
                Child = ea.Child.Name,
                Type = ea.Association.Type.ToString()
            });
        }
        return summaries;
    }

    public List<AssociationDto> GetAssociationDetails(IModule module)
    {
        var details = new List<AssociationDto>();
        var associations = _domainModelService.GetAllAssociations(_model, new[] { module });

        foreach (var ea in associations)
        {
            details.Add(new AssociationDto
            {
                Name = ea.Association.Name,
                Parent = ea.Parent.Name,
                Child = ea.Child.Name,
                Type = ea.Association.Type.ToString(),
                Owner = ea.Association.Owner.ToString()
            });
        }
        return details;
    }

    public List<EnumerationSummaryDto> GetEnumerationSummaries(IModule module)
    {
        var summaries = new List<EnumerationSummaryDto>();
        // Reuses MicroflowReader's recursive traversal — enumerations can be nested in folders
        var enumerations = MicroflowReader.GetAllDocumentsRecursive<IEnumeration>(module);

        foreach (var enumeration in enumerations)
        {
            if (enumeration.Excluded) continue;
            summaries.Add(new EnumerationSummaryDto
            {
                Name = enumeration.Name,
                Values = enumeration.GetValues().Select(v => v.Name).ToList()
            });
        }
        return summaries;
    }

    // Pattern-match on concrete attribute type interfaces to produce human-readable type names
    internal static string GetAttributeTypeName(IAttributeType attrType) => attrType switch
    {
        IBooleanAttributeType => "Boolean",
        IIntegerAttributeType => "Integer",
        ILongAttributeType => "Long",
        IDecimalAttributeType => "Decimal",
        IStringAttributeType sat => $"String({sat.Length})",
        IDateTimeAttributeType => "DateTime",
        IAutoNumberAttributeType => "AutoNumber",
        IBinaryAttributeType => "Binary",
        IHashedStringAttributeType => "HashedString",
        IEnumerationAttributeType eat => $"Enum({eat.Enumeration})",
        _ => attrType.GetType().Name
    };

    internal static string GetDataTypeName(Mendix.StudioPro.ExtensionsAPI.Model.DataTypes.DataType dataType) => dataType switch
    {
        Mendix.StudioPro.ExtensionsAPI.Model.DataTypes.BooleanType => "Boolean",
        Mendix.StudioPro.ExtensionsAPI.Model.DataTypes.IntegerType => "Integer",
        Mendix.StudioPro.ExtensionsAPI.Model.DataTypes.DecimalType => "Decimal",
        Mendix.StudioPro.ExtensionsAPI.Model.DataTypes.StringType => "String",
        Mendix.StudioPro.ExtensionsAPI.Model.DataTypes.DateTimeType => "DateTime",
        Mendix.StudioPro.ExtensionsAPI.Model.DataTypes.FloatType => "Float",
        Mendix.StudioPro.ExtensionsAPI.Model.DataTypes.ObjectType odt => $"Object({odt.EntityName})",
        Mendix.StudioPro.ExtensionsAPI.Model.DataTypes.ListType ldt => $"List({ldt.EntityName})",
        Mendix.StudioPro.ExtensionsAPI.Model.DataTypes.EnumerationType edt => $"Enum({edt.EnumerationName})",
        Mendix.StudioPro.ExtensionsAPI.Model.DataTypes.BinaryType => "Binary",
        Mendix.StudioPro.ExtensionsAPI.Model.DataTypes.VoidType => "Void",
        _ => dataType.GetType().Name
    };

    private static string? GetGeneralizationName(IGeneralizationBase? gen) => gen switch
    {
        IGeneralization g => g.Generalization?.ToString(),
        _ => null
    };
}
