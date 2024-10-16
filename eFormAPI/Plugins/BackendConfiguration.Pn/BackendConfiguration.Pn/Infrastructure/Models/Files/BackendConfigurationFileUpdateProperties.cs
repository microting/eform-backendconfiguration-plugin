using System.Collections.Generic;

namespace BackendConfiguration.Pn.Infrastructure.Models.Files;

public class BackendConfigurationFileUpdateProperties
{
    public int Id { get; set; }

    public List<int> Properties { get; set; }
        = [];

}