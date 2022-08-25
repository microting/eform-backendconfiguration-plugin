using System.Collections.Generic;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;

namespace BackendConfiguration.Pn.Messages;

public class WorkOrderCreated
{
    public List<KeyValuePair<int, int>> PropertyWorkers { get; set; }
    public int EformId { get; set; }
    public int FolderId { get; set; }
    public string Description { get; set; }
    public CaseStatusesEnum Status { get; set; }
    public int WorkorderCaseId { get; set; }
    public string NewDescription { get; set; }
    public int? DeviceUsersGroupId { get; set; }
    public string PushMessageBody { get; set; }
    public string PushMessageTitle { get; set; }
    public string AreaName { get; set; }
    public int CreatedByUserId {get; set;}
    public List<string> PicturesOfTasks { get; set; }

    public WorkOrderCreated(List<KeyValuePair<int, int>> propertyWorkers, int eformId, int folderId, string description, CaseStatusesEnum status, int workorderCaseId, string newDescription, int? deviceUsersGroupId, string pushMessageBody, string pushMessageTitle, string areaName, int createdByUserId, List<string> picturesOfTasks)
    {
        PropertyWorkers = propertyWorkers;
        EformId = eformId;
        FolderId = folderId;
        Description = description;
        Status = status;
        WorkorderCaseId = workorderCaseId;
        NewDescription = newDescription;
        DeviceUsersGroupId = deviceUsersGroupId;
        PushMessageBody = pushMessageBody;
        PushMessageTitle = pushMessageTitle;
        AreaName = areaName;
        CreatedByUserId = createdByUserId;
        PicturesOfTasks = picturesOfTasks;
    }
}