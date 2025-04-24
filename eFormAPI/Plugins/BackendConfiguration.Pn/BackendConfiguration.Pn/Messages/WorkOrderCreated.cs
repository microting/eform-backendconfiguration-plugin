using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using System.Collections.Generic;
using Microting.eForm.Infrastructure.Data.Entities;

namespace BackendConfiguration.Pn.Messages;

public class WorkOrderCreated(
    List<KeyValuePair<int, int>> propertyWorkers,
    int eformId,
    int folderId,
    string description,
    CaseStatusesEnum status,
    int workorderCaseId,
    string newDescription,
    int? deviceUsersGroupId,
    string pushMessageBody,
    string pushMessageTitle,
    string areaName,
    int createdByUserId,
    List<string> picturesOfTasks,
    Site assignedToSite,
    string propertyName,
    int idForOngoingTasks,
    int idForTasks,
    int idForCompletedTasks,
    bool hasImages,
    List<KeyValuePair<string, string>> picturesOfTasksList)
{
    public List<KeyValuePair<int, int>> PropertyWorkers { get; set; } = propertyWorkers;
    public int EformId { get; set; } = eformId;
    public int FolderId { get; set; } = folderId;
    public string Description { get; set; } = description;
    public CaseStatusesEnum Status { get; set; } = status;
    public int WorkorderCaseId { get; set; } = workorderCaseId;
    public string NewDescription { get; set; } = newDescription;
    public Site AssignedToSite { get; set; } = assignedToSite;
    public int? DeviceUsersGroupId { get; set; } = deviceUsersGroupId;
    public string PushMessageBody { get; set; } = pushMessageBody;
    public string PushMessageTitle { get; set; } = pushMessageTitle;
    public string AreaName { get; set; } = areaName;
    public int CreatedByUserId {get; set;} = createdByUserId;
    public List<string> PicturesOfTasks { get; set; } = picturesOfTasks;
    public string PropertyName { get; set; } = propertyName;
    public int FolderIdForOngoingTasks { get; set; } = idForOngoingTasks;
    public int FolderIdForTasks { get; set; } = idForTasks;
    public int FolderIdForCompletedTasks { get; set; } = idForCompletedTasks;
    public bool HasImages { get; set; } = hasImages;
    public List<KeyValuePair<string, string>> PicturesOfTasksList = picturesOfTasksList;
}