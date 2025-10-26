using System.Collections.Generic;
using Microting.eForm.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;

namespace BackendConfiguration.Pn.Messages;

public class WorkOrderUpdated(
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
    List<KeyValuePair<string, string>> picturesOfTask,
    int createdByUserId,
    int updatedByUserId)
{
    public List<KeyValuePair<int, int>> PropertyWorkers { get; set; } = propertyWorkers;
    public readonly int EformId = eformId;
    public readonly int PropertyId = propertyId;
    public readonly string Description = description;
    public readonly CaseStatusesEnum Status = status;
    public readonly int WorkorderCaseId = workorderCaseId;
    public readonly string NewDescription = newDescription;
    public int? DeviceUsersGroupId = deviceUsersGroupId;
    // public readonly string PdfHash = pdfHash;
    public readonly Site AssignedToSite = assignedToSite;
    public readonly string PushMessageBody = pushMessageBody;
    public readonly string PushMessageTitle = pushMessageTitle;
    public readonly string UpdatedByName = updatedByName;
    public bool HasImages { get; set; } = hasImages;
    public List<KeyValuePair<string, string>> PicturesOfTask { get; set; } = picturesOfTask;
    public int CreatedByUserId {get; set;} = createdByUserId;
    public int UpdatedByUserId {get; set;} = updatedByUserId;
}