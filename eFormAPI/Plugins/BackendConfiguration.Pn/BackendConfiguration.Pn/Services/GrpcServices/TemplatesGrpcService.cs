using System.Globalization;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Grpc;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microting.eFormApi.BasePn.Abstractions;

namespace BackendConfiguration.Pn.Services.GrpcServices;

public class TemplatesGrpcService(IEFormCoreService coreHelper)
    : BackendConfigurationTemplatesGrpc.BackendConfigurationTemplatesGrpcBase
{
    public override async Task<GetTemplateResponse> GetTemplate(
        GetTemplateRequest request,
        ServerCallContext context)
    {
        var core = await coreHelper.GetCore().ConfigureAwait(false);
        var sdkDbContext = core.DbContextHelper.GetDbContext();
        var language = await sdkDbContext.Languages
            .FirstOrDefaultAsync(x => x.Id == request.LanguageId)
            .ConfigureAwait(false);

        if (language == null)
        {
            return new GetTemplateResponse { Success = false, Message = "Language not found" };
        }

        var dto = await core.TemplateItemRead(request.TemplateId, language).ConfigureAwait(false);
        if (dto == null)
        {
            return new GetTemplateResponse { Success = false, Message = "Template not found" };
        }

        var item = new TemplateItem
        {
            Id = dto.Id,
            CreatedAt = dto.CreatedAt?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty,
            UpdatedAt = dto.UpdatedAt?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty,
            Label = dto.Label ?? string.Empty,
            Description = dto.Description ?? string.Empty,
            Repeated = dto.Repeated,
            FolderName = dto.FolderName ?? string.Empty,
            WorkflowState = dto.WorkflowState ?? string.Empty,
            HasCases = dto.HasCases,
            DisplayIndex = dto.DisplayIndex ?? 0,
            FolderId = dto.FolderId ?? 0,
            JasperExportEnabled = dto.JasperExportEnabled,
            DocxExportEnabled = dto.DocxExportEnabled,
            ExcelExportEnabled = dto.ExcelExportEnabled,
            ReportH1 = dto.ReportH1 ?? string.Empty,
            ReportH2 = dto.ReportH2 ?? string.Empty,
            ReportH3 = dto.ReportH3 ?? string.Empty,
            ReportH4 = dto.ReportH4 ?? string.Empty,
            ReportH5 = dto.ReportH5 ?? string.Empty,
            IsLocked = dto.IsLocked,
            IsEditable = dto.IsEditable,
            IsHidden = dto.IsHidden,
            IsAchievable = dto.IsAchievable,
            IsDoneAtEditable = dto.IsDoneAtEditable
        };

        if (dto.DeployedSites != null)
        {
            foreach (var site in dto.DeployedSites)
            {
                item.DeployedSites.Add(new SiteNameDto
                {
                    SiteUId = site.SiteUId,
                    SiteName = site.SiteName ?? string.Empty,
                    CreatedAt = site.CreatedAt?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty,
                    UpdatedAt = site.UpdatedAt?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty
                });
            }
        }

        if (dto.Tags != null)
        {
            foreach (var tag in dto.Tags)
            {
                item.Tags.Add(new TemplateTagItem { Id = tag.Key, Name = tag.Value ?? string.Empty });
            }
        }

        item.Field1 = MapFieldDto(dto.Field1);
        item.Field2 = MapFieldDto(dto.Field2);
        item.Field3 = MapFieldDto(dto.Field3);
        item.Field4 = MapFieldDto(dto.Field4);
        item.Field5 = MapFieldDto(dto.Field5);
        item.Field6 = MapFieldDto(dto.Field6);
        item.Field7 = MapFieldDto(dto.Field7);
        item.Field8 = MapFieldDto(dto.Field8);
        item.Field9 = MapFieldDto(dto.Field9);
        item.Field10 = MapFieldDto(dto.Field10);

        return new GetTemplateResponse { Success = true, Message = string.Empty, Template = item };
    }

    private static TemplateFieldDto MapFieldDto(Microting.eForm.Dto.FieldDto src)
    {
        if (src == null) return null;
        return new TemplateFieldDto
        {
            Id = src.Id,
            Label = src.Label ?? string.Empty,
            Description = src.Description ?? string.Empty,
            FieldType = src.FieldType ?? string.Empty,
            FieldTypeId = src.FieldTypeId,
            CheckListId = src.CheckListId,
            ParentName = src.ParentName ?? string.Empty
        };
    }
}
