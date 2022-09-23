using Microting.eFormApi.BasePn.Infrastructure.Models.Common;

namespace BackendConfiguration.Pn.Infrastructure.Models.Documents;

public class BackendConfigurationDocumentFolderRequestModel : FilterAndSortModel
{
    public int PageIndex { get; set; }

    public int PageSize { get; set; }

    public int Offset { get; set; }
}