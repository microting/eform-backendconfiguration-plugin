using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Models.TaskManagement;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using eFormCore;
using ImageMagick;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Microting.eForm.Infrastructure.Data.Entities;
using Microting.eForm.Infrastructure.Models;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using KeyValuePair = Microting.eForm.Dto.KeyValuePair;

namespace BackendConfiguration.Pn.Infrastructure.Helpers;

public static class BackendConfigurationTaskManagementHelper
{
    public static async Task<OperationResult> UpdateTask(WorkOrderCaseUpdateModel updateModel,
        IBackendConfigurationLocalizationService localizationService,
        Core core,
        IUserService userService,
        BackendConfigurationPnDbContext backendConfigurationPnDbContext,
        bool useGetCurrentUserFullName)
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

        var picturesOfTasksList = new List<KeyValuePair<string, string>>();
        var hasImages = false;
        var parentCaseImages = await backendConfigurationPnDbContext.WorkorderCaseImages.Where(x => x.WorkorderCaseId == workOrderCase.ParentWorkorderCaseId).ToListAsync();

        foreach (var workorderCaseImage in parentCaseImages)
        {
            var uploadedData = await sdkDbContext.UploadedDatas.FirstAsync(x => x.Id == workorderCaseImage.UploadedDataId);
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

        // deploy eform synchronously so DB is up-to-date before returning
        var propertyWorkerKvpList = new List<KeyValuePair<int, int>>();
        foreach (var propertyWorker in propertyWorkers)
        {
            propertyWorkerKvpList.Add(new KeyValuePair<int, int>(propertyWorker.Id, propertyWorker.WorkerId));
        }

        await DeployWorkOrderEform(
            propertyWorkerKvpList,
            eformIdForOngoingTasks,
            workOrderCase,
            property,
            label,
            updateModel.CaseStatusEnum,
            updateModel.Description,
            int.Parse(deviceUsersGroupUid),
            site,
            pushMessageBody,
            pushMessageTitle,
            updatedByName,
            hasImages,
            picturesOfTasksList,
            userService.UserId,
            userService.UserId,
            core,
            backendConfigurationPnDbContext,
            localizationService).ConfigureAwait(false);

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
                        .Padding(0, QuestPDF.Infrastructure.Unit.Centimetre)
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

    private static async Task DeployWorkOrderEform(
        List<KeyValuePair<int, int>> propertyWorkers,
        int eformId,
        WorkorderCase workOrderCase,
        Property property,
        string description,
        CaseStatusesEnum status,
        string newDescription,
        int? deviceUsersGroupId,
        Site assignedToSite,
        string pushMessageBody,
        string pushMessageTitle,
        string updatedByName,
        bool hasImages,
        List<KeyValuePair<string, string>> picturesOfTasks,
        int createdByUserId,
        int updatedByUserId,
        Core core,
        BackendConfigurationPnDbContext backendConfigurationPnDbContext,
        IBackendConfigurationLocalizationService localizationService)
    {
        var sdkDbContext = core.DbContextHelper.GetDbContext();

        int? folderId = null;
        var i = 0;
        DateTime startDate = new DateTime(2022, 12, 5);
        var displayOrder = (int)(DateTime.UtcNow - startDate).TotalMinutes;

        foreach (var propertyWorker in propertyWorkers)
        {
            var priorityText = "";

            var site = await sdkDbContext.Sites.SingleAsync(x => x.Id == propertyWorker.Value).ConfigureAwait(false);
            var unit = await sdkDbContext.Units.FirstAsync(x => x.SiteId == site.Id);
            var siteLanguage = await sdkDbContext.Languages.SingleAsync(x => x.Id == site.LanguageId).ConfigureAwait(false);
            Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo(siteLanguage.LanguageCode);
            switch (workOrderCase.Priority)
            {
                case "1":
                    displayOrder = 100_000_000 + displayOrder;
                    priorityText = $"<strong>{localizationService.GetString("Priority")}:</strong> {localizationService.GetString("Urgent")}<br>";
                    break;
                case "2":
                    displayOrder = 200_000_000 + displayOrder;
                    priorityText = $"<strong>{localizationService.GetString("Priority")}:</strong> {localizationService.GetString("High")}<br>";
                    break;
                case "3":
                    displayOrder = 300_000_000 + displayOrder;
                    priorityText = $"<strong>{localizationService.GetString("Priority")}:</strong> {localizationService.GetString("Medium")}<br>";
                    break;
                case "4":
                    displayOrder = 400_000_000 + displayOrder;
                    priorityText = $"<strong>{localizationService.GetString("Priority")}:</strong> {localizationService.GetString("Low")}<br>";
                    break;
            }

            var textStatus = status switch
            {
                CaseStatusesEnum.Ongoing => localizationService.GetString("Ongoing"),
                CaseStatusesEnum.Completed => localizationService.GetString("Completed"),
                CaseStatusesEnum.Awaiting => localizationService.GetString("Awaiting"),
                CaseStatusesEnum.Ordered => localizationService.GetString("Ordered"),
                _ => ""
            };

            var assignedTo = site.Name == assignedToSite.Name ? "" : $"<strong>{localizationService.GetString("AssignedTo")}:</strong> {assignedToSite.Name}<br>";

            var areaName = !string.IsNullOrEmpty(workOrderCase.SelectedAreaName)
                ? $"<strong>{localizationService.GetString("Area")}:</strong> {workOrderCase.SelectedAreaName}<br>"
                : "";

            var outerDescription = $"<strong>{localizationService.GetString("Location")}:</strong> {property.Name}<br>" +
                                   areaName +
                                   $"<strong>{localizationService.GetString("Description")}:</strong> {newDescription}<br>" +
                                   priorityText +
                                   $"<strong>{localizationService.GetString("CreatedBy")}:</strong> {workOrderCase.CreatedByName}<br>" +
                                   (!string.IsNullOrEmpty(workOrderCase.CreatedByText)
                                       ? $"<strong>{localizationService.GetString("CreatedBy")}:</strong> {workOrderCase.CreatedByText}<br>"
                                       : "") +
                                   assignedTo +
                                   $"<strong>{localizationService.GetString("Status")}:</strong> {textStatus}<br><br>";
            var mainElement = await core.ReadeForm(eformId, siteLanguage);
            mainElement.Label = " ";
            mainElement.ElementList[0].QuickSyncEnabled = true;
            mainElement.EnableQuickSync = true;
            mainElement.ElementList[0].Label = " ";
            mainElement.ElementList[0].Description.InderValue = outerDescription.Replace("\n", "<br>");
            mainElement.DisplayOrder = displayOrder; // Lowest value is the top of the list

            if (site.Name == assignedToSite.Name)
            {
                mainElement.CheckListFolderName = sdkDbContext.Folders.First(x => x.Id == (workOrderCase.Priority != "1" ? property.FolderIdForOngoingTasks : property.FolderIdForTasks))
                    .MicrotingUid.ToString();
                folderId = property.FolderIdForOngoingTasks;
                mainElement.PushMessageTitle = pushMessageTitle;
                mainElement.PushMessageBody = pushMessageBody;
                mainElement.BadgeCountEnabled = true;
            }
            else
            {
                folderId = property.FolderIdForCompletedTasks;
                mainElement.CheckListFolderName = sdkDbContext.Folders.First(x => x.Id == property.FolderIdForCompletedTasks)
                    .MicrotingUid.ToString();
            }
            // TODO uncomment when new app has been released.
            ((DataElement)mainElement.ElementList[0]).DataItemList[0].Description.InderValue = description.Replace("\n", "<br>");
            ((DataElement)mainElement.ElementList[0]).DataItemList[0].Label = " ";
            ((DataElement)mainElement.ElementList[0]).DataItemList[0].Color = Constants.FieldColors.Yellow;
            // ((ShowPdf) ((DataElement) mainElement.ElementList[0]).DataItemList[1]).Value = pdfHash;

            List<Microting.eForm.Dto.KeyValuePair> kvpList = ((SingleSelect) ((DataElement) mainElement.ElementList[0]).DataItemList[4]).KeyValuePairList;
            var newKvpList = new List<Microting.eForm.Dto.KeyValuePair>();
            foreach (var keyValuePair in kvpList)
            {
                if (keyValuePair.Key == workOrderCase.Priority)
                {
                    keyValuePair.Selected = true;
                }
                newKvpList.Add(keyValuePair);
            }
            ((SingleSelect) ((DataElement) mainElement.ElementList[0]).DataItemList[4]).KeyValuePairList = newKvpList;

            if (deviceUsersGroupId != null)
            {
                ((EntitySelect)((DataElement)mainElement.ElementList[0]).DataItemList[5]).Source = (int)deviceUsersGroupId;
                ((EntitySelect)((DataElement)mainElement.ElementList[0]).DataItemList[5]).Mandatory = true;
                ((Comment)((DataElement)mainElement.ElementList[0]).DataItemList[3]).Value = newDescription;
                ((SingleSelect)((DataElement)mainElement.ElementList[0]).DataItemList[6]).Mandatory = true;
                mainElement.EndDate = DateTime.Now.AddYears(10).ToUniversalTime();
                mainElement.Repeated = 1;
            }
            else
            {
                mainElement.EndDate = DateTime.Now.AddDays(30).ToUniversalTime();
                mainElement.ElementList[0].DoneButtonEnabled = false;
                mainElement.Repeated = 1;
            }

            mainElement.StartDate = DateTime.Now.ToUniversalTime();
            ((DataElement) mainElement.ElementList[0]).DataItemList.RemoveAt(1);
            // if (hasImages == false)
            // {
            //     ((DataElement) mainElement.ElementList[0]).DataItemList.RemoveAt(1);
            // }
            // unit.eFormVersion ??= "1.0.0";
            // if (int.Parse(unit.eFormVersion.Replace(".","")) > 3212)
            // {
            if (hasImages)
            {
                // add a new show picture element for each picture in the picturesOfTasks list
                int j = 0;
                foreach (var picture in picturesOfTasks)
                {
                    var showPicture = new ShowPicture(j, false, false, "", "", "", 0, false, "");
                    var storageResult = core.GetFileFromS3Storage(picture.Key).GetAwaiter().GetResult();

                    await core.PngUpload(storageResult.ResponseStream, picture.Value, picture.Key);
                    showPicture.Value = picture.Value;
                    ((DataElement) mainElement.ElementList[0]).DataItemList.Add(showPicture);

                    j++;
                }
            }
            // }
            int caseId = 0;
            if (status != CaseStatusesEnum.Completed)
            {
                caseId = (int)await core.CaseCreate(mainElement, "", (int)site.MicrotingUid, folderId);
            }

            var createdBySite =
                await sdkDbContext.Sites.FirstOrDefaultAsync(x => x.Id == workOrderCase.CreatedBySdkSiteId);
            if (createdBySite != null)
            {
                workOrderCase.CreatedByName = createdBySite.Name;
            }
            await new WorkorderCase
            {
                CaseId = caseId,
                PropertyWorkerId = propertyWorker.Key,
                CaseStatusesEnum = status,
                ParentWorkorderCaseId = workOrderCase.Id,
                GroupId = workOrderCase.GroupId,
                SelectedAreaName = workOrderCase.SelectedAreaName,
                CreatedByName = workOrderCase.CreatedByName,
                CreatedByText = workOrderCase.CreatedByText,
                Description = newDescription,
                CaseInitiated = workOrderCase.CaseInitiated,
                LastAssignedToName = assignedToSite.Name,
                AssignedToSdkSiteId = assignedToSite.Id,
                LastUpdatedByName = updatedByName,
                LeadingCase = i == 0,
                Priority = workOrderCase.Priority,
                CreatedByUserId = createdByUserId,
                UpdatedByUserId = updatedByUserId
            }.Create(backendConfigurationPnDbContext);
            i++;
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

    public static async Task DeployWorkOrderEformForCreate(
        List<System.Collections.Generic.KeyValuePair<int, int>> propertyWorkers,
        int eformId,
        WorkorderCase workOrderCase,
        int folderIdForOngoingTasks,
        int folderIdForTasks,
        int folderIdForCompletedTasks,
        string description,
        CaseStatusesEnum status,
        string newDescription,
        int? deviceUsersGroupId,
        Site assignedToSite,
        string pushMessageBody,
        string pushMessageTitle,
        string areaName,
        string propertyName,
        bool hasImages,
        List<System.Collections.Generic.KeyValuePair<string, string>> picturesOfTasks,
        Core core,
        BackendConfigurationPnDbContext backendConfigurationPnDbContext,
        IBackendConfigurationLocalizationService localizationService)
    {
        var sdkDbContext = core.DbContextHelper.GetDbContext();
        DateTime startDate = new DateTime(2022, 12, 5);
        var displayOrder = (int)(DateTime.UtcNow - startDate).TotalSeconds;

        foreach (var propertyWorker in propertyWorkers)
        {
            var site = await sdkDbContext.Sites.SingleAsync(x => x.Id == propertyWorker.Value).ConfigureAwait(false);
            var unit = await sdkDbContext.Units.FirstAsync(x => x.SiteId == site.Id);
            var siteLanguage = await sdkDbContext.Languages.SingleAsync(x => x.Id == site.LanguageId).ConfigureAwait(false);
            Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo(siteLanguage.LanguageCode);
            var priorityText = "";

            switch (workOrderCase.Priority)
            {
                case "1":
                    displayOrder = 100_000_000 + displayOrder;
                    priorityText = $"<strong>{localizationService.GetString("Priority")}:</strong> {localizationService.GetString("Urgent")}<br>";
                    break;
                case "2":
                    displayOrder = 200_000_000 + displayOrder;
                    priorityText = $"<strong>{localizationService.GetString("Priority")}:</strong> {localizationService.GetString("High")}<br>";
                    break;
                case "3":
                    displayOrder = 300_000_000 + displayOrder;
                    priorityText = $"<strong>{localizationService.GetString("Priority")}:</strong> {localizationService.GetString("Medium")}<br>";
                    break;
                case "4":
                    displayOrder = 400_000_000 + displayOrder;
                    priorityText = $"<strong>{localizationService.GetString("Priority")}:</strong> {localizationService.GetString("Low")}<br>";
                    break;
            }

            var textStatus = workOrderCase.CaseStatusesEnum switch
            {
                CaseStatusesEnum.Ongoing => localizationService.GetString("Ongoing"),
                CaseStatusesEnum.Completed => localizationService.GetString("Completed"),
                CaseStatusesEnum.Awaiting => localizationService.GetString("Awaiting"),
                CaseStatusesEnum.Ordered => localizationService.GetString("Ordered"),
                _ => ""
            };

            var assignedTo = site.Name == assignedToSite.Name ? "" : $"<strong>{localizationService.GetString("AssignedTo")}:</strong> {assignedToSite.Name}<br>";

            var selectedAreaName = !string.IsNullOrEmpty(workOrderCase.SelectedAreaName)
                ? $"<strong>{localizationService.GetString("Area")}:</strong> {workOrderCase.SelectedAreaName}<br>"
                : "";

            var outerDescription = $"<strong>{localizationService.GetString("Location")}:</strong> {propertyName}<br>" +
                                   selectedAreaName +
                                   $"<strong>{localizationService.GetString("Description")}:</strong> {newDescription}<br>" +
                                   priorityText +
                                   $"<strong>{localizationService.GetString("CreatedBy")}:</strong> {workOrderCase.CreatedByName}<br>" +
                                   (!string.IsNullOrEmpty(workOrderCase.CreatedByText)
                                       ? $"<strong>{localizationService.GetString("CreatedBy")}:</strong> {workOrderCase.CreatedByText}<br>"
                                       : "") +
                                   assignedTo +
                                   $"<strong>{localizationService.GetString("Status")}:</strong> {textStatus}<br><br>";

            var mainElement = await core.ReadeForm(eformId, siteLanguage).ConfigureAwait(false);
            mainElement.Label = " ";
            mainElement.ElementList[0].QuickSyncEnabled = true;
            mainElement.EnableQuickSync = true;
            mainElement.ElementList[0].Label = " ";
            mainElement.ElementList[0].Description.InderValue = outerDescription.Replace("\n", "<br>");
            mainElement.DisplayOrder = displayOrder;
            int folderId;
            if (site.Name == assignedToSite.Name)
            {
                mainElement.CheckListFolderName = sdkDbContext.Folders.First(x => x.Id == (workOrderCase.Priority != "1" ? folderIdForOngoingTasks : folderIdForTasks))
                    .MicrotingUid.ToString();
                folderId = folderIdForOngoingTasks;
                mainElement.PushMessageTitle = pushMessageTitle;
                mainElement.PushMessageBody = pushMessageBody;
                mainElement.BadgeCountEnabled = true;
            }
            else
            {
                folderId = folderIdForCompletedTasks;
                mainElement.CheckListFolderName = sdkDbContext.Folders.First(x => x.Id == folderIdForCompletedTasks)
                    .MicrotingUid.ToString();
            }
            ((DataElement)mainElement.ElementList[0]).DataItemList[0].Description.InderValue = description.Replace("\r\n", "<br>").Replace("\n", "<br>");
            ((DataElement)mainElement.ElementList[0]).DataItemList[0].Label = " ";
            ((DataElement)mainElement.ElementList[0]).DataItemList[0].Color = Constants.FieldColors.Yellow;
            List<KeyValuePair> kvpList = ((SingleSelect) ((DataElement) mainElement.ElementList[0]).DataItemList[4]).KeyValuePairList;
            var newKvpList = new List<KeyValuePair>();
            foreach (var keyValuePair in kvpList)
            {
                if (keyValuePair.Key == workOrderCase.Priority)
                {
                    keyValuePair.Selected = true;
                }
                newKvpList.Add(keyValuePair);
            }
            ((SingleSelect) ((DataElement) mainElement.ElementList[0]).DataItemList[4]).KeyValuePairList = newKvpList;

            if (deviceUsersGroupId != null)
            {
                ((EntitySelect)((DataElement)mainElement.ElementList[0]).DataItemList[5]).Source = (int)deviceUsersGroupId;
                ((EntitySelect)((DataElement)mainElement.ElementList[0]).DataItemList[5]).Mandatory = true;
                ((Comment)((DataElement)mainElement.ElementList[0]).DataItemList[3]).Value = newDescription;
                ((SingleSelect)((DataElement)mainElement.ElementList[0]).DataItemList[6]).Mandatory = true;
                mainElement.EndDate = DateTime.Now.AddYears(10).ToUniversalTime();
                mainElement.Repeated = 1;
            }
            else
            {
                mainElement.EndDate = DateTime.Now.AddDays(30).ToUniversalTime();
                mainElement.ElementList[0].DoneButtonEnabled = false;
                mainElement.Repeated = 1;
            }

            mainElement.StartDate = DateTime.Now.ToUniversalTime();
            ((DataElement) mainElement.ElementList[0]).DataItemList.RemoveAt(1);
            if (hasImages)
            {
                int j = 0;
                foreach (var picture in picturesOfTasks)
                {
                    var showPicture = new ShowPicture(j, false, false, "", "", "", 0, false, "");
                    var storageResult = core.GetFileFromS3Storage(picture.Key).GetAwaiter().GetResult();

                    await core.PngUpload(storageResult.ResponseStream, picture.Value, picture.Key);
                    showPicture.Value = picture.Value;
                    ((DataElement) mainElement.ElementList[0]).DataItemList.Add(showPicture);
                    j++;
                }
            }
            var caseId = await core.CaseCreate(mainElement, "", (int)site.MicrotingUid!, folderId).ConfigureAwait(false);
            var newWorkOrderCase = new WorkorderCase
            {
                CaseId = (int)caseId!,
                PropertyWorkerId = propertyWorker.Key,
                CaseStatusesEnum = status,
                ParentWorkorderCaseId = workOrderCase.Id,
                GroupId = workOrderCase.GroupId,
                SelectedAreaName = workOrderCase.SelectedAreaName,
                CreatedByName = workOrderCase.CreatedByName,
                CreatedByText = workOrderCase.CreatedByText,
                Description = newDescription,
                CaseInitiated = DateTime.UtcNow,
                LastAssignedToName = site.Name,
                AssignedToSdkSiteId = site.Id,
                LastUpdatedByName = "",
                LeadingCase = false,
                Priority = workOrderCase.Priority,
                CreatedByUserId = workOrderCase.CreatedByUserId,
                UpdatedByUserId = workOrderCase.UpdatedByUserId
            };
            await newWorkOrderCase.Create(backendConfigurationPnDbContext).ConfigureAwait(false);
        }
    }
}
