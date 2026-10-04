using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;

namespace BackendConfiguration.Pn.Integration.Test;

/// <summary>Fixture data for the Indbakke tests. Fictional names and example.org/.net addresses only.</summary>
public static class InboxTestData
{
    /// <summary>Empties the inbox and archive tables. Properties are left behind; assert by id, not by set.</summary>
    public static async Task ClearAsync(BackendConfigurationPnDbContext db)
    {
        db.InboxSuggestions.RemoveRange(db.InboxSuggestions);
        db.InboxDocuments.RemoveRange(db.InboxDocuments);
        db.InboxSenderRules.RemoveRange(db.InboxSenderRules);
        db.InboxAddresses.RemoveRange(db.InboxAddresses);
        db.FilesTags.RemoveRange(db.FilesTags);
        db.PropertyFiles.RemoveRange(db.PropertyFiles);
        db.UploadedDatas.RemoveRange(db.UploadedDatas);
        db.Files.RemoveRange(db.Files);
        db.FileTags.RemoveRange(db.FileTags);
        await db.SaveChangesAsync();
    }

    public static async Task<Property> PropertyAsync(BackendConfigurationPnDbContext db, string name = "Nordvej 12",
        string address = "Nordvej 12, 8000 Aarhus C")
    {
        var p = new Property { Name = name, Address = address, ItemPlanningTagId = 0, CreatedByUserId = 1, UpdatedByUserId = 1 };
        await p.Create(db);
        return p;
    }

    public static async Task<FileTag> TagAsync(BackendConfigurationPnDbContext db, string name = "Ventilation")
    {
        var t = new FileTag { Name = name, CreatedByUserId = 1, UpdatedByUserId = 1 };
        await t.Create(db);
        return t;
    }

    public static async Task<InboxDocument> ReadyDocumentAsync(BackendConfigurationPnDbContext db,
        string md5 = "0123456789abcdef0123456789abcdef")
    {
        var d = new InboxDocument
        {
            HubDocumentId = Guid.NewGuid().ToString(), FromAddress = "jane.doe@example.org",
            Subject = "Fwd: Servicerapport", ReceivedAt = DateTime.UtcNow, FileName = "Servicerapport VA-02.pdf",
            SizeBytes = 13, Status = InboxDocumentStatus.Ready, Md5 = md5, DeliveredAt = DateTime.UtcNow,
            CreatedByUserId = 0, UpdatedByUserId = 0
        };
        await d.Create(db);
        return d;
    }
}
