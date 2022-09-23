using System.Collections.Generic;

namespace BackendConfiguration.Pn.Infrastructure.Models.Documents;

public class BackendConfigurationDocumentFolderModel
{
    public int Id { get; set; }

    public List<BackendConfigurationDocumentFolderTranslationModel> DocumentFolderTranslations { get; set; }

    public List<BackendConfigurationDocumentFolderPropertyModel> Properties { get; set; }
}