namespace BackendConfiguration.Pn.Infrastructure.Models.Properties;

using System.Collections.Generic;

public class PropertyFolderModel
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public int? ParentId { get; set; }
    public int? MicrotingUId { get; set; }
    public List<PropertyFolderModel> Children { get; set; }
        = [];
}