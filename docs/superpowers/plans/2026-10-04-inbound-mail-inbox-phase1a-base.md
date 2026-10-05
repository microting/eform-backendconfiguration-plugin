# Inbound Mail Inbox, Phase 1A (base entities): Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or
> superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for
> tracking.

**Goal:** add the tenant-side inbox tables (address, sender rules, documents, suggestions), the `inbox_enable`
claim constant and the migration to `Microting.EformBackendConfigurationBase`, then release them as a NuGet
package.

**Architecture:** four new `PnBase` entities, each with its `…Version` twin. `PnBase.Create/Update/Delete`
writes the version rows by reflection: `MapVersion` looks up `<FullName>Version` and copies scalar properties,
mapping `Id` to `<ClassName>Id`. The enums live in `Infrastructure/Enum`. One migration.

**Tech Stack:** .NET 10, EF Core 10 (Pomelo MySQL/MariaDB), NUnit 4.

**Spec:** `eform-backendconfiguration-plugin/docs/superpowers/specs/2026-10-04-inbox-tenant-side-design.md`
(section "Entities").

**Repo:** `/home/rene/Documents/workspace/microting/eform-backendconfiguration-base`, target `master`.

## Global Constraints

- Migrations live only in this repo. Never add them in the plugin.
- Every entity extends `PnBase`. Every entity has a `…Version` class in the same namespace
  (`Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities`). The version class repeats the
  scalar properties plus `int <ClassName>Id`.
- Every new file starts with the MIT licence header used across the repo; copy it from `FileTag.cs`.
- Use fictional data only (`4711`, `jane.doe@example.org`).
- Tests run only in CI. Locally, run `dotnet build` and nothing more. The one exception is the local MariaDB
  container used only to generate the migration in Task 2, Step 2.

## Review Focus

- **Two deliveries with the same `HubDocumentId`** (a hub retry after a timeout) must not create two rows. The
  unique index is the last line of defence. Pinned by `InboxDocument_DuplicateHubDocumentId_Throws` in Task 1.
- **A long subject or file name** (mail subjects can be close to 1000 characters) must not crash the insert.
  Columns are sized, and the plugin truncates before saving (Phase 1C). The test asserts that a 500-character
  subject saves.
- **Danish characters in addresses and evidence** (`Søndervej`) must round-trip. Pinned in Task 1's suggestion
  test.
- **An address token hash looked up after rotation:** two rows with different hashes, both readable. Pinned
  by the unique index on `TokenHash` and `InboxAddress_TwoRows_DifferentHash_BothSave`.
- **Soft delete:** `Delete()` must keep the row with `WorkflowState = removed` and write a version row.
  Pinned in Task 1.

---

### Task 1: Entities, enums, claim, DbSets and entity tests

**Files:**
- Create: `Microting.EformBackendConfigurationBase/Infrastructure/Enum/InboxDocumentStatus.cs`
- Create: `Microting.EformBackendConfigurationBase/Infrastructure/Enum/InboxSuggestionKind.cs`
- Create: `Microting.EformBackendConfigurationBase/Infrastructure/Enum/InboxSuggestionSource.cs`
- Create: `Microting.EformBackendConfigurationBase/Infrastructure/Enum/InboxSenderRuleKind.cs`
- Create: `Microting.EformBackendConfigurationBase/Infrastructure/Data/Entities/InboxAddress.cs` + `InboxAddressVersion.cs`
- Create: `Microting.EformBackendConfigurationBase/Infrastructure/Data/Entities/InboxSenderRule.cs` + `InboxSenderRuleVersion.cs`
- Create: `Microting.EformBackendConfigurationBase/Infrastructure/Data/Entities/InboxDocument.cs` + `InboxDocumentVersion.cs`
- Create: `Microting.EformBackendConfigurationBase/Infrastructure/Data/Entities/InboxSuggestion.cs` + `InboxSuggestionVersion.cs`
- Modify: `Microting.EformBackendConfigurationBase/Infrastructure/Data/BackendConfigurationPnDbContext.cs` (DbSets
  near line 112; indexes in `OnModelCreating` after the `DeviceToken` indexes, near line 211)
- Modify: `Microting.EformBackendConfigurationBase/Infrastructure/Const/BackendConfigurationClaims.cs` (after line 37)
- Test: `Microting.EformBackendConfigurationBase.Tests/InboxEntitiesUTest.cs`

**Interfaces:**
- Produces, used by Phase 1C:
  - `DbSet<InboxAddress> InboxAddresses`, `DbSet<InboxAddressVersion> InboxAddressVersions`;
  - `DbSet<InboxSenderRule> InboxSenderRules`, `DbSet<InboxSenderRuleVersion> InboxSenderRuleVersions`;
  - `DbSet<InboxDocument> InboxDocuments`, `DbSet<InboxDocumentVersion> InboxDocumentVersions`;
  - `DbSet<InboxSuggestion> InboxSuggestions`, `DbSet<InboxSuggestionVersion> InboxSuggestionVersions`;
  - the enums `InboxDocumentStatus`, `InboxSuggestionKind`, `InboxSuggestionSource`, `InboxSenderRuleKind`;
  - `BackendConfigurationClaims.EnableInbox = "inbox_enable"`.

- [ ] **Step 1: Branch**

```bash
cd /home/rene/Documents/workspace/microting/eform-backendconfiguration-base
git checkout master && git pull
git checkout -b feat/inbound-inbox-entities
```

- [ ] **Step 2: Write the failing tests**

Create `Microting.EformBackendConfigurationBase.Tests/InboxEntitiesUTest.cs`. Put the licence header from
`AdhocTagUTest.cs` at the top, then:

```csharp
namespace Microting.EformBackendConfigurationBase.Tests;

using System;
using System.Linq;
using System.Threading.Tasks;
using eForm.Infrastructure.Constants;
using Infrastructure.Data.Entities;
using Infrastructure.Enum;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

[TestFixture]
public class InboxEntitiesUTest : DbTestFixture
{
    private static InboxDocument NewDocument(string hubId = null) => new()
    {
        HubDocumentId = hubId ?? Guid.NewGuid().ToString(),
        FromAddress = "jane.doe@example.org",
        Subject = "Fwd: Servicerapport ventilationsanlæg",
        ReceivedAt = new DateTime(2026, 10, 2, 9, 42, 0, DateTimeKind.Utc),
        FileName = "Servicerapport VA-02.pdf",
        SizeBytes = 421_888,
        Status = InboxDocumentStatus.Preparing,
        CreatedByUserId = 0,
        UpdatedByUserId = 0
    };

    [Test]
    public async Task InboxDocument_Create_SavesRowAndVersion()
    {
        var doc = NewDocument();

        await doc.Create(DbContext);

        var rows = await DbContext.InboxDocuments.AsNoTracking().ToListAsync();
        var versions = await DbContext.InboxDocumentVersions.AsNoTracking().ToListAsync();
        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0].Status, Is.EqualTo(InboxDocumentStatus.Preparing));
        Assert.That(rows[0].Subject, Is.EqualTo("Fwd: Servicerapport ventilationsanlæg"));
        Assert.That(versions, Has.Count.EqualTo(1));
        Assert.That(versions[0].InboxDocumentId, Is.EqualTo(doc.Id));
        Assert.That(versions[0].HubDocumentId, Is.EqualTo(doc.HubDocumentId));
    }

    [Test]
    public async Task InboxDocument_UpdateStatus_WritesSecondVersion()
    {
        var doc = NewDocument();
        await doc.Create(DbContext);

        doc.Status = InboxDocumentStatus.Ready;
        doc.Md5 = "0123456789abcdef0123456789abcdef";
        await doc.Update(DbContext);

        var versions = await DbContext.InboxDocumentVersions.AsNoTracking()
            .OrderBy(v => v.Version).ToListAsync();
        Assert.That(versions, Has.Count.EqualTo(2));
        Assert.That(versions[1].Status, Is.EqualTo(InboxDocumentStatus.Ready));
        Assert.That(versions[1].Md5, Is.EqualTo("0123456789abcdef0123456789abcdef"));
    }

    [Test]
    public async Task InboxDocument_Delete_IsSoft()
    {
        var doc = NewDocument();
        await doc.Create(DbContext);

        await doc.Delete(DbContext);

        var row = await DbContext.InboxDocuments.AsNoTracking().SingleAsync();
        Assert.That(row.WorkflowState, Is.EqualTo(Constants.WorkflowStates.Removed));
        Assert.That(await DbContext.InboxDocumentVersions.CountAsync(), Is.EqualTo(2));
    }

    [Test]
    public async Task InboxDocument_LongSubject_Saves()
    {
        var doc = NewDocument();
        doc.Subject = new string('æ', 500);

        await doc.Create(DbContext);

        var row = await DbContext.InboxDocuments.AsNoTracking().SingleAsync();
        Assert.That(row.Subject.Length, Is.EqualTo(500));
    }

    [Test]
    public async Task InboxDocument_DuplicateHubDocumentId_Throws()
    {
        var hubId = Guid.NewGuid().ToString();
        await NewDocument(hubId).Create(DbContext);

        Assert.ThrowsAsync<DbUpdateException>(async () => await NewDocument(hubId).Create(DbContext));
    }

    [Test]
    public async Task InboxSuggestion_Create_KeepsDanishEvidence()
    {
        var doc = NewDocument();
        await doc.Create(DbContext);
        var suggestion = new InboxSuggestion
        {
            InboxDocumentId = doc.Id,
            Kind = InboxSuggestionKind.Property,
            TargetId = 12,
            Source = InboxSuggestionSource.TextMatch,
            Confidence = 0.5,
            Evidence = "Leveringsadresse: Søndervej 4, 8600 Silkeborg",
            Page = 1,
            Reason = "Adressen står i dokumentet.",
            CreatedByUserId = 0,
            UpdatedByUserId = 0
        };

        await suggestion.Create(DbContext);

        var row = await DbContext.InboxSuggestions.AsNoTracking().SingleAsync();
        Assert.That(row.Evidence, Is.EqualTo("Leveringsadresse: Søndervej 4, 8600 Silkeborg"));
        Assert.That(row.Accepted, Is.Null);
        var version = await DbContext.InboxSuggestionVersions.AsNoTracking().SingleAsync();
        Assert.That(version.InboxSuggestionId, Is.EqualTo(suggestion.Id));
        Assert.That(version.InboxDocumentId, Is.EqualTo(doc.Id));
    }

    [Test]
    public async Task InboxAddress_TwoRows_DifferentHash_BothSave()
    {
        var oldAddress = new InboxAddress
        {
            Address = "4711-k7f2q9abcd@indbakke.example.test",
            TokenHash = new string('a', 64),
            Active = false,
            GraceUntil = new DateTime(2026, 10, 11, 0, 0, 0, DateTimeKind.Utc),
            CreatedByUserId = 1, UpdatedByUserId = 1
        };
        var newAddress = new InboxAddress
        {
            Address = "4711-p3xm8defgh@indbakke.example.test",
            TokenHash = new string('b', 64),
            Active = true,
            CreatedByUserId = 1, UpdatedByUserId = 1
        };

        await oldAddress.Create(DbContext);
        await newAddress.Create(DbContext);

        Assert.That(await DbContext.InboxAddresses.CountAsync(), Is.EqualTo(2));
        Assert.That(await DbContext.InboxAddressVersions.CountAsync(), Is.EqualTo(2));
    }

    [Test]
    public async Task InboxSenderRule_Create_SavesKind()
    {
        var rule = new InboxSenderRule
        {
            Pattern = "@example.org",
            Kind = InboxSenderRuleKind.Allow,
            CreatedByUserId = 1, UpdatedByUserId = 1
        };

        await rule.Create(DbContext);

        var row = await DbContext.InboxSenderRules.AsNoTracking().SingleAsync();
        Assert.That(row.Kind, Is.EqualTo(InboxSenderRuleKind.Allow));
        Assert.That(await DbContext.InboxSenderRuleVersions.CountAsync(), Is.EqualTo(1));
    }
}
```

- [ ] **Step 3: Build to verify it fails**

Run: `dotnet build Microting.EformBackendConfigurationBase.sln`
Expected: FAIL with `CS0246: The type or namespace name 'InboxDocument' could not be found`, and the same for
the other new types.

- [ ] **Step 4: Write the enums**

`Infrastructure/Enum/InboxDocumentStatus.cs`, with the licence header:

```csharp
namespace Microting.EformBackendConfigurationBase.Infrastructure.Enum;

public enum InboxDocumentStatus
{
    Preparing = 0,
    SenderPending = 1,
    Ready = 2,
    Failed = 3,
    Filed = 4,
    Rejected = 5
}
```

`Infrastructure/Enum/InboxSuggestionKind.cs`:

```csharp
namespace Microting.EformBackendConfigurationBase.Infrastructure.Enum;

public enum InboxSuggestionKind
{
    Property = 0,
    Tag = 1
}
```

`Infrastructure/Enum/InboxSuggestionSource.cs`:

```csharp
namespace Microting.EformBackendConfigurationBase.Infrastructure.Enum;

public enum InboxSuggestionSource
{
    TextMatch = 0,
    Ai = 1,
    Reviewer = 2
}
```

`Infrastructure/Enum/InboxSenderRuleKind.cs`:

```csharp
namespace Microting.EformBackendConfigurationBase.Infrastructure.Enum;

public enum InboxSenderRuleKind
{
    Allow = 0,
    Block = 1
}
```

- [ ] **Step 5: Write the entities and version twins**

`Infrastructure/Data/Entities/InboxAddress.cs`:

```csharp
namespace Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;

using System;
using System.ComponentModel.DataAnnotations;

/// <summary>
/// The tenant's inbound mail address. One row is Active; a rotated-out row keeps
/// working until GraceUntil. The hub only ever sees TokenHash.
/// </summary>
public class InboxAddress : PnBase
{
    [Required]
    [StringLength(254)]
    public string Address { get; set; }

    /// <summary>Lower-case hex SHA-256 of the token part of the local part.</summary>
    [Required]
    [StringLength(64)]
    public string TokenHash { get; set; }

    public bool Active { get; set; }

    public DateTime? GraceUntil { get; set; }
}
```

`Infrastructure/Data/Entities/InboxAddressVersion.cs`:

```csharp
namespace Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;

using System;
using System.ComponentModel.DataAnnotations.Schema;

public class InboxAddressVersion : PnBase
{
    public string Address { get; set; }

    public string TokenHash { get; set; }

    public bool Active { get; set; }

    public DateTime? GraceUntil { get; set; }

    [ForeignKey("InboxAddress")]
    public int InboxAddressId { get; set; }
}
```

`Infrastructure/Data/Entities/InboxSenderRule.cs`:

```csharp
namespace Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;

using System.ComponentModel.DataAnnotations;
using Enum;

/// <summary>An exact address ("jane.doe@example.org") or a domain ("@example.org").</summary>
public class InboxSenderRule : PnBase
{
    [Required]
    [StringLength(254)]
    public string Pattern { get; set; }

    public InboxSenderRuleKind Kind { get; set; }
}
```

`Infrastructure/Data/Entities/InboxSenderRuleVersion.cs`:

```csharp
namespace Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;

using System.ComponentModel.DataAnnotations.Schema;
using Enum;

public class InboxSenderRuleVersion : PnBase
{
    public string Pattern { get; set; }

    public InboxSenderRuleKind Kind { get; set; }

    [ForeignKey("InboxSenderRule")]
    public int InboxSenderRuleId { get; set; }
}
```

`Infrastructure/Data/Entities/InboxDocument.cs`:

```csharp
namespace Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Enum;

/// <summary>
/// One PDF received through the inbound mail hub. Created by the hub's "arrived"
/// call (Preparing / SenderPending), completed by "deliver" (Ready) or "failed",
/// and ends Filed or Rejected by a manager.
/// </summary>
public class InboxDocument : PnBase
{
    [Required]
    [StringLength(36)]
    public string HubDocumentId { get; set; }

    [Required]
    [StringLength(254)]
    public string FromAddress { get; set; }

    [StringLength(500)]
    public string Subject { get; set; }

    public DateTime ReceivedAt { get; set; }

    public DateTime? ReadyBy { get; set; }

    public DateTime? DeliveredAt { get; set; }

    [Required]
    [StringLength(250)]
    public string FileName { get; set; }

    public int? PageCount { get; set; }

    public long SizeBytes { get; set; }

    [StringLength(20)]
    public string SpfResult { get; set; }

    [StringLength(20)]
    public string DkimResult { get; set; }

    public InboxDocumentStatus Status { get; set; }

    [StringLength(500)]
    public string FailureReason { get; set; }

    public bool ReviewedByMicroting { get; set; }

    /// <summary>MD5 hex of the PDF; the storage object is "{Md5}.pdf" (same convention as Files).</summary>
    [StringLength(32)]
    public string Md5 { get; set; }

    public int? FiledFileId { get; set; }

    public DateTime? FiledAt { get; set; }

    public int? FiledByUserId { get; set; }

    public virtual ICollection<InboxSuggestion> Suggestions { get; set; } = new List<InboxSuggestion>();
}
```

`Infrastructure/Data/Entities/InboxDocumentVersion.cs`:

```csharp
namespace Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;

using System;
using System.ComponentModel.DataAnnotations.Schema;
using Enum;

public class InboxDocumentVersion : PnBase
{
    public string HubDocumentId { get; set; }
    public string FromAddress { get; set; }
    public string Subject { get; set; }
    public DateTime ReceivedAt { get; set; }
    public DateTime? ReadyBy { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public string FileName { get; set; }
    public int? PageCount { get; set; }
    public long SizeBytes { get; set; }
    public string SpfResult { get; set; }
    public string DkimResult { get; set; }
    public InboxDocumentStatus Status { get; set; }
    public string FailureReason { get; set; }
    public bool ReviewedByMicroting { get; set; }
    public string Md5 { get; set; }
    public int? FiledFileId { get; set; }
    public DateTime? FiledAt { get; set; }
    public int? FiledByUserId { get; set; }

    [ForeignKey("InboxDocument")]
    public int InboxDocumentId { get; set; }
}
```

`MapVersion` skips any property whose type name contains the entities namespace. So `Suggestions`, an
`ICollection<InboxSuggestion>`, is skipped and needs no twin.

`Infrastructure/Data/Entities/InboxSuggestion.cs`:

```csharp
namespace Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;

using System.ComponentModel.DataAnnotations;
using Enum;

public class InboxSuggestion : PnBase
{
    public int InboxDocumentId { get; set; }

    public virtual InboxDocument InboxDocument { get; set; }

    public InboxSuggestionKind Kind { get; set; }

    /// <summary>Property.Id when Kind = Property, FileTag.Id when Kind = Tag.</summary>
    public int TargetId { get; set; }

    public InboxSuggestionSource Source { get; set; }

    /// <summary>0..1.</summary>
    public double Confidence { get; set; }

    [StringLength(250)]
    public string Evidence { get; set; }

    public int? Page { get; set; }

    [StringLength(500)]
    public string Reason { get; set; }

    /// <summary>Null until the document is filed; then whether the manager kept it.</summary>
    public bool? Accepted { get; set; }
}
```

`Infrastructure/Data/Entities/InboxSuggestionVersion.cs`:

```csharp
namespace Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;

using System.ComponentModel.DataAnnotations.Schema;
using Enum;

public class InboxSuggestionVersion : PnBase
{
    public int InboxDocumentId { get; set; }
    public InboxSuggestionKind Kind { get; set; }
    public int TargetId { get; set; }
    public InboxSuggestionSource Source { get; set; }
    public double Confidence { get; set; }
    public string Evidence { get; set; }
    public int? Page { get; set; }
    public string Reason { get; set; }
    public bool? Accepted { get; set; }

    [ForeignKey("InboxSuggestion")]
    public int InboxSuggestionId { get; set; }
}
```

- [ ] **Step 6: Register the DbSets and indexes**

In `BackendConfigurationPnDbContext.cs`, after the `FilesTagsVersions` DbSet (line 116), add:

```csharp
    public DbSet<InboxAddress> InboxAddresses { get; set; }
    public DbSet<InboxAddressVersion> InboxAddressVersions { get; set; }
    public DbSet<InboxSenderRule> InboxSenderRules { get; set; }
    public DbSet<InboxSenderRuleVersion> InboxSenderRuleVersions { get; set; }
    public DbSet<InboxDocument> InboxDocuments { get; set; }
    public DbSet<InboxDocumentVersion> InboxDocumentVersions { get; set; }
    public DbSet<InboxSuggestion> InboxSuggestions { get; set; }
    public DbSet<InboxSuggestionVersion> InboxSuggestionVersions { get; set; }
```

In `OnModelCreating`, directly after the `DeviceToken` `FcmToken` index, add:

```csharp
        // A hub retry after a timeout must not create a second inbox row.
        modelBuilder.Entity<InboxDocument>()
            .HasIndex(e => e.HubDocumentId)
            .IsUnique();

        // Inbox list query: live documents by status.
        modelBuilder.Entity<InboxDocument>()
            .HasIndex(e => new { e.Status, e.WorkflowState });

        modelBuilder.Entity<InboxAddress>()
            .HasIndex(e => e.TokenHash)
            .IsUnique();

        modelBuilder.Entity<InboxSuggestion>()
            .HasOne(x => x.InboxDocument)
            .WithMany(x => x.Suggestions)
            .HasForeignKey(x => x.InboxDocumentId);
```

- [ ] **Step 7: Add the claim constant**

In `Infrastructure/Const/BackendConfigurationClaims.cs`, after
`public const string EnableFilesManagement = "files_management_enable";` (line 37):

```csharp
    /// <summary>Managers who review and file documents from the inbound mail Indbakke.</summary>
    public const string EnableInbox = "inbox_enable";
```

- [ ] **Step 8: Build**

Run: `dotnet build Microting.EformBackendConfigurationBase.sln`
Expected: `Build succeeded.` with 0 errors. The tests themselves run in CI.

- [ ] **Step 9: Dual review gate, then commit**

Dispatch `superpowers:requesting-code-review` and a `code-simplifier` subagent in parallel on the diff, and act
on their findings. Then:

```bash
git add Microting.EformBackendConfigurationBase/Infrastructure/Enum/InboxDocumentStatus.cs \
  Microting.EformBackendConfigurationBase/Infrastructure/Enum/InboxSuggestionKind.cs \
  Microting.EformBackendConfigurationBase/Infrastructure/Enum/InboxSuggestionSource.cs \
  Microting.EformBackendConfigurationBase/Infrastructure/Enum/InboxSenderRuleKind.cs \
  Microting.EformBackendConfigurationBase/Infrastructure/Data/Entities/InboxAddress.cs \
  Microting.EformBackendConfigurationBase/Infrastructure/Data/Entities/InboxAddressVersion.cs \
  Microting.EformBackendConfigurationBase/Infrastructure/Data/Entities/InboxSenderRule.cs \
  Microting.EformBackendConfigurationBase/Infrastructure/Data/Entities/InboxSenderRuleVersion.cs \
  Microting.EformBackendConfigurationBase/Infrastructure/Data/Entities/InboxDocument.cs \
  Microting.EformBackendConfigurationBase/Infrastructure/Data/Entities/InboxDocumentVersion.cs \
  Microting.EformBackendConfigurationBase/Infrastructure/Data/Entities/InboxSuggestion.cs \
  Microting.EformBackendConfigurationBase/Infrastructure/Data/Entities/InboxSuggestionVersion.cs \
  Microting.EformBackendConfigurationBase/Infrastructure/Data/BackendConfigurationPnDbContext.cs \
  Microting.EformBackendConfigurationBase/Infrastructure/Const/BackendConfigurationClaims.cs \
  Microting.EformBackendConfigurationBase.Tests/InboxEntitiesUTest.cs
git commit -m "feat: inbox entities for the inbound mail archive inbox

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Migration, PR and release

**Files:**
- Create: `Microting.EformBackendConfigurationBase/Migrations/<timestamp>_AddInboundMailInbox.cs` (generated)
- Create: `Microting.EformBackendConfigurationBase/Migrations/<timestamp>_AddInboundMailInbox.Designer.cs` (generated)
- Modify: `Microting.EformBackendConfigurationBase/Migrations/BackendConfigurationPnDbContextModelSnapshot.cs` (generated)
- Test: `Microting.EformBackendConfigurationBase.Tests/InboxMigrationUTest.cs`

**Interfaces:**
- Consumes: the Task 1 entities.
- Produces: the database tables `InboxAddresses`, `InboxSenderRules`, `InboxDocuments`, `InboxSuggestions`
  and their `…Versions`. The NuGet version number is recorded in Step 7 for Phase 1C.

- [ ] **Step 1: Write the failing migration test**

`Microting.EformBackendConfigurationBase.Tests/InboxMigrationUTest.cs`, with the licence header. Follow
`DeviceTokenMigrationUTest.cs`, which checks that an index landed:

```csharp
namespace Microting.EformBackendConfigurationBase.Tests;

using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

[TestFixture]
public class InboxMigrationUTest : DbTestFixture
{
    [Test]
    public async Task Migration_CreatesUniqueHubDocumentIdIndex()
    {
        var conn = DbContext.Database.GetDbConnection();
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT NON_UNIQUE FROM information_schema.STATISTICS " +
            "WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'InboxDocuments' " +
            "AND COLUMN_NAME = 'HubDocumentId'";
        var nonUnique = await cmd.ExecuteScalarAsync();

        Assert.That(nonUnique, Is.Not.Null, "index on InboxDocuments.HubDocumentId is missing");
        Assert.That(System.Convert.ToInt32(nonUnique), Is.EqualTo(0), "index must be unique");
    }

    [Test]
    public void Migration_IsTheLatest()
    {
        var latest = DbContext.Database.GetMigrations().Last();
        Assert.That(latest, Does.EndWith("_AddInboundMailInbox"));
    }
}
```

- [ ] **Step 2: Generate the migration against a throwaway local MariaDB**

This is generation only, not a test run. `AutoDetect` in the design-time factory needs a reachable server.

```bash
docker run --rm -d --name inbox-mig -e MYSQL_ROOT_PASSWORD=secretpassword -p 3306:3306 mariadb:10.8
timeout 60 sh -c 'until docker exec inbox-mig mariadb-admin ping -psecretpassword --silent; do sleep 1; done'
dotnet ef migrations add AddInboundMailInbox \
  --project Microting.EformBackendConfigurationBase --startup-project DBMigrator
docker stop inbox-mig
```

Expected: three files are added or changed under `Migrations/`. The generated `Up()` creates the 8 tables and
the indexes `IX_InboxDocuments_HubDocumentId` (unique), `IX_InboxDocuments_Status_WorkflowState`,
`IX_InboxAddresses_TokenHash` (unique) and `IX_InboxSuggestions_InboxDocumentId`. Read the generated file and
confirm it contains no unrelated changes (no `AlterColumn` on existing tables). If it does, the snapshot was
stale. Stop and report it instead of committing it.

- [ ] **Step 3: Build**

Run: `dotnet build Microting.EformBackendConfigurationBase.sln`
Expected: `Build succeeded.`

- [ ] **Step 4: Dual review gate, then commit**

```bash
git add Microting.EformBackendConfigurationBase/Migrations/*_AddInboundMailInbox.cs \
  Microting.EformBackendConfigurationBase/Migrations/*_AddInboundMailInbox.Designer.cs \
  Microting.EformBackendConfigurationBase/Migrations/BackendConfigurationPnDbContextModelSnapshot.cs \
  Microting.EformBackendConfigurationBase.Tests/InboxMigrationUTest.cs
git commit -m "feat: migration for the inbound mail inbox tables

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

- [ ] **Step 5: Push and open the PR**

```bash
git push -u origin feat/inbound-inbox-entities
gh pr create --base master --title "feat: inbox entities for the inbound mail archive inbox" --body "$(cat <<'EOF'
Adds the tenant-side tables for the inbound mail inbox (spec: eform-backendconfiguration-plugin
docs/superpowers/specs/2026-10-04-inbox-tenant-side-design.md):

- InboxAddress, InboxSenderRule, InboxDocument, InboxSuggestion + version twins
- unique HubDocumentId (hub retries cannot double-insert), unique TokenHash
- claim constant `inbox_enable`
- migration AddInboundMailInbox

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

- [ ] **Step 6: Watch CI to a verdict and consider the Copilot review**

Run `gh pr checks <n>` until every check has finished. Classify every red check as infrastructure or a real
failure, comparing against `master`'s latest run. Read the Copilot review
(`gh api repos/microting/eform-backendconfiguration-base/pulls/<n>/comments`), fix the valid findings through
the review gate, and note why the others do not apply.

- [ ] **Step 7: Release (human gate)**

Ask the user to merge the PR and cut the base release tag. Record the published NuGet version
(`Microting.EformBackendConfigurationBase` `10.0.<x>`) in the Phase 1C plan, Task 1, Step 1.
