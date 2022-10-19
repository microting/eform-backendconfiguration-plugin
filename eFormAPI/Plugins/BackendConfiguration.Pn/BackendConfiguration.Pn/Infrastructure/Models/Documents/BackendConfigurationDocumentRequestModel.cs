using Microting.eFormApi.BasePn.Infrastructure.Models.Common;

namespace BackendConfiguration.Pn.Infrastructure.Models.Documents;

public class BackendConfigurationDocumentRequestModel : FilterAndSortModel
{
    public int PropertyId { get; set; }

    public int? FolderId { get; set; }

    public int? DocumentId { get; set; }

    public int? Expiration { get; set; }

    public int PageIndex { get; set; }

    public int PageSize { get; set; }

    public int Offset { get; set; }
}