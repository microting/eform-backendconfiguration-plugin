using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Helpers;
using BackendConfiguration.Pn.Messages;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using eFormCore;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Microting.eForm.Infrastructure.Data.Entities;
using Microting.eForm.Infrastructure.Models;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using Rebus.Handlers;
using KeyValuePair = Microting.eForm.Dto.KeyValuePair;

namespace BackendConfiguration.Pn.Handlers;

public class WorkOrderUpdatedHandler(
    Core sdkCore,
    BackendConfigurationDbContextHelper backendConfigurationDbContextHelper,
    IBackendConfigurationLocalizationService backendConfigurationLocalizationService)
    : IHandleMessages<WorkOrderUpdated>
{
    public async Task Handle(WorkOrderUpdated message)
    {
        await DeployWorkOrderEform(message.PropertyWorkers,
            message.EformId,
            message.PropertyId,
            message.Description,
            message.Status,
            message.WorkorderCaseId,
            message.NewDescription,
            message.DeviceUsersGroupId,
            // message.PdfHash,
            message.AssignedToSite,
            message.PushMessageBody,
            message.PushMessageTitle,
            message.UpdatedByName,
            message.HasImages,
            message.PicturesOfTask,
            message.CreatedByUserId,
            message.UpdatedByUserId)
            .ConfigureAwait(false);
    }

    private async Task DeployWorkOrderEform(
        List<KeyValuePair<int, int>> propertyWorkers,
        int eformId,
        int propertyId,
        string description,
        CaseStatusesEnum status,
        int workorderCaseId,
        string newDescription,
        int? deviceUsersGroupId,
        // string pdfHash,
        Site assignedToSite,
        string pushMessageBody,
        string pushMessageTitle,
        string updatedByName,
        bool hasImages,
        List<KeyValuePair<string, string>> picturesOfTasks,
        int createdByUserId,
        int updatedByUserId)
    {


        var backendConfigurationPnDbContext = backendConfigurationDbContextHelper.GetDbContext();
        var sdkDbContext = sdkCore.DbContextHelper.GetDbContext();

        int? folderId = null;
        var i = 0;
        DateTime startDate = new DateTime(2022, 12, 5);
        var displayOrder = (int)(DateTime.UtcNow - startDate).TotalMinutes;
        var workOrderCase = await backendConfigurationPnDbContext.WorkorderCases.FirstAsync(x => x.Id == workorderCaseId).ConfigureAwait(false);
        var property = await backendConfigurationPnDbContext.Properties.FirstAsync(x => x.Id == propertyId).ConfigureAwait(false);
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
                    priorityText = $"<strong>{backendConfigurationLocalizationService.GetString("Priority")}:</strong> {backendConfigurationLocalizationService.GetString("Urgent")}<br>";
                    break;
                case "2":
                    displayOrder = 200_000_000 + displayOrder;
                    priorityText = $"<strong>{backendConfigurationLocalizationService.GetString("Priority")}:</strong> {backendConfigurationLocalizationService.GetString("High")}<br>";
                    break;
                case "3":
                    displayOrder = 300_000_000 + displayOrder;
                    priorityText = $"<strong>{backendConfigurationLocalizationService.GetString("Priority")}:</strong> {backendConfigurationLocalizationService.GetString("Medium")}<br>";
                    break;
                case "4":
                    displayOrder = 400_000_000 + displayOrder;
                    priorityText = $"<strong>{backendConfigurationLocalizationService.GetString("Priority")}:</strong> {backendConfigurationLocalizationService.GetString("Low")}<br>";
                    break;
            }

            var textStatus = status switch
            {
                CaseStatusesEnum.Ongoing => backendConfigurationLocalizationService.GetString("Ongoing"),
                CaseStatusesEnum.Completed => backendConfigurationLocalizationService.GetString("Completed"),
                CaseStatusesEnum.Awaiting => backendConfigurationLocalizationService.GetString("Awaiting"),
                CaseStatusesEnum.Ordered => backendConfigurationLocalizationService.GetString("Ordered"),
                _ => ""
            };

            var assignedTo = site.Name == assignedToSite.Name ? "" : $"<strong>{backendConfigurationLocalizationService.GetString("AssignedTo")}:</strong> {assignedToSite.Name}<br>";

            var areaName = !string.IsNullOrEmpty(workOrderCase.SelectedAreaName)
                ? $"<strong>{backendConfigurationLocalizationService.GetString("Area")}:</strong> {workOrderCase.SelectedAreaName}<br>"
                : "";

            var outerDescription = $"<strong>{backendConfigurationLocalizationService.GetString("Location")}:</strong> {property.Name}<br>" +
                                   areaName +
                                   $"<strong>{backendConfigurationLocalizationService.GetString("Description")}:</strong> {newDescription}<br>" +
                                   priorityText +
                                   $"<strong>{backendConfigurationLocalizationService.GetString("CreatedBy")}:</strong> {workOrderCase.CreatedByName}<br>" +
                                   (!string.IsNullOrEmpty(workOrderCase.CreatedByText)
                                       ? $"<strong>{backendConfigurationLocalizationService.GetString("CreatedBy")}:</strong> {workOrderCase.CreatedByText}<br>"
                                       : "") +
                                   assignedTo +
                                   $"<strong>{backendConfigurationLocalizationService.GetString("Status")}:</strong> {textStatus}<br><br>";
            var mainElement = await sdkCore.ReadeForm(eformId, siteLanguage);
            mainElement.Label = " ";
            mainElement.ElementList[0].QuickSyncEnabled = true;
            mainElement.EnableQuickSync = true;
            mainElement.ElementList[0].Label = " ";
            mainElement.ElementList[0].Description.InderValue = outerDescription.Replace("\n", "<br>");
            mainElement.DisplayOrder = displayOrder; // Lowest value is the top of the list
            // if (status == CaseStatusesEnum.Completed || site.Name == siteName)
            // {
            //     DateTime startDate = new DateTime(2020, 1, 1);
            //     mainElement.DisplayOrder = (int)(startDate - DateTime.UtcNow).TotalSeconds;
            // }
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
                    var storageResult = sdkCore.GetFileFromS3Storage(picture.Key).GetAwaiter().GetResult();

                    await sdkCore.PngUpload(storageResult.ResponseStream, picture.Value, picture.Key);
                    showPicture.Value = picture.Value;
                    ((DataElement) mainElement.ElementList[0]).DataItemList.Add(showPicture);

                    j++;
                }
            }
            // }
            int caseId = 0;
            if (status != CaseStatusesEnum.Completed)
            {
                caseId = (int)await sdkCore.CaseCreate(mainElement, "", (int)site.MicrotingUid, folderId);
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
}