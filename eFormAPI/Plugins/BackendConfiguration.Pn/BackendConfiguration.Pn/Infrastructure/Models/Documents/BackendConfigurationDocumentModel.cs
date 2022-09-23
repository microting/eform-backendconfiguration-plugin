using System;
using System.Collections.Generic;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;

namespace BackendConfiguration.Pn.Infrastructure.Models.Documents;

public class BackendConfigurationDocumentModel
{
    public int Id { get; set; }
    public List<BackendConfigurationDocumentTranslationModel> DocumentTranslations { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public bool Approvable { get; set; }
    public bool RetractIfApproved { get; set; }
    public bool AlwaysVisible { get; set; }
    public int FolderId { get; set; }
    public string PropertyNames { get; set; }
    public List<BackendConfigurationDocumentProperty> DocumentProperties { get; set; }
}