using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Grpc;
using BackendConfiguration.Pn.Services.UserPropertyAccess;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Microting.eForm.Infrastructure.Models;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Helpers;
using Microting.eFormApi.BasePn.Infrastructure.Models.Application.Case.CaseEdit;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;

namespace BackendConfiguration.Pn.Services.GrpcServices;

public class CompliancesGrpcService(
    IEFormCoreService coreHelper,
    IBackendConfigurationUserPropertyAccess userPropertyAccess,
    IGrpcSiteResolver siteResolver,
    BackendConfigurationPnDbContext dbContext)
    : BackendConfigurationCompliancesGrpc.BackendConfigurationCompliancesGrpcBase
{
    public override async Task<ReadComplianceCaseResponse> ReadComplianceCase(
        ReadComplianceCaseRequest request,
        ServerCallContext context)
    {
        var sdkSiteId = await siteResolver.GetSdkSiteIdAsync();

        if (request.ExtraId > 0)
        {
            var compliance = await dbContext.Compliances
                .FirstOrDefaultAsync(x => x.Id == request.ExtraId)
                .ConfigureAwait(false);

            if (compliance != null &&
                !await userPropertyAccess.HasAccessAsync(sdkSiteId, compliance.PropertyId)
                    .ConfigureAwait(false))
            {
                throw new RpcException(new Status(StatusCode.PermissionDenied,
                    "Caller has no PropertyWorker access to the compliance's property."));
            }
        }

        var core = await coreHelper.GetCore().ConfigureAwait(false);
        var sdkDbContext = core.DbContextHelper.GetDbContext();
        var language = await sdkDbContext.Languages.FirstAsync().ConfigureAwait(false);

        ReplyElement theCase;
        try
        {
            theCase = await core.CaseRead(request.CaseId, language).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new ReadComplianceCaseResponse { Success = false, Message = ex.Message };
        }

        if (theCase == null)
        {
            return new ReadComplianceCaseResponse { Success = false, Message = "Case not found" };
        }

        var response = new ReadComplianceCaseResponse
        {
            Success = true,
            Message = string.Empty,
            Id = theCase.Id,
            Label = theCase.Label ?? string.Empty,
            DoneAt = theCase.DoneAt.ToString("O", CultureInfo.InvariantCulture),
            DoneById = theCase.DoneById,
            UnitId = theCase.UnitId
        };

        foreach (var element in theCase.ElementList)
        {
            response.ElementList.Add(MapElement(element));
        }

        return response;
    }

    public override async Task<UpdateComplianceCaseResponse> UpdateComplianceCase(
        UpdateComplianceCaseRequest request,
        ServerCallContext context)
    {
        var sdkSiteId = await siteResolver.GetSdkSiteIdAsync();

        var compliance = await dbContext.Compliances
            .SingleOrDefaultAsync(x => x.Id == request.ExtraId)
            .ConfigureAwait(false);

        if (compliance == null)
        {
            return new UpdateComplianceCaseResponse { Success = false, Message = "Compliance not found" };
        }

        if (!await userPropertyAccess.HasAccessAsync(sdkSiteId, compliance.PropertyId)
                .ConfigureAwait(false))
        {
            throw new RpcException(new Status(StatusCode.PermissionDenied,
                "Caller has no PropertyWorker access to the compliance's property."));
        }

        var replyRequest = new ReplyRequest
        {
            Id = request.Id,
            Label = request.Label,
            DoneAt = DateTime.TryParse(request.DoneAt, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var doneAt)
                ? doneAt
                : DateTime.UtcNow,
            ExtraId = request.ExtraId,
            SiteId = request.SiteId,
            ElementList = request.ElementList.Select(MapCaseEditElement).ToList()
        };

        var checkListValueList = new List<string>();
        var fieldValueList = new List<string>();
        replyRequest.ElementList.ForEach(element =>
        {
            checkListValueList.AddRange(CaseUpdateHelper.GetCheckList(element));
            fieldValueList.AddRange(CaseUpdateHelper.GetFieldList(element));
        });

        var core = await coreHelper.GetCore().ConfigureAwait(false);
        var sdkDbContext = core.DbContextHelper.GetDbContext();
        var language = await sdkDbContext.Languages.FirstAsync().ConfigureAwait(false);

        try
        {
            await compliance.Delete(dbContext).ConfigureAwait(false);
            await core.CaseUpdate(replyRequest.Id, fieldValueList, checkListValueList)
                .ConfigureAwait(false);
            await core.CaseUpdateFieldValues(replyRequest.Id, language).ConfigureAwait(false);

            var foundCase = await sdkDbContext.Cases
                .FirstOrDefaultAsync(x => x.Id == replyRequest.Id)
                .ConfigureAwait(false);

            if (foundCase != null)
            {
                var newDoneAt = new DateTime(
                    replyRequest.DoneAt.Year, replyRequest.DoneAt.Month, replyRequest.DoneAt.Day,
                    0, 0, 0, DateTimeKind.Utc);
                foundCase.DoneAtUserModifiable = newDoneAt;
                foundCase.DoneAt = newDoneAt;
                foundCase.SiteId = replyRequest.SiteId;
                foundCase.Status = 100;
                foundCase.WorkflowState = Constants.WorkflowStates.Created;
                await foundCase.Update(sdkDbContext).ConfigureAwait(false);
            }

            return new UpdateComplianceCaseResponse { Success = true, Message = string.Empty };
        }
        catch (Exception ex)
        {
            return new UpdateComplianceCaseResponse { Success = false, Message = ex.Message };
        }
    }

    private static ReplyElementItem MapElement(Element element)
    {
        var item = new ReplyElementItem
        {
            Id = element.Id,
            Label = element.Label ?? string.Empty
        };

        switch (element)
        {
            case CheckListValue clv:
                item.Status = clv.Status ?? string.Empty;
                MapDataItems(clv.DataItemList, item.DataItems);
                MapDataItemGroups(clv.DataItemGroupList, item.DataItemGroups);
                break;
            case DataElement de:
                MapDataItems(de.DataItemList, item.DataItems);
                MapDataItemGroups(de.DataItemGroupList, item.DataItemGroups);
                break;
            case GroupElement ge:
                foreach (var child in ge.ElementList)
                {
                    item.ElementList.Add(MapElement(child));
                }
                break;
        }

        return item;
    }

    private static void MapDataItems(
        List<DataItem> source,
        Google.Protobuf.Collections.RepeatedField<DataItemInfo> target)
    {
        if (source == null) return;
        foreach (var di in source)
        {
            target.Add(new DataItemInfo
            {
                Id = di.Id,
                Label = di.Label ?? string.Empty,
                Description = di.Description?.InderValue ?? string.Empty,
                FieldType = di.GetType().Name,
                Value = ExtractValue(di),
                Mandatory = di.Mandatory,
                ReadOnly = di.ReadOnly,
                Color = di.Color ?? string.Empty,
                DisplayOrder = di.DisplayOrder
            });
        }
    }

    private static void MapDataItemGroups(
        List<DataItemGroup> source,
        Google.Protobuf.Collections.RepeatedField<DataItemGroupInfo> target)
    {
        if (source == null) return;
        foreach (var group in source)
        {
            var g = new DataItemGroupInfo
            {
                Id = int.TryParse(group.Id, out var gid) ? gid : 0,
                Label = group.Label ?? string.Empty,
                Description = group.Description ?? string.Empty,
                Color = group.Color ?? string.Empty,
                DisplayOrder = group.DisplayOrder
            };
            MapDataItems(group.DataItemList, g.DataItems);
            target.Add(g);
        }
    }

    private static string ExtractValue(DataItem di)
    {
        return di switch
        {
            Date d => d.DefaultValue ?? string.Empty,
            Number n => n.DefaultValue.ToString(CultureInfo.InvariantCulture),
            NumberStepper ns => ns.DefaultValue.ToString(CultureInfo.InvariantCulture),
            Text t => t.Value ?? string.Empty,
            Comment c => c.Value ?? string.Empty,
            CheckBox cb => cb.DefaultValue ? "1" : "0",
            SingleSelect => string.Empty,
            MultiSelect => string.Empty,
            _ => string.Empty
        };
    }

    private static CaseEditRequest MapCaseEditElement(CaseEditElement proto)
    {
        return new CaseEditRequest
        {
            Id = proto.Id,
            Status = proto.Status,
            Fields = proto.Fields.Select(f => new CaseEditRequestField
            {
                FieldType = f.FieldType,
                FieldValues = f.FieldValues.Select(fv => new CaseEditRequestFieldValue
                {
                    FieldId = fv.FieldId,
                    Value = fv.Value,
                    FieldType = fv.FieldType
                }).ToList()
            }).ToList(),
            GroupFields = proto.GroupFields.Select(gf => new CaseEditRequestGroupField
            {
                Id = gf.Id,
                Label = gf.Label,
                Description = gf.Description,
                Color = gf.Color,
                DisplayOrder = gf.DisplayOrder,
                Fields = gf.Fields.Select(f => new CaseEditRequestField
                {
                    FieldType = f.FieldType,
                    FieldValues = f.FieldValues.Select(fv => new CaseEditRequestFieldValue
                    {
                        FieldId = fv.FieldId,
                        Value = fv.Value,
                        FieldType = fv.FieldType
                    }).ToList()
                }).ToList()
            }).ToList(),
            ElementList = proto.ElementList.Select(MapCaseEditElement).ToList()
        };
    }
}
