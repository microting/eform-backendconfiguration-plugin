using Microsoft.AspNetCore.Http;

namespace BackendConfiguration.Pn.Infrastructure.Models.Documents;

public class BackendConfigurationDocumentUploadedData
{
    public int? Id { get; set; }
    public int? DocumentId { get; set; }
    public int? UploadedDataId { get; set; }
    public int LanguageId { get; set; }
    public IFormFile File { get; set; }
    public string Name { get; set; }
    public string Hash { get; set; }
    public string FileName { get; set; }
}