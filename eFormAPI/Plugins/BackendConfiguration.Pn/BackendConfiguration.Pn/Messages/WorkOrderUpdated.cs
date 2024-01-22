using System.Collections.Generic;
using Microting.eForm.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;

namespace BackendConfiguration.Pn.Messages;

public class WorkOrderUpdated
{
    public List<KeyValuePair<int, int>> PropertyWorkers { get; set; }
    public int EformId;
    public int PropertyId;
    public string Description;
    public CaseStatusesEnum Status;
    public int WorkorderCaseId;
    public string NewDescription;
    public int? DeviceUsersGroupId;
    public string PdfHash;
    public Site AssignedToSite;
    public string PushMessageBody;
    public string PushMessageTitle;
    public string UpdatedByName;
    public bool HasImages { get; set; }

    public WorkOrderUpdated(List<KeyValuePair<int, int>> propertyWorkers, int eformId, int propertyId, string description, CaseStatusesEnum status, int workorderCaseId, string newDescription, int? deviceUsersGroupId, string pdfHash, Site assignedToSite, string pushMessageBody, string pushMessageTitle, string updatedByName, bool hasImages)
    {
        PropertyWorkers = propertyWorkers;
        EformId = eformId;
        PropertyId = propertyId;
        Description = description;
        Status = status;
        WorkorderCaseId = workorderCaseId;
        NewDescription = newDescription;
        DeviceUsersGroupId = deviceUsersGroupId;
        PdfHash = pdfHash;
        AssignedToSite = assignedToSite;
        PushMessageBody = pushMessageBody;
        PushMessageTitle = pushMessageTitle;
        UpdatedByName = updatedByName;
        HasImages = hasImages;
    }
}