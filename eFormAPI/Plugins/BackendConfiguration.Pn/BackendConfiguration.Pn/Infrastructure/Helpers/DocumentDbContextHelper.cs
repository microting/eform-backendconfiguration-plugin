using Microting.eFormCaseTemplateBase.Infrastructure.Data;
using Microting.eFormCaseTemplateBase.Infrastructure.Data.Factories;

namespace BackendConfiguration.Pn.Infrastructure.Helpers;

public class DocumentDbContextHelper
{
    private string ConnectionString { get; set; }

    public DocumentDbContextHelper(string connectionString)
    {
        ConnectionString = connectionString;
    }

    public CaseTemplatePnDbContext GetDbContext()
    {
        CaseTemplatePnContextFactory contextFactory = new CaseTemplatePnContextFactory();

        return contextFactory.CreateDbContext(new[] { ConnectionString });
    }
}