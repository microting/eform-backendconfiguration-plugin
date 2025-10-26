using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Models.TaskManagement;
using BackendConfiguration.Pn.Messages;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using eFormCore;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using Rebus.Bus;

namespace BackendConfiguration.Pn.Infrastructure.Helpers;

public static class BackendConfigurationTaskManagementHelper
{
    public static async Task<OperationResult> UpdateTask(WorkOrderCaseUpdateModel updateModel,
        IBackendConfigurationLocalizationService localizationService,
        Core core,
        IUserService userService,
        BackendConfigurationPnDbContext backendConfigurationPnDbContext,
        IBus bus, bool useGetCurrentUserFullName)
    {
        // var core = await coreHelper.GetCore().ConfigureAwait(false);
        var sdkDbContext = core.DbContextHelper.GetDbContext();
        var workOrderCase = await backendConfigurationPnDbContext.WorkorderCases
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .Include(x => x.PropertyWorker)
            .Include(x => x.PropertyWorker.Property)
            .Where(x => x.Id == updateModel.Id).FirstOrDefaultAsync().ConfigureAwait(false);
        if (workOrderCase == null)
        {
            return new OperationDataResult<WorkOrderCaseReadModel>(false,
                localizationService.GetString("TaskNotFound"));
        }
        workOrderCase.Priority = updateModel.Priority.ToString();

        var createdBySite =
            await sdkDbContext.Sites.FirstOrDefaultAsync(x => x.Id == workOrderCase.CreatedBySdkSiteId);
        if (createdBySite != null)
        {
            workOrderCase.CreatedByName = createdBySite.Name;
        }

        var site = await sdkDbContext.Sites.FirstAsync(x => x.Id == updateModel.AssignedSiteId).ConfigureAwait(false);
        var updatedByName = await userService.GetCurrentUserFullName().ConfigureAwait(false);
        if (!useGetCurrentUserFullName)
        {
            updatedByName = workOrderCase.LastUpdatedByName;
        }

        var picturesOfTasks = new List<string>();
        var picturesOfTasksList = new List<KeyValuePair<string, string>>();
        var hasImages = false;
        var parentCaseImages = await backendConfigurationPnDbContext.WorkorderCaseImages.Where(x => x.WorkorderCaseId == workOrderCase.ParentWorkorderCaseId).ToListAsync();

        foreach (var workorderCaseImage in parentCaseImages)
        {
            var uploadedData = await sdkDbContext.UploadedDatas.FirstAsync(x => x.Id == workorderCaseImage.UploadedDataId);
            picturesOfTasks.Add($"{uploadedData.Id}_700_{uploadedData.Checksum}{uploadedData.Extension}");
            picturesOfTasksList.Add(new KeyValuePair<string, string>($"{uploadedData.Id}_700_{uploadedData.Checksum}{uploadedData.Extension}", uploadedData.Checksum));
            var workOrderCaseImage = new WorkorderCaseImage
            {
                WorkorderCaseId = workOrderCase.Id,
                UploadedDataId = uploadedData.Id
            };
            await workOrderCaseImage.Create(backendConfigurationPnDbContext);
            hasImages = true;
        }

        var property = workOrderCase.PropertyWorker.Property;
        // var hash = await GeneratePdf(picturesOfTasks, site.Id, core);

        var label = $"<strong>{localizationService.GetString("AssignedTo")}:</strong> {site.Name}<br>";

        var pushMessageTitle = !string.IsNullOrEmpty(workOrderCase.SelectedAreaName) ? $"{property.Name}; {workOrderCase.SelectedAreaName}" : $"{property.Name}";
        var pushMessageBody = $"{updateModel.Description}";
        var deviceUsersGroupUid = await sdkDbContext.EntityGroups
            .Where(x => x.Id == property.EntitySelectListDeviceUsers)
            .Select(x => x.MicrotingUid)
            .FirstAsync();
            var priorityText = "";

        var propertyWorkers = await backendConfigurationPnDbContext.PropertyWorkers
            .Where(x => x.PropertyId == property.Id)
            .Where(x => x.WorkflowState != Constants.WorkflowStates.Removed)
            .Where(x => x.TaskManagementEnabled == true || x.TaskManagementEnabled == null)
            .ToListAsync();

        var eformIdForOngoingTasks = await sdkDbContext.CheckLists
            .Where(x => x.OriginalId == "142664new2")
            .Select(x => x.Id)
            .FirstOrDefaultAsync();

        var textStatus = "";
        if (updateModel.CaseStatusEnum == CaseStatusesEnum.NewTask)
        {
            workOrderCase.CaseStatusesEnum = CaseStatusesEnum.Ongoing;
            updateModel.CaseStatusEnum = CaseStatusesEnum.Ongoing;
        }

        textStatus = workOrderCase.CaseStatusesEnum switch
        {
            CaseStatusesEnum.Ongoing => localizationService.GetString("Ongoing"),
            CaseStatusesEnum.Completed => localizationService.GetString("Completed"),
            CaseStatusesEnum.Ordered => localizationService.GetString("Ordered"),
            CaseStatusesEnum.Awaiting => localizationService.GetString("Awaiting"),
            _ => textStatus
        };

        priorityText = updateModel.Priority switch
        {
            1 =>
                $"<strong>{localizationService.GetString("Priority")}:</strong> {localizationService.GetString("Urgent")}<br>",
            2 =>
                $"<strong>{localizationService.GetString("Priority")}:</strong> {localizationService.GetString("High")}<br>",
            3 =>
                $"<strong>{localizationService.GetString("Priority")}:</strong> {localizationService.GetString("Medium")}<br>",
            4 =>
                $"<strong>{localizationService.GetString("Priority")}:</strong> {localizationService.GetString("Low")}<br>",
            _ => priorityText
        };
        label += $"<strong>{localizationService.GetString("Location")}:</strong> {property.Name}<br>" +
                 (!string.IsNullOrEmpty(workOrderCase.SelectedAreaName)
                     ? $"<strong>{localizationService.GetString("Area")}:</strong> {workOrderCase.SelectedAreaName}<br>"
                     : "") +
                 $"<strong>{localizationService.GetString("Description")}:</strong> {updateModel.Description}<br>" +
                 priorityText +
                 $"<strong>{localizationService.GetString("CreatedBy")}:</strong> {workOrderCase.CreatedByName}<br>" +
                 (!string.IsNullOrEmpty(workOrderCase.CreatedByText)
                     ? $"<strong>{localizationService.GetString("CreatedBy")}:</strong> {workOrderCase.CreatedByText}<br>"
                     : "") +
                 $"<strong>{localizationService.GetString("CreatedDate")}:</strong> {workOrderCase.CaseInitiated: dd.MM.yyyy}<br><br>" +
                 $"<strong>{localizationService.GetString("LastUpdatedBy")}:</strong> {updatedByName}<br>" +
                 $"<strong>{localizationService.GetString("LastUpdatedDate")}:</strong> {DateTime.UtcNow: dd.MM.yyyy}<br><br>" +
                 $"<strong>{localizationService.GetString("Status")}:</strong> {textStatus}<br><br>";
        // retract eform
        await RetractEform(workOrderCase, core, backendConfigurationPnDbContext);
        // deploy eform to ongoing status

        //_bus.Send(new WorkOrderUpdated(propertyWorkers, eformIdForOngoingTasks, property))

        //await DeployWorkOrderEform(propertyWorkers, eformIdForOngoingTasks, property, label,  workOrderCase.CaseStatusesEnum, workOrderCase, updateModel.Description, int.Parse(deviceUsersGroupUid), hash, site.Name, pushMessageBody, pushMessageTitle, updatedByName);

        var propertyWorkerKvpList = new List<KeyValuePair<int, int>>();

        foreach (var propertyWorker in propertyWorkers)
        {
            var kvp = new KeyValuePair<int, int>(propertyWorker.Id, propertyWorker.WorkerId);
            propertyWorkerKvpList.Add(kvp);
        }

        await bus.SendLocal(new WorkOrderUpdated(propertyWorkerKvpList, eformIdForOngoingTasks, property.Id, label,
            updateModel.CaseStatusEnum, workOrderCase.Id, updateModel.Description, int.Parse(deviceUsersGroupUid), site, pushMessageBody, pushMessageTitle, updatedByName, hasImages, picturesOfTasksList, userService.UserId, userService.UserId)).ConfigureAwait(false);

        return new OperationResult(true, localizationService.GetString("TaskUpdatedSuccessful"));
    }

    private static async Task<string> GeneratePdf(List<string> picturesOfTasks, int sitId, Core core)
    {
        try
        {

            // var sdkCore = await coreHelper.GetCore().ConfigureAwait(false);
            picturesOfTasks.Reverse();
            var downloadPath = Path.Combine(Path.GetTempPath(), "reports", "results");
            Directory.CreateDirectory(downloadPath);
            var timeStamp = DateTime.UtcNow.ToString("yyyyMMdd") + "_" + DateTime.UtcNow.ToString("hhmmss");
            var tempPdfFileName = $"{timeStamp}{sitId}_temp.pdf";
            var tempPdfFilePath = Path.Combine(downloadPath, tempPdfFileName);
            var document = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Content()
                        .Padding(0, Unit.Centimetre)
                        .Column(x =>
                        {
                            x.Spacing(20);
                            // loop over all images and add them to the document
                            foreach (var imageName in picturesOfTasks)
                            {
                                var storageResult = core.GetFileFromS3Storage(imageName).GetAwaiter().GetResult();
                                x.Item().Image(storageResult.ResponseStream)
                                    .FitArea();
                            }
                        });
                });
            }).GeneratePdf();

            await using var fileStream = new FileStream(tempPdfFilePath, FileMode.Create, FileAccess.Write);
            // save the byte[] to a file.
            await fileStream.WriteAsync(document, 0, document.Length);
            await fileStream.FlushAsync();

            // Upload PDF
            // string pdfFileName = null;
            string hash = await core.PdfUpload(tempPdfFilePath);
            if (hash != null)
            {
                //rename local file
                FileInfo fileInfo = new FileInfo(tempPdfFilePath);
                fileInfo.CopyTo(downloadPath + "/" + hash + ".pdf", true);
                fileInfo.Delete();
                await core.PutFileToStorageSystem(Path.Combine(downloadPath, $"{hash}.pdf"), $"{hash}.pdf");

                // TODO Remove from file storage?
            }

            return hash;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }

    private static async Task RetractEform(WorkorderCase workOrderCase, Core core, BackendConfigurationPnDbContext _backendConfigurationPnDbContext)
    {
        // var core = await _coreHelper.GetCore().ConfigureAwait(false);
        await using var sdkDbContext = core.DbContextHelper.GetDbContext();

        var workOrdersToRetract = await _backendConfigurationPnDbContext.WorkorderCases
            .Where(x => x.ParentWorkorderCaseId == workOrderCase.Id).ToListAsync();

        foreach (var theCase in workOrdersToRetract)
        {
            try {
                await core.CaseDelete(theCase.CaseId);
            } catch (Exception e) {
                Console.WriteLine(e);
                Console.WriteLine($"faild to delete case {theCase.CaseId}");
            }
            await theCase.Delete(_backendConfigurationPnDbContext);
        }

        if (workOrderCase.ParentWorkorderCaseId != null)
        {
            var siblings = await _backendConfigurationPnDbContext.WorkorderCases
                .Where(x => x.ParentWorkorderCaseId == workOrderCase.ParentWorkorderCaseId).ToListAsync();

            foreach (var sibling in siblings)
            {
                if (sibling.CaseId != 0)
                {
                    try
                    {
                        await core.CaseDelete(sibling.CaseId);
                    } catch (Exception e) {
                        Console.WriteLine(e);
                        Console.WriteLine($"faild to delete case {sibling.CaseId}");
                    }
                }
                await sibling.Delete(_backendConfigurationPnDbContext);
            }
        }

        if (workOrderCase.CaseId != 0)
        {
            try
            {
                await core.CaseDelete(workOrderCase.CaseId);
            } catch (Exception e) {
                Console.WriteLine(e);
                Console.WriteLine($"faild to delete case {workOrderCase.CaseId}");
            }
        }

        await workOrderCase.Delete(_backendConfigurationPnDbContext);
    }
}