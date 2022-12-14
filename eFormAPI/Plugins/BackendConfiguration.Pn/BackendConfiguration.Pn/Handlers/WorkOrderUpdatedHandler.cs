using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Helpers;
using BackendConfiguration.Pn.Messages;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using eFormCore;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Microting.eForm.Infrastructure.Models;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using Rebus.Handlers;
using KeyValuePair = Microting.eForm.Dto.KeyValuePair;

namespace BackendConfiguration.Pn.Handlers;

public class WorkOrderUpdatedHandler : IHandleMessages<WorkOrderUpdated>
{
    private readonly Core _sdkCore;
    private readonly BackendConfigurationDbContextHelper _backendConfigurationDbContextHelper;
    private readonly IBackendConfigurationLocalizationService _backendConfigurationLocalizationService;

    public WorkOrderUpdatedHandler(Core sdkCore, BackendConfigurationDbContextHelper backendConfigurationDbContextHelper, IBackendConfigurationLocalizationService backendConfigurationLocalizationService)
    {
        _sdkCore = sdkCore;
        _backendConfigurationDbContextHelper = backendConfigurationDbContextHelper;
        _backendConfigurationLocalizationService = backendConfigurationLocalizationService;
    }

    public async Task Handle(WorkOrderUpdated message)
    {
        await DeployWorkOrderEform(message.PropertyWorkers, message.EformId, message.PropertyId, message.Description,  message.Status, message.WorkorderCaseId, message.NewDescription, message.DeviceUsersGroupId, message.PdfHash, message.SiteName, message.PushMessageBody, message.PushMessageTitle, message.UpdatedByName);
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
        string pdfHash,
        string siteName,
        string pushMessageBody,
        string pushMessageTitle,
        string updatedByName)
    {


        var backendConfigurationPnDbContext = _backendConfigurationDbContextHelper.GetDbContext();
        var sdkDbContext = _sdkCore.DbContextHelper.GetDbContext();
        await using var _ = sdkDbContext.ConfigureAwait(false);

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
            switch (workOrderCase.Priority)
            {
                case "1":
                    displayOrder = 100_000_000 + displayOrder;
                    priorityText = $"<strong>{_backendConfigurationLocalizationService.GetString("Priority")}:</strong> {_backendConfigurationLocalizationService.GetString("Urgent")}<br>";
                    break;
                case "2":
                    displayOrder = 200_000_000 + displayOrder;
                    priorityText = $"<strong>{_backendConfigurationLocalizationService.GetString("Priority")}:</strong> {_backendConfigurationLocalizationService.GetString("High")}<br>";
                    break;
                case "3":
                    displayOrder = 300_000_000 + displayOrder;
                    priorityText = $"<strong>{_backendConfigurationLocalizationService.GetString("Priority")}:</strong> {_backendConfigurationLocalizationService.GetString("Medium")}<br>";
                    break;
                case "4":
                    displayOrder = 400_000_000 + displayOrder;
                    priorityText = $"<strong>{_backendConfigurationLocalizationService.GetString("Priority")}:</strong> {_backendConfigurationLocalizationService.GetString("Low")}<br>";
                    break;
            }

            var textStatus = "";

            switch (workOrderCase.CaseStatusesEnum)
            {
                case CaseStatusesEnum.Ongoing:
                    textStatus = _backendConfigurationLocalizationService.GetString("Ongoing");
                    break;
                case CaseStatusesEnum.Completed:
                    textStatus = _backendConfigurationLocalizationService.GetString("Completed");
                    break;
                case CaseStatusesEnum.Awaiting:
                    textStatus = _backendConfigurationLocalizationService.GetString("Awaiting");
                    break;
                case CaseStatusesEnum.Ordered:
                    textStatus = _backendConfigurationLocalizationService.GetString("Ordered");
                    break;
            }

            var assignedTo = site.Name == siteName ? "" : $"<strong>{_backendConfigurationLocalizationService.GetString("AssignedTo")}:</strong> {siteName}<br>";

            var areaName = !string.IsNullOrEmpty(workOrderCase.SelectedAreaName)
                ? $"<strong>{_backendConfigurationLocalizationService.GetString("Area")}:</strong> {workOrderCase.SelectedAreaName}<br>"
                : "";

            var outerDescription = $"<strong>{_backendConfigurationLocalizationService.GetString("Location")}:</strong> {property.Name}<br>" +
                                   areaName +
                                   $"<strong>{_backendConfigurationLocalizationService.GetString("Description")}:</strong> {newDescription}<br>" +
                                   priorityText +
                                   assignedTo +
                                   $"<strong>{_backendConfigurationLocalizationService.GetString("Status")}:</strong> {textStatus}<br><br>";
            var siteLanguage = await sdkDbContext.Languages.FirstAsync(x => x.Id == site.LanguageId);
            var mainElement = await _sdkCore.ReadeForm(eformId, siteLanguage);
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
            if (site.Name == siteName)
            {
                mainElement.CheckListFolderName = sdkDbContext.Folders.First(x => x.Id == (workOrderCase.Priority != "1" ? property.FolderIdForOngoingTasks : property.FolderIdForTasks))
                    .MicrotingUid.ToString();
                folderId = property.FolderIdForOngoingTasks;
                mainElement.PushMessageTitle = pushMessageTitle;
                mainElement.PushMessageBody = pushMessageBody;
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
            ((ShowPdf) ((DataElement) mainElement.ElementList[0]).DataItemList[1]).Value = pdfHash;

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
            int caseId = 0;
            if (workOrderCase.CaseStatusesEnum != CaseStatusesEnum.Completed)
            {
                caseId = (int)await _sdkCore.CaseCreate(mainElement, "", (int)site.MicrotingUid, folderId);
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
                LastAssignedToName = siteName,
                LastUpdatedByName = $"{updatedByName} - web",
                LeadingCase = i == 0,
                Priority = workOrderCase.Priority
            }.Create(backendConfigurationPnDbContext);
            i++;
        }
    }
}