# PDF archive Indbakke, Phase 1C (tenant plugin): Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or
> superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for
> tracking.

**Goal:** the tenant side of the PDF archive Indbakke:
- the signed endpoints the central service calls (`arrived`, `catalog`, `deliver`, `failed`);
- the manager endpoints (list, review, file, undo, reject, approve or reject sender, settings);
- the Angular Indbakke and Indstillinger e-mail pages;
- Playwright coverage.

**Architecture:**
- **Two controllers.** `InboxHubController` is `[AllowAnonymous]` and verified by signature.
  `InboxController` is `[Authorize(Policy = "inbox_enable")]`. Both are thin over the services.
- **Services:** `InboxHubService`, `InboxService` and `InboxSettingsService`. Storage goes through
  `IArchiveStorage`, a seam over the SDK's `PutFileToStorageSystem` / `GetFileFromS3Storage`.
- **Filing** goes through a new `FileArchiver`, which today's upload `Create` also uses. The upload happens
  first, then one transaction creates the rows.
- **Angular:** a feature-module extension of `modules/files`, with sub-navigation shared by the three pages.

**Tech Stack:** .NET 10, ASP.NET Core MVC, EF Core 10 (Pomelo), NUnit + NSubstitute integration tests (real
MariaDB in CI), Angular (NgModule, Angular Material, `@ng-matero/extensions`), Playwright.

**Spec:** `docs/superpowers/specs/2026-10-04-inbox-tenant-side-design.md` (this repo).

**Repo:** `eform-backendconfiguration-plugin`, target `stable`. **Not in dev mode** (decided at session start),
so edits go directly in this repo. If the session is in dev mode, edit in the host app instead and sync back
with `devgetchanges.sh` (see the workspace CLAUDE.md).

## Global Constraints

- **Public repo.** Do not write central-service internals, AI or model details, costs, cluster names or keys
  into code, comments, commits or PR text. Describe the counterpart only as "the central inbound mail
  service".
- **Signature:**
  - canonical string `METHOD\npath\ncustomerNo\nrequestId\nDate\nsha256hex(body)`;
  - `sig = hex(HMAC_SHA256(utf8(tenantKey), utf8(canonical)))`, lower case;
  - headers `Authorization: HMAC-SHA256 <sig>`, `Date`, `X-Request-Id`, `X-Customer-No`;
  - ±2 minute skew; request ids cached for 10 minutes.
- **The test vector must pass:**
  - Inputs: tenant key `c25608a9e5271a2bafd480022de09ea60a11c7970566d3dca57034c662d7c767`, `4711`, `POST`,
    `/api/backend-configuration-pn/inbox/hub/arrived`, `00000000-0000-0000-0000-000000000001`,
    `Sun, 04 Oct 2026 10:00:00 GMT`, `{"a":1}`.
  - Expected body hash: `015abd7f5cc57a2dd94b7590f04ad8084273905ee33ec5cebeae62276a97f862`
  - Expected signature: `be3548b960e7fd4b4dfdd72518c1f33d5d642a0312a36d5b4d7eb1cf2d833b1a`
- **Hub-facing JSON** is camelCase. Enum values are camelCase names (`property`/`tag`, `textMatch`/`ai`/`reviewer`,
  `allowed`/`unknown`/`blocked`) and are parsed case-insensitively. It is serialised with explicit
  `System.Text.Json` options, independent of the host's MVC serializer.
- **Configuration:** section `InboundMailHub` with `HubUrl`, `TenantSigningKey` and `MailDomain`
  (default `indbakke.microting.dk`).
  - If `TenantSigningKey` is empty, every hub-facing call gets `401`.
  - If `HubUrl` is empty, outbound calls to the hub are skipped with a warning. The UI still works.
- **The setting `BackendConfigurationSettings:InboxUnknownSenderPolicy`** is `hold` or `refuse`, seeded to
  `hold`.
- **Use Danish copy as written below.** Every UI string goes through translate with `enUS` and `da` entries.
- **Only fictional data** (`4711`, `jane.doe@example.org`, `Example Org A/S`).
- **Testing:** tests run only in CI. Locally, run `dotnet build` for the backend and nothing more.
- **Playwright** follows this repo's CLAUDE.md: every wait bounded, `waitForApiResponse`, no
  `waitForTimeout`, no retries, scoped locators.
- **Every commit** goes through the dual review gate, with files staged by name.

## Lessons carried from the central service build (binding)

- The replay check must be atomic and tenant-scoped: key `"{customerNo}:{requestId}"`, a single atomic insert
  (`ConcurrentDictionary.TryAdd` with expiry pruning) — never `TryGetValue` followed by `Set`.
- The signature verifier must not cap the body below the `deliver` size (30 MB `RequestSizeLimit`); the
  64 KB cap the central service uses applies to its own small endpoints only.
- The central service answers `409` on `sender-decision` while it has not yet recorded the document as
  waiting for sender approval. `InboxSettingsService` must treat a 409 as "try again shortly" and return an
  `OperationResult(false, "InboxTryAgainShortly")` (da "Prøv igen om lidt", en "Try again in a moment") instead of throwing.
- Enum strings on the wire are camelCase (`textMatch`) and are parsed case-insensitively.
- Test mail addresses use `example.org` / `example.test` domains only (commit hook).

## Review Focus

- **The same `deliver` arrives twice** (the hub retries after a timeout). The second is a `204` no-op: no
  second storage write and no duplicate suggestions. Pinned by `Deliver_Twice_IsIdempotent` in Task 3.
- **A suggestion refers to a property or tag deleted since the catalog was read.** It is dropped on
  `deliver`, and filing validates ids. Pinned by `Deliver_DropsSuggestionsForDeletedTargets` (Task 3) and
  `File_UnknownPropertyId_IsRejected` (Task 4).
- **A manager files with no property selected.** The request is rejected with "Vælg mindst én ejendom", and
  the document stays Ready. Pinned by `File_NoProperty_IsRejected` (Task 4).
- **The sender's address case differs** from the user's login email (`Jane.Doe@example.org`). It must still
  count as allowed. Pinned by `Verdict_UserEmail_CaseInsensitive` (Task 3).
- **The storage upload fails during filing.** No `File` rows may be left half-created. Pinned by
  `Archive_UploadFails_CreatesNoRows` (Task 2).

---

## File map

```
eFormAPI/Plugins/BackendConfiguration.Pn/BackendConfiguration.Pn/
  BackendConfiguration.Pn.csproj                                  — bump Microting.EformBackendConfigurationBase
  EformBackendConfigurationPlugin.cs                              — DI + options binding
  Infrastructure/Models/Settings/InboundMailHubOptions.cs
  Infrastructure/Models/Inbox/HubContracts.cs                     — wire DTOs + JSON options
  Infrastructure/Models/Inbox/InboxModels.cs                      — UI DTOs
  Infrastructure/Data/Seed/Data/BackendConfigurationPermissionsSeedData.cs  — inbox_enable
  Infrastructure/Data/Seed/Data/BackendConfigurationSeedData.cs   — InboxUnknownSenderPolicy=hold
  Services/InboundMail/InboundMailSignature.cs
  Services/InboundMail/InboundMailRequestVerifier.cs
  Services/InboundMail/CustomerNoProvider.cs
  Services/InboundMail/InboundMailHubClient.cs
  Services/InboundMail/SenderVerdictResolver.cs
  Services/InboundMail/InboxAddressGenerator.cs
  Services/InboundMail/InboxHubService.cs
  Services/InboundMail/InboxService.cs
  Services/InboundMail/InboxSettingsService.cs
  Services/FileArchive/IArchiveStorage.cs
  Services/FileArchive/CoreArchiveStorage.cs
  Services/FileArchive/FileArchiver.cs
  Services/BackendConfigurationFilesService/BackendConfigurationFilesService.cs  — Create → FileArchiver
  Controllers/InboxHubController.cs
  Controllers/InboxController.cs
BackendConfiguration.Pn.Integration.Test/
  InboundMailSignatureTests.cs, FileArchiverTests.cs, InboxHubServiceTests.cs,
  InboxServiceTests.cs, InboxSettingsServiceTests.cs, InboxTestData.cs
eform-client/src/app/plugins/modules/backend-configuration-pn/
  models/inbox/inbox.models.ts, models/index.ts
  services/backend-configuration-pn-inbox.service.ts, services/index.ts
  enums/backend-configuration-pn-claims.const.ts                  — enableInbox
  modules/files/files.routing.ts, files.module.ts
  modules/files/components/archive-section-nav/…
  modules/files/components/inbox-container/…
  modules/files/components/inbox-review-dialog/…
  modules/files/components/inbox-settings/…
  modules/files/components/files-container/files-container.component.html  — section nav
  modules/files/components/index.ts
  i18n/enUS.ts, i18n/da.ts
eform-client/playwright/e2e/plugins/backend-configuration-pn/
  inbox-hub-seed.ts
  i/inbox.spec.ts
.github/workflows/dotnet-core-pr.yml, dotnet-core-master.yml      — CI-only signing key env
```

---

### Task 1: Package bump, options, permission, settings seed and request signature

**Files:**
- Modify: `BackendConfiguration.Pn/BackendConfiguration.Pn.csproj:282`
- Create: `BackendConfiguration.Pn/Infrastructure/Models/Settings/InboundMailHubOptions.cs`
- Create: `BackendConfiguration.Pn/Services/InboundMail/InboundMailSignature.cs`,
  `InboundMailRequestVerifier.cs`, `CustomerNoProvider.cs`
- Modify: `BackendConfiguration.Pn/EformBackendConfigurationPlugin.cs` (`ConfigureServices` near line 175; the
  options method near line 251)
- Modify: `BackendConfiguration.Pn/Infrastructure/Data/Seed/Data/BackendConfigurationPermissionsSeedData.cs`
  (append an entry)
- Modify: `BackendConfiguration.Pn/Infrastructure/Data/Seed/Data/BackendConfigurationSeedData.cs` (append an entry)
- Test: `BackendConfiguration.Pn.Integration.Test/InboundMailSignatureTests.cs`

**Interfaces:**
- Consumes: from Phase 1A, `BackendConfigurationClaims.EnableInbox` and the inbox entities.
- Produces:
  - `InboundMailHubOptions { string HubUrl; string TenantSigningKey; string MailDomain }`
  - `static class InboundMailSignature { const string Scheme; string BodyHash(byte[]); string Canonical(string method, string path, int customerNo, string requestId, string date, string bodyHash); string Sign(string tenantKey, string canonical); bool FixedTimeEquals(string, string) }`
  - `interface ICustomerNoProvider { Task<int> GetAsync(); }`
  - `InboundMailRequestVerifier.Verify(string method, string path, IHeaderDictionary headers, byte[] body, int customerNo, DateTime nowUtc) : bool`

- [ ] **Step 1: Bump the base package** (the branch `feat/inbox-tenant-side` already exists off `stable`)

In `BackendConfiguration.Pn.csproj` line 282, change
`<PackageReference Include="Microting.EformBackendConfigurationBase" Version="10.0.53" />` to `10.0.54` (published by Phase 1A). Make the same change in
`BackendConfiguration.Pn.Integration.Test/BackendConfiguration.Pn.Integration.Test.csproj` and
`BackendConfiguration.Pn.Test/*.csproj` if they reference the package.

- [ ] **Step 2: Write the failing signature tests**

`BackendConfiguration.Pn.Integration.Test/InboundMailSignatureTests.cs`:

```csharp
using System.Text;
using BackendConfiguration.Pn.Infrastructure.Models.Settings;
using BackendConfiguration.Pn.Services.InboundMail;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace BackendConfiguration.Pn.Integration.Test;

[TestFixture]
public class InboundMailSignatureTests
{
    private const string TenantKey = "c25608a9e5271a2bafd480022de09ea60a11c7970566d3dca57034c662d7c767";
    private const string Path = "/api/backend-configuration-pn/inbox/hub/arrived";
    private const string Date = "Sun, 04 Oct 2026 10:00:00 GMT";
    private static readonly DateTime Now = new(2026, 10, 4, 10, 0, 30, DateTimeKind.Utc);
    private static readonly byte[] Body = Encoding.UTF8.GetBytes("{\"a\":1}");

    [Test]
    public void TestVector_MatchesContract()
    {
        var bodyHash = InboundMailSignature.BodyHash(Body);
        var canonical = InboundMailSignature.Canonical("POST", Path, 4711,
            "00000000-0000-0000-0000-000000000001", Date, bodyHash);

        Assert.That(bodyHash, Is.EqualTo("015abd7f5cc57a2dd94b7590f04ad8084273905ee33ec5cebeae62276a97f862"));
        Assert.That(InboundMailSignature.Sign(TenantKey, canonical),
            Is.EqualTo("be3548b960e7fd4b4dfdd72518c1f33d5d642a0312a36d5b4d7eb1cf2d833b1a"));
    }

    private static InboundMailRequestVerifier Verifier(string key = TenantKey) =>
        new(Options.Create(new InboundMailHubOptions { TenantSigningKey = key }), new MemoryCache(new MemoryCacheOptions()));

    private static HeaderDictionary Headers(string requestId = "00000000-0000-0000-0000-000000000001",
        string customerNo = "4711", string sig = "be3548b960e7fd4b4dfdd72518c1f33d5d642a0312a36d5b4d7eb1cf2d833b1a") => new()
    {
        ["Authorization"] = InboundMailSignature.Scheme + sig,
        ["Date"] = Date,
        ["X-Request-Id"] = requestId,
        ["X-Customer-No"] = customerNo
    };

    [Test]
    public void Verify_ValidRequest_True() =>
        Assert.That(Verifier().Verify("POST", Path, Headers(), Body, 4711, Now), Is.True);

    [Test]
    public void Verify_WrongCustomerNo_False() =>
        Assert.That(Verifier().Verify("POST", Path, Headers(customerNo: "4712"), Body, 4711, Now), Is.False);

    [Test]
    public void Verify_TamperedBody_False() =>
        Assert.That(Verifier().Verify("POST", Path, Headers(), Encoding.UTF8.GetBytes("{\"a\":2}"), 4711, Now), Is.False);

    [Test]
    public void Verify_OtherPath_False() =>
        Assert.That(Verifier().Verify("POST", "/api/backend-configuration-pn/inbox/hub/deliver", Headers(), Body, 4711, Now), Is.False);

    [Test]
    public void Verify_StaleDate_False() =>
        Assert.That(Verifier().Verify("POST", Path, Headers(), Body, 4711, Now.AddMinutes(3)), Is.False);

    [Test]
    public void Verify_ReplayedRequestId_False()
    {
        var v = Verifier();
        Assert.That(v.Verify("POST", Path, Headers(), Body, 4711, Now), Is.True);
        Assert.That(v.Verify("POST", Path, Headers(), Body, 4711, Now), Is.False);
    }

    [Test]
    public void Verify_NoKeyConfigured_False() =>
        Assert.That(Verifier(key: "").Verify("POST", Path, Headers(), Body, 4711, Now), Is.False);
}
```

- [ ] **Step 3: Build to verify it fails**

Run: `dotnet build eFormAPI/Plugins/BackendConfiguration.Pn/BackendConfiguration.Pn.sln`
Expected: FAIL with `CS0246` for `InboundMailSignature` and `InboundMailHubOptions`.

- [ ] **Step 4: Implement the options, signature, verifier and customer-number provider**

`Infrastructure/Models/Settings/InboundMailHubOptions.cs` (licence header as in `GoogleDriveOptions.cs`):

```csharp
namespace BackendConfiguration.Pn.Infrastructure.Models.Settings;

/// <summary>
/// Connection to the central inbound mail service. Empty TenantSigningKey → every
/// inbound hub call is refused (401). Empty HubUrl → outbound calls are skipped.
/// </summary>
public class InboundMailHubOptions
{
    public string HubUrl { get; set; } = "";
    public string TenantSigningKey { get; set; } = "";
    public string MailDomain { get; set; } = "indbakke.microting.dk";
}
```

`Services/InboundMail/InboundMailSignature.cs`:

```csharp
using System;
using System.Security.Cryptography;
using System.Text;

namespace BackendConfiguration.Pn.Services.InboundMail;

/// <summary>Request signature shared with the central inbound mail service (see the tenant-side spec).</summary>
public static class InboundMailSignature
{
    public const string Scheme = "HMAC-SHA256 ";

    public static string BodyHash(byte[] body) => Hex(SHA256.HashData(body));

    public static string Canonical(string method, string path, int customerNo, string requestId, string date, string bodyHash) =>
        $"{method.ToUpperInvariant()}\n{path}\n{customerNo}\n{requestId}\n{date}\n{bodyHash}";

    public static string Sign(string tenantKey, string canonical) =>
        Hex(HMACSHA256.HashData(Encoding.UTF8.GetBytes(tenantKey), Encoding.UTF8.GetBytes(canonical)));

    public static bool FixedTimeEquals(string a, string b) =>
        a.Length == b.Length &&
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));

    private static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();
}
```

`Services/InboundMail/InboundMailRequestVerifier.cs`:

```csharp
using System;
using System.Globalization;
using BackendConfiguration.Pn.Infrastructure.Models.Settings;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace BackendConfiguration.Pn.Services.InboundMail;

public class InboundMailRequestVerifier(IOptions<InboundMailHubOptions> options, IMemoryCache replayCache)
{
    private static readonly TimeSpan MaxSkew = TimeSpan.FromMinutes(2);

    public bool Verify(string method, string path, IHeaderDictionary headers, byte[] body, int customerNo, DateTime nowUtc)
    {
        var key = options.Value.TenantSigningKey;
        var auth = headers["Authorization"].ToString();
        var date = headers["Date"].ToString();
        var requestId = headers["X-Request-Id"].ToString();
        if (string.IsNullOrEmpty(key) || !auth.StartsWith(InboundMailSignature.Scheme, StringComparison.Ordinal)
            || !Guid.TryParse(requestId, out _)
            || headers["X-Customer-No"].ToString() != customerNo.ToString(CultureInfo.InvariantCulture))
            return false;

        if (!DateTime.TryParseExact(date, "R", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var sent)
            || (nowUtc - sent).Duration() > MaxSkew)
            return false;

        var expected = InboundMailSignature.Sign(key, InboundMailSignature.Canonical(method, path, customerNo,
            requestId, date, InboundMailSignature.BodyHash(body)));
        if (!InboundMailSignature.FixedTimeEquals(expected, auth[InboundMailSignature.Scheme.Length..].Trim()))
            return false;

        var cacheKey = "inbound-mail-request:" + requestId;
        if (replayCache.TryGetValue(cacheKey, out _)) return false;
        replayCache.Set(cacheKey, true, TimeSpan.FromMinutes(10));
        return true;
    }
}
```

`Services/InboundMail/CustomerNoProvider.cs`:

```csharp
using System.Threading.Tasks;
using Microting.eForm.Dto;
using Microting.eFormApi.BasePn.Abstractions;

namespace BackendConfiguration.Pn.Services.InboundMail;

public interface ICustomerNoProvider
{
    Task<int> GetAsync();
}

/// <summary>The tenant's customer number, from the SDK setting the host writes at startup.</summary>
public class CustomerNoProvider(IEFormCoreService coreService) : ICustomerNoProvider
{
    private int? _cached;

    public async Task<int> GetAsync()
    {
        if (_cached is { } n) return n;
        var core = await coreService.GetCore();
        _cached = int.Parse(await core.GetSdkSetting(Settings.customerNo));
        return _cached.Value;
    }
}
```

- [ ] **Step 5: Register the services, permission and setting seed**

In `EformBackendConfigurationPlugin.cs`, inside `ConfigureServices` after line 175
(`services.AddTransient<IBackendConfigurationFilesService, BackendConfigurationFilesService>();`):

```csharp
        services.AddMemoryCache();
        services.AddSingleton<Services.InboundMail.InboundMailRequestVerifier>();
        services.AddSingleton<Services.InboundMail.ICustomerNoProvider, Services.InboundMail.CustomerNoProvider>();
```

In the options method, next to the `GoogleDriveOptions` binding (line 251):

```csharp
        services.AddOptions<Infrastructure.Models.Settings.InboundMailHubOptions>()
            .Bind(configuration.GetSection("InboundMailHub"));
```

In `BackendConfigurationPermissionsSeedData.cs`, append to the array (after the `adhoc_enable` entry):

```csharp
        new PluginPermission
        {
            PermissionName = "Enable inbox",
            ClaimName = BackendConfigurationClaims.EnableInbox
        }
```

In `BackendConfigurationSeedData.cs`, append after `MaxCvrNumbers`:

```csharp
        new PluginConfigurationValue
        {
            Name = $"{TagBackendConfigurationSettingsName}:InboxUnknownSenderPolicy",
            Value = "hold"
        }
```

- [ ] **Step 6: Build**: `dotnet build eFormAPI/Plugins/BackendConfiguration.Pn/BackendConfiguration.Pn.sln` → `Build succeeded.`

- [ ] **Step 7: Review gate, then commit**

```bash
git add eFormAPI/Plugins/BackendConfiguration.Pn/BackendConfiguration.Pn/BackendConfiguration.Pn.csproj \
  eFormAPI/Plugins/BackendConfiguration.Pn/BackendConfiguration.Pn.Integration.Test/BackendConfiguration.Pn.Integration.Test.csproj \
  eFormAPI/Plugins/BackendConfiguration.Pn/BackendConfiguration.Pn/Infrastructure/Models/Settings/InboundMailHubOptions.cs \
  eFormAPI/Plugins/BackendConfiguration.Pn/BackendConfiguration.Pn/Services/InboundMail/InboundMailSignature.cs \
  eFormAPI/Plugins/BackendConfiguration.Pn/BackendConfiguration.Pn/Services/InboundMail/InboundMailRequestVerifier.cs \
  eFormAPI/Plugins/BackendConfiguration.Pn/BackendConfiguration.Pn/Services/InboundMail/CustomerNoProvider.cs \
  eFormAPI/Plugins/BackendConfiguration.Pn/BackendConfiguration.Pn/EformBackendConfigurationPlugin.cs \
  eFormAPI/Plugins/BackendConfiguration.Pn/BackendConfiguration.Pn/Infrastructure/Data/Seed/Data/BackendConfigurationPermissionsSeedData.cs \
  eFormAPI/Plugins/BackendConfiguration.Pn/BackendConfiguration.Pn/Infrastructure/Data/Seed/Data/BackendConfigurationSeedData.cs \
  eFormAPI/Plugins/BackendConfiguration.Pn/BackendConfiguration.Pn.Integration.Test/InboundMailSignatureTests.cs
git commit -m "feat(inbox): request signature, options and inbox_enable permission

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: FileArchiver (filing in one transaction) and the upload refactor

**Files:**
- Create: `Services/FileArchive/IArchiveStorage.cs`, `Services/FileArchive/CoreArchiveStorage.cs`,
  `Services/FileArchive/FileArchiver.cs`
- Modify: `Services/BackendConfigurationFilesService/BackendConfigurationFilesService.cs:320-409` (`Create`) and its
  constructor
- Modify: `EformBackendConfigurationPlugin.cs` (DI)
- Test: `BackendConfiguration.Pn.Integration.Test/FileArchiverTests.cs`

**Interfaces:**
- Produces:
  - `interface IArchiveStorage { Task PutAsync(string localPath, string objectName); Task<Stream?> GetAsync(string objectName); }`
  - `interface IFileArchiver { Task<int> ArchiveAsync(Stream content, string name, string extension, IReadOnlyCollection<int> propertyIds, IReadOnlyCollection<int> tagIds, int userId); }`.
    It returns the new `File.Id` and throws on failure, leaving no rows behind.
  - `static string FileArchiver.ObjectName(string md5, string extension)` → `"{md5}.{extension}"`.

- [ ] **Step 1: Write the failing tests**

`BackendConfiguration.Pn.Integration.Test/FileArchiverTests.cs`:

```csharp
using System.Text;
using BackendConfiguration.Pn.Services.FileArchive;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace BackendConfiguration.Pn.Integration.Test;

[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class FileArchiverTests : TestBaseSetup
{
    private IArchiveStorage _storage = null!;
    private FileArchiver _archiver = null!;

    [SetUp]
    public async Task SetUpArchiver()
    {
        BackendConfigurationPnDbContext!.FilesTags.RemoveRange(BackendConfigurationPnDbContext.FilesTags);
        BackendConfigurationPnDbContext.PropertyFiles.RemoveRange(BackendConfigurationPnDbContext.PropertyFiles);
        BackendConfigurationPnDbContext.UploadedDatas.RemoveRange(BackendConfigurationPnDbContext.UploadedDatas);
        BackendConfigurationPnDbContext.Files.RemoveRange(BackendConfigurationPnDbContext.Files);
        await BackendConfigurationPnDbContext.SaveChangesAsync();
        _storage = Substitute.For<IArchiveStorage>();
        _archiver = new FileArchiver(BackendConfigurationPnDbContext, _storage);
    }

    private static MemoryStream Pdf() => new(Encoding.ASCII.GetBytes("%PDF-1.7 test"));

    [Test]
    public async Task Archive_CreatesFilePropertiesTagsAndUploadedData()
    {
        var property = new Property { Name = "Nordvej 12", ItemPlanningTagId = 0, CreatedByUserId = 1, UpdatedByUserId = 1 };
        await property.Create(BackendConfigurationPnDbContext!);
        var tag = new FileTag { Name = "Ventilation", CreatedByUserId = 1, UpdatedByUserId = 1 };
        await tag.Create(BackendConfigurationPnDbContext);

        var fileId = await _archiver.ArchiveAsync(Pdf(), "Servicerapport VA-02", "pdf", [property.Id], [tag.Id], 7);

        var file = await BackendConfigurationPnDbContext.Files.AsNoTracking().SingleAsync(f => f.Id == fileId);
        Assert.That(file.FileName, Is.EqualTo("Servicerapport VA-02"));
        Assert.That(file.CreatedByUserId, Is.EqualTo(7));
        Assert.That(await BackendConfigurationPnDbContext.PropertyFiles.CountAsync(x => x.FileId == fileId && x.PropertyId == property.Id), Is.EqualTo(1));
        Assert.That(await BackendConfigurationPnDbContext.FilesTags.CountAsync(x => x.FileId == fileId && x.FileTagId == tag.Id), Is.EqualTo(1));
        var uploaded = await BackendConfigurationPnDbContext.UploadedDatas.AsNoTracking().SingleAsync(x => x.FileId == fileId);
        Assert.That(uploaded.Extension, Is.EqualTo("pdf"));
        Assert.That(uploaded.Checksum, Has.Length.EqualTo(32));
        await _storage.Received(1).PutAsync(Arg.Any<string>(), FileArchiver.ObjectName(uploaded.Checksum, "pdf"));
    }

    [Test]
    public void Archive_UploadFails_CreatesNoRows()
    {
        _storage.PutAsync(Arg.Any<string>(), Arg.Any<string>()).ThrowsAsync(new IOException("storage down"));

        Assert.ThrowsAsync<IOException>(() => _archiver.ArchiveAsync(Pdf(), "x", "pdf", [], [], 1));

        Assert.That(BackendConfigurationPnDbContext!.Files.Count(f => f.WorkflowState != Constants.WorkflowStates.Removed), Is.EqualTo(0));
        Assert.That(BackendConfigurationPnDbContext.UploadedDatas.Count(), Is.EqualTo(0));
    }
}
```

- [ ] **Step 2: Build to verify it fails**: `dotnet build …/BackendConfiguration.Pn.sln` (`FileArchiver` is missing)

- [ ] **Step 3: Implement**

`Services/FileArchive/IArchiveStorage.cs`:

```csharp
using System.IO;
using System.Threading.Tasks;

namespace BackendConfiguration.Pn.Services.FileArchive;

/// <summary>Seam over the SDK file storage (S3 or local) so filing can be tested without it.</summary>
public interface IArchiveStorage
{
    Task PutAsync(string localPath, string objectName);
    Task<Stream?> GetAsync(string objectName);
}
```

`Services/FileArchive/CoreArchiveStorage.cs`:

```csharp
using System.IO;
using System.Threading.Tasks;
using Microting.eFormApi.BasePn.Abstractions;

namespace BackendConfiguration.Pn.Services.FileArchive;

public class CoreArchiveStorage(IEFormCoreService coreService) : IArchiveStorage
{
    public async Task PutAsync(string localPath, string objectName)
    {
        var core = await coreService.GetCore();
        await core.PutFileToStorageSystem(localPath, objectName);
    }

    public async Task<Stream?> GetAsync(string objectName)
    {
        var core = await coreService.GetCore();
        var response = await core.GetFileFromS3Storage(objectName);
        return response?.ResponseStream;
    }
}
```

`Services/FileArchive/FileArchiver.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using File = Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.File;

namespace BackendConfiguration.Pn.Services.FileArchive;

public interface IFileArchiver
{
    Task<int> ArchiveAsync(Stream content, string name, string extension,
        IReadOnlyCollection<int> propertyIds, IReadOnlyCollection<int> tagIds, int userId);
}

/// <summary>
/// Puts one file into the PDF archive. Upload first, then File + PropertyFile +
/// FileTags + UploadedData in one transaction, so a storage failure leaves nothing
/// behind and a DB failure leaves only an orphan object (same md5 name next time).
/// </summary>
public class FileArchiver(BackendConfigurationPnDbContext dbContext, IArchiveStorage storage) : IFileArchiver
{
    public static string ObjectName(string md5, string extension) => $"{md5}.{extension}";

    public async Task<int> ArchiveAsync(Stream content, string name, string extension,
        IReadOnlyCollection<int> propertyIds, IReadOnlyCollection<int> tagIds, int userId)
    {
        extension = extension.TrimStart('.').ToLowerInvariant();
        var folder = Path.Combine(Path.GetTempPath(), "backend-configuration-files");
        Directory.CreateDirectory(folder);
        var tempName = $"{DateTime.UtcNow.Ticks}_{Guid.NewGuid():N}";
        var tempPath = Path.Combine(folder, $"{tempName}.{extension}");

        await using (var target = new FileStream(tempPath, FileMode.Create))
            await content.CopyToAsync(target);

        string md5;
        await using (var read = System.IO.File.OpenRead(tempPath))
            md5 = Convert.ToHexString(await MD5.HashDataAsync(read)).ToLowerInvariant();

        await storage.PutAsync(tempPath, ObjectName(md5, extension));

        var strategy = dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await dbContext.Database.BeginTransactionAsync();
            var file = new File { FileName = name, CreatedByUserId = userId, UpdatedByUserId = userId };
            await file.Create(dbContext);
            foreach (var propertyId in propertyIds)
                await new PropertyFile { FileId = file.Id, PropertyId = propertyId, CreatedByUserId = userId, UpdatedByUserId = userId }
                    .Create(dbContext);
            foreach (var tagId in tagIds)
                await new FileTags { FileId = file.Id, FileTagId = tagId, CreatedByUserId = userId, UpdatedByUserId = userId }
                    .Create(dbContext);
            await new UploadedData
            {
                FileId = file.Id, Extension = extension, FileName = tempName, Checksum = md5,
                FileLocation = tempPath, CreatedByUserId = userId, UpdatedByUserId = userId
            }.Create(dbContext);
            await tx.CommitAsync();
            return file.Id;
        });
    }
}
```

Note the deliberate change from today's `Create`: storage is written **before** the rows, so a storage error
never leaves a File without a stored object. The temp naming keeps the existing `{ticks}_…` shape.

- [ ] **Step 4: Route the existing upload through the archiver**

In `BackendConfigurationFilesService`:
- add the constructor parameter `IFileArchiver fileArchiver` and store it in `_fileArchiver`;
- replace the body of the `foreach` in `Create` (lines 325–398) with:

```csharp
			foreach (var fileCreate in model.Where(x => x.File != null))
			{
				var fileExtension = Path.GetExtension(fileCreate.File.FileName).TrimStart('.').ToLower();
				var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fileCreate.File.FileName);
				await using var stream = fileCreate.File.OpenReadStream();
				await _fileArchiver.ArchiveAsync(stream, fileNameWithoutExtension, fileExtension,
					fileCreate.PropertyIds, fileCreate.TagIds, _userService.UserId);
			}
```

Delete the `core` variable in `Create` if nothing else uses it. Add `using BackendConfiguration.Pn.Services.FileArchive;`.
Check the other constructors of this service: grep the Integration.Test project for
`new BackendConfigurationFilesService(` and pass `Substitute.For<IFileArchiver>()` where it is constructed.

Register in `EformBackendConfigurationPlugin.cs`, after the Task 1 lines:

```csharp
        services.AddTransient<Services.FileArchive.IArchiveStorage, Services.FileArchive.CoreArchiveStorage>();
        services.AddTransient<Services.FileArchive.IFileArchiver, Services.FileArchive.FileArchiver>();
```

- [ ] **Step 5: Build**, then **Step 6: review gate, commit `feat(files): file archiver uploads first and files in one transaction`.**

---

### Task 3: Hub-facing endpoints (arrived, catalog, deliver, failed)

**Files:**
- Create: `Infrastructure/Models/Inbox/HubContracts.cs`, `Services/InboundMail/SenderVerdictResolver.cs`,
  `Services/InboundMail/InboxHubService.cs`, `Controllers/InboxHubController.cs`
- Modify: `EformBackendConfigurationPlugin.cs` (DI)
- Test: `BackendConfiguration.Pn.Integration.Test/InboxTestData.cs`, `BackendConfiguration.Pn.Integration.Test/InboxHubServiceTests.cs`

**Interfaces:**
- Consumes: `IArchiveStorage`, `InboundMailRequestVerifier`, `ICustomerNoProvider`, the inbox entities.
- Produces:
  - wire DTOs, all `public record`s in `BackendConfiguration.Pn.Infrastructure.Models.Inbox`:
    - `ArrivedRequest(string HubDocumentId, string FromAddress, string? Subject, DateTime ReceivedAt, string FileName, long SizeBytes, string SpfResult, string DkimResult, DateTime? ReadyBy)`
    - `ArrivedResponse(string SenderVerdict)`
    - `CatalogProperty(int Id, string Name, string? Address)`, `CatalogTag(int Id, string Name)`,
      `CatalogResponse(List<CatalogProperty> Properties, List<CatalogTag> Tags)`
    - `DeliverSuggestion(string Kind, int TargetId, string Source, double Confidence, string? Evidence, int? Page, string? Reason)`
    - `DeliverMetadata(string HubDocumentId, int? PageCount, bool ReviewedByMicroting, List<DeliverSuggestion> Suggestions)`
    - `FailedRequest(string HubDocumentId, string Reason)`
    - `static JsonSerializerOptions HubJson.Options`
  - `SenderVerdictResolver.ResolveAsync(string fromAddress) : Task<string>`, returning "allowed", "unknown"
    or "blocked"
  - `IInboxHubService`:
    - `Task<ArrivedResponse> ArrivedAsync(ArrivedRequest r)`
    - `Task<CatalogResponse> CatalogAsync()`
    - `Task DeliverAsync(DeliverMetadata meta, Stream pdf, string fileName)`
    - `Task FailedAsync(FailedRequest r)`

- [ ] **Step 1: Write the test data helper and the failing tests**

`BackendConfiguration.Pn.Integration.Test/InboxTestData.cs`:

```csharp
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;

namespace BackendConfiguration.Pn.Integration.Test;

public static class InboxTestData
{
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

    public static async Task<InboxDocument> ReadyDocumentAsync(BackendConfigurationPnDbContext db, string md5 = "0123456789abcdef0123456789abcdef")
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
```

`BackendConfiguration.Pn.Integration.Test/InboxHubServiceTests.cs`:

```csharp
using System.Text;
using BackendConfiguration.Pn.Infrastructure.Models.Inbox;
using BackendConfiguration.Pn.Services.FileArchive;
using BackendConfiguration.Pn.Services.InboundMail;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microting.eFormApi.BasePn.Infrastructure.Database.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using NSubstitute;

namespace BackendConfiguration.Pn.Integration.Test;

[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class InboxHubServiceTests : TestBaseSetup
{
    private IArchiveStorage _storage = null!;
    private InboxHubService _service = null!;

    [SetUp]
    public async Task SetUpService()
    {
        await InboxTestData.ClearAsync(BackendConfigurationPnDbContext!);
        BaseDbContext!.Users.RemoveRange(BaseDbContext.Users.Where(u => u.Email!.EndsWith("@example.org")));
        await BaseDbContext.SaveChangesAsync();
        await SetPolicyAsync("hold");
        _storage = Substitute.For<IArchiveStorage>();
        _service = new InboxHubService(BackendConfigurationPnDbContext!, _storage,
            new SenderVerdictResolver(BackendConfigurationPnDbContext!, BaseDbContext!));
    }

    private async Task SetPolicyAsync(string value)
    {
        const string name = "BackendConfigurationSettings:InboxUnknownSenderPolicy";
        var row = await BackendConfigurationPnDbContext!.PluginConfigurationValues.SingleOrDefaultAsync(x => x.Name == name);
        if (row == null) BackendConfigurationPnDbContext.PluginConfigurationValues.Add(new PluginConfigurationValue { Name = name, Value = value });
        else row.Value = value;
        await BackendConfigurationPnDbContext.SaveChangesAsync();
    }

    private static ArrivedRequest Arrived(string from = "post@example.net", string? id = null) => new(
        id ?? Guid.NewGuid().ToString(), from, "Dokumenter", DateTime.UtcNow, "scan_0012.pdf", 1000,
        "not-checked", "not-checked", null);

    [Test]
    public async Task Verdict_UnknownSender_HoldPolicy_CreatesSenderPending()
    {
        var res = await _service.ArrivedAsync(Arrived());

        Assert.That(res.SenderVerdict, Is.EqualTo("unknown"));
        var doc = await BackendConfigurationPnDbContext!.InboxDocuments.AsNoTracking().SingleAsync();
        Assert.That(doc.Status, Is.EqualTo(InboxDocumentStatus.SenderPending));
    }

    [Test]
    public async Task Verdict_UnknownSender_RefusePolicy_BlockedNoRow()
    {
        await SetPolicyAsync("refuse");

        var res = await _service.ArrivedAsync(Arrived());

        Assert.That(res.SenderVerdict, Is.EqualTo("blocked"));
        Assert.That(await BackendConfigurationPnDbContext!.InboxDocuments.CountAsync(), Is.EqualTo(0));
    }

    [Test]
    public async Task Verdict_UserEmail_CaseInsensitive()
    {
        BaseDbContext!.Users.Add(new Microting.EformAngularFrontendBase.Infrastructure.Data.Entities.Users.EformUser
        {
            UserName = "jane.doe@example.org", Email = "jane.doe@example.org", FirstName = "Jane", LastName = "Doe"
        });
        await BaseDbContext.SaveChangesAsync();

        var res = await _service.ArrivedAsync(Arrived("Jane.Doe@example.org"));

        Assert.That(res.SenderVerdict, Is.EqualTo("allowed"));
        Assert.That((await BackendConfigurationPnDbContext!.InboxDocuments.SingleAsync()).Status, Is.EqualTo(InboxDocumentStatus.Preparing));
    }

    [Test]
    public async Task Verdict_DomainAllowRule_Allowed_ButBlockRuleWins()
    {
        await new InboxSenderRule { Pattern = "@example.net", Kind = InboxSenderRuleKind.Allow, CreatedByUserId = 1, UpdatedByUserId = 1 }.Create(BackendConfigurationPnDbContext!);
        Assert.That((await _service.ArrivedAsync(Arrived("post@example.net"))).SenderVerdict, Is.EqualTo("allowed"));

        await new InboxSenderRule { Pattern = "spam@example.net", Kind = InboxSenderRuleKind.Block, CreatedByUserId = 1, UpdatedByUserId = 1 }.Create(BackendConfigurationPnDbContext!);
        Assert.That((await _service.ArrivedAsync(Arrived("spam@example.net"))).SenderVerdict, Is.EqualTo("blocked"));
    }

    [Test]
    public async Task Arrived_Twice_SameRow()
    {
        var req = Arrived();
        await _service.ArrivedAsync(req);
        await _service.ArrivedAsync(req);
        Assert.That(await BackendConfigurationPnDbContext!.InboxDocuments.CountAsync(), Is.EqualTo(1));
    }

    [Test]
    public async Task Catalog_ListsLivePropertiesAndTags()
    {
        var p = await InboxTestData.PropertyAsync(BackendConfigurationPnDbContext!);
        var removed = await InboxTestData.PropertyAsync(BackendConfigurationPnDbContext!, "Gammel", "Gammelvej 1");
        await removed.Delete(BackendConfigurationPnDbContext!);
        var t = await InboxTestData.TagAsync(BackendConfigurationPnDbContext!);

        var catalog = await _service.CatalogAsync();

        Assert.That(catalog.Properties.Select(x => x.Id), Is.EquivalentTo(new[] { p.Id }));
        Assert.That(catalog.Properties[0].Address, Is.EqualTo("Nordvej 12, 8000 Aarhus C"));
        Assert.That(catalog.Tags.Select(x => x.Id), Is.EquivalentTo(new[] { t.Id }));
    }

    private async Task<InboxDocument> PreparingAsync()
    {
        var req = Arrived("post@example.net");
        await new InboxSenderRule { Pattern = "post@example.net", Kind = InboxSenderRuleKind.Allow, CreatedByUserId = 1, UpdatedByUserId = 1 }.Create(BackendConfigurationPnDbContext!);
        await _service.ArrivedAsync(req);
        return await BackendConfigurationPnDbContext!.InboxDocuments.SingleAsync(d => d.HubDocumentId == req.HubDocumentId);
    }

    [Test]
    public async Task Deliver_StoresPdfAndSuggestions_Ready()
    {
        var doc = await PreparingAsync();
        var p = await InboxTestData.PropertyAsync(BackendConfigurationPnDbContext!);
        var t = await InboxTestData.TagAsync(BackendConfigurationPnDbContext!);
        var meta = new DeliverMetadata(doc.HubDocumentId, 2, true,
        [
            new("property", p.Id, "textMatch", 0.5, "Adresse: Nordvej 12", 1, "Adressen står i dokumentet."),
            new("tag", t.Id, "textMatch", 0.5, "ventilation", 1, "“Ventilation” står i dokumentet.")
        ]);

        await _service.DeliverAsync(meta, new MemoryStream(Encoding.ASCII.GetBytes("%PDF-1.7 x")), "scan_0012.pdf");

        await BackendConfigurationPnDbContext!.Entry(doc).ReloadAsync();
        Assert.That(doc.Status, Is.EqualTo(InboxDocumentStatus.Ready));
        Assert.That(doc.ReviewedByMicroting, Is.True);
        Assert.That(doc.PageCount, Is.EqualTo(2));
        Assert.That(doc.Md5, Has.Length.EqualTo(32));
        Assert.That(await BackendConfigurationPnDbContext.InboxSuggestions.CountAsync(s => s.InboxDocumentId == doc.Id), Is.EqualTo(2));
        await _storage.Received(1).PutAsync(Arg.Any<string>(), $"{doc.Md5}.pdf");
    }

    [Test]
    public async Task Deliver_Twice_IsIdempotent()
    {
        var doc = await PreparingAsync();
        var meta = new DeliverMetadata(doc.HubDocumentId, 1, false, []);
        await _service.DeliverAsync(meta, new MemoryStream(Encoding.ASCII.GetBytes("%PDF-1.7 x")), "a.pdf");

        await _service.DeliverAsync(meta, new MemoryStream(Encoding.ASCII.GetBytes("%PDF-1.7 x")), "a.pdf");

        await _storage.Received(1).PutAsync(Arg.Any<string>(), Arg.Any<string>());
    }

    [Test]
    public async Task Deliver_DropsSuggestionsForDeletedTargets()
    {
        var doc = await PreparingAsync();
        var tag = await InboxTestData.TagAsync(BackendConfigurationPnDbContext!);
        await tag.Delete(BackendConfigurationPnDbContext!);

        await _service.DeliverAsync(new DeliverMetadata(doc.HubDocumentId, 1, false,
            [new("tag", tag.Id, "textMatch", 0.5, null, 1, null), new("property", 999999, "textMatch", 0.5, null, 1, null)]),
            new MemoryStream(Encoding.ASCII.GetBytes("%PDF-1.7 x")), "a.pdf");

        Assert.That(await BackendConfigurationPnDbContext!.InboxSuggestions.CountAsync(), Is.EqualTo(0));
    }

    [Test]
    public async Task Deliver_UnknownHubDocument_CreatesRow()
    {
        var id = Guid.NewGuid().ToString();

        await _service.DeliverAsync(new DeliverMetadata(id, 1, false, []), new MemoryStream(Encoding.ASCII.GetBytes("%PDF-1.7 x")), "lost.pdf");

        var doc = await BackendConfigurationPnDbContext!.InboxDocuments.SingleAsync(d => d.HubDocumentId == id);
        Assert.That(doc.Status, Is.EqualTo(InboxDocumentStatus.Ready));
        Assert.That(doc.FileName, Is.EqualTo("lost.pdf"));
    }

    [Test]
    public async Task Failed_SetsFailedWithReason()
    {
        var doc = await PreparingAsync();

        await _service.FailedAsync(new FailedRequest(doc.HubDocumentId, "PDF-filen er beskyttet med adgangskode."));

        await BackendConfigurationPnDbContext!.Entry(doc).ReloadAsync();
        Assert.That(doc.Status, Is.EqualTo(InboxDocumentStatus.Failed));
        Assert.That(doc.FailureReason, Is.EqualTo("PDF-filen er beskyttet med adgangskode."));
    }
}
```

Check two names in `TestBaseSetup` before writing the user insert. First, confirm `BaseDbContext` is the
exposed property name; grep `TestBaseSetup.cs` for `BaseDbContext`. Second, confirm the user entity type that
`BaseDbContext.Users` is declared with; grep it for `Users`. Use exactly those names. If `TestBaseSetup` has
no Angular DB context, add one by following its existing `MicrotingDbContext` pattern, pointed at the
`*_Angular` test database.

- [ ] **Step 2: Build to verify it fails**: `dotnet build …/BackendConfiguration.Pn.sln`

- [ ] **Step 3: Implement the contracts**

`Infrastructure/Models/Inbox/HubContracts.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace BackendConfiguration.Pn.Infrastructure.Models.Inbox;

public record ArrivedRequest(string HubDocumentId, string FromAddress, string? Subject, DateTime ReceivedAt,
    string FileName, long SizeBytes, string SpfResult, string DkimResult, DateTime? ReadyBy);
public record ArrivedResponse(string SenderVerdict);
public record CatalogProperty(int Id, string Name, string? Address);
public record CatalogTag(int Id, string Name);
public record CatalogResponse(List<CatalogProperty> Properties, List<CatalogTag> Tags);
public record DeliverSuggestion(string Kind, int TargetId, string Source, double Confidence, string? Evidence, int? Page, string? Reason);
public record DeliverMetadata(string HubDocumentId, int? PageCount, bool ReviewedByMicroting, List<DeliverSuggestion> Suggestions);
public record FailedRequest(string HubDocumentId, string Reason);

public static class HubJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
```

Enums travel as camelCase strings (`"property"`, `"textMatch"`) and are mapped by hand, case-insensitively, in the service. That
keeps the contract explicit.

- [ ] **Step 4: Implement the verdict resolver**

`Services/InboundMail/SenderVerdictResolver.cs`:

```csharp
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Microting.EformAngularFrontendBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;

namespace BackendConfiguration.Pn.Services.InboundMail;

/// <summary>Block rule → blocked; user login email or Allow rule → allowed; else the unknown-sender policy.</summary>
public class SenderVerdictResolver(BackendConfigurationPnDbContext dbContext, BaseDbContext baseDbContext)
{
    public const string PolicyName = "BackendConfigurationSettings:InboxUnknownSenderPolicy";

    public async Task<string> ResolveAsync(string fromAddress)
    {
        var address = fromAddress.Trim().ToLowerInvariant();
        var domain = address.Contains('@') ? address[address.IndexOf('@')..] : "";
        var rules = await dbContext.InboxSenderRules
            .Where(r => r.WorkflowState != Constants.WorkflowStates.Removed)
            .Select(r => new { Pattern = r.Pattern.ToLower(), r.Kind }).ToListAsync();

        if (rules.Any(r => r.Kind == InboxSenderRuleKind.Block && (r.Pattern == address || r.Pattern == domain)))
            return "blocked";
        if (await baseDbContext.Users.AnyAsync(u => u.Email != null && u.Email.ToLower() == address))
            return "allowed";
        if (rules.Any(r => r.Kind == InboxSenderRuleKind.Allow && (r.Pattern == address || r.Pattern == domain)))
            return "allowed";

        var policy = await dbContext.PluginConfigurationValues
            .Where(x => x.Name == PolicyName).Select(x => x.Value).FirstOrDefaultAsync();
        return policy == "refuse" ? "blocked" : "unknown";
    }
}
```

Check `BaseDbContext`'s namespace by grepping how `BackendConfigurationFilesService` imports `BaseDbContext`,
and use the same `using`.

- [ ] **Step 5: Implement the service**

`Services/InboundMail/InboxHubService.cs`:

```csharp
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Models.Inbox;
using BackendConfiguration.Pn.Services.FileArchive;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;

namespace BackendConfiguration.Pn.Services.InboundMail;

public interface IInboxHubService
{
    Task<ArrivedResponse> ArrivedAsync(ArrivedRequest r);
    Task<CatalogResponse> CatalogAsync();
    Task DeliverAsync(DeliverMetadata meta, Stream pdf, string fileName);
    Task FailedAsync(FailedRequest r);
}

public class InboxHubService(BackendConfigurationPnDbContext dbContext, IArchiveStorage storage,
    SenderVerdictResolver verdicts) : IInboxHubService
{
    private const int SystemUserId = 0;

    public async Task<ArrivedResponse> ArrivedAsync(ArrivedRequest r)
    {
        var existing = await dbContext.InboxDocuments.FirstOrDefaultAsync(d => d.HubDocumentId == r.HubDocumentId);
        if (existing != null)
            return new ArrivedResponse(existing.Status switch
            {
                InboxDocumentStatus.SenderPending => "unknown",
                InboxDocumentStatus.Rejected => "blocked",
                _ => "allowed"
            });

        var verdict = await verdicts.ResolveAsync(r.FromAddress);
        if (verdict == "blocked") return new ArrivedResponse(verdict);

        await new InboxDocument
        {
            HubDocumentId = r.HubDocumentId,
            FromAddress = Cut(r.FromAddress, 254),
            Subject = r.Subject == null ? null : Cut(r.Subject, 500),
            ReceivedAt = r.ReceivedAt,
            ReadyBy = r.ReadyBy,
            FileName = Cut(r.FileName, 250),
            SizeBytes = r.SizeBytes,
            SpfResult = Cut(r.SpfResult, 20),
            DkimResult = Cut(r.DkimResult, 20),
            Status = verdict == "unknown" ? InboxDocumentStatus.SenderPending : InboxDocumentStatus.Preparing,
            CreatedByUserId = SystemUserId,
            UpdatedByUserId = SystemUserId
        }.Create(dbContext);
        return new ArrivedResponse(verdict);
    }

    public async Task<CatalogResponse> CatalogAsync()
    {
        var properties = await dbContext.Properties
            .Where(p => p.WorkflowState != Constants.WorkflowStates.Removed)
            .Select(p => new CatalogProperty(p.Id, p.Name, p.Address)).ToListAsync();
        var tags = await dbContext.FileTags
            .Where(t => t.WorkflowState != Constants.WorkflowStates.Removed)
            .Select(t => new CatalogTag(t.Id, t.Name)).ToListAsync();
        return new CatalogResponse(properties, tags);
    }

    public async Task DeliverAsync(DeliverMetadata meta, Stream pdf, string fileName)
    {
        var doc = await dbContext.InboxDocuments.FirstOrDefaultAsync(d => d.HubDocumentId == meta.HubDocumentId);
        if (doc is { Status: InboxDocumentStatus.Ready or InboxDocumentStatus.Filed or InboxDocumentStatus.Rejected })
            return; // the central service retried after a timeout

        var folder = Path.Combine(Path.GetTempPath(), "backend-configuration-inbox");
        Directory.CreateDirectory(folder);
        var tempPath = Path.Combine(folder, $"{Guid.NewGuid():N}.pdf");
        try
        {
            await using (var target = new FileStream(tempPath, FileMode.Create)) await pdf.CopyToAsync(target);
            string md5;
            await using (var read = File.OpenRead(tempPath))
                md5 = Convert.ToHexString(await MD5.HashDataAsync(read)).ToLowerInvariant();
            await storage.PutAsync(tempPath, FileArchiver.ObjectName(md5, "pdf"));

            if (doc == null)
            {
                doc = new InboxDocument
                {
                    HubDocumentId = meta.HubDocumentId, FromAddress = "ukendt", ReceivedAt = DateTime.UtcNow,
                    FileName = Cut(fileName, 250), CreatedByUserId = SystemUserId, UpdatedByUserId = SystemUserId,
                    Status = InboxDocumentStatus.Preparing
                };
                await doc.Create(dbContext);
            }

            var propertyIds = await dbContext.Properties.Where(p => p.WorkflowState != Constants.WorkflowStates.Removed).Select(p => p.Id).ToListAsync();
            var tagIds = await dbContext.FileTags.Where(t => t.WorkflowState != Constants.WorkflowStates.Removed).Select(t => t.Id).ToListAsync();
            foreach (var s in meta.Suggestions)
            {
                var kind = string.Equals(s.Kind, "tag", StringComparison.OrdinalIgnoreCase) ? InboxSuggestionKind.Tag : InboxSuggestionKind.Property;
                if (!(kind == InboxSuggestionKind.Tag ? tagIds : propertyIds).Contains(s.TargetId)) continue;
                await new InboxSuggestion
                {
                    InboxDocumentId = doc.Id, Kind = kind, TargetId = s.TargetId,
                    Source = s.Source?.ToLowerInvariant() switch { "ai" => InboxSuggestionSource.Ai, "reviewer" => InboxSuggestionSource.Reviewer, _ => InboxSuggestionSource.TextMatch },
                    Confidence = Math.Clamp(s.Confidence, 0, 1),
                    Evidence = s.Evidence == null ? null : Cut(s.Evidence, 250),
                    Page = s.Page,
                    Reason = s.Reason == null ? null : Cut(s.Reason, 500),
                    CreatedByUserId = SystemUserId, UpdatedByUserId = SystemUserId
                }.Create(dbContext);
            }

            doc.Md5 = md5;
            doc.PageCount = meta.PageCount;
            doc.ReviewedByMicroting = meta.ReviewedByMicroting;
            doc.DeliveredAt = DateTime.UtcNow;
            doc.Status = InboxDocumentStatus.Ready;
            await doc.Update(dbContext);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    public async Task FailedAsync(FailedRequest r)
    {
        var doc = await dbContext.InboxDocuments.FirstOrDefaultAsync(d => d.HubDocumentId == r.HubDocumentId);
        if (doc == null || doc.Status != InboxDocumentStatus.Preparing) return;
        doc.Status = InboxDocumentStatus.Failed;
        doc.FailureReason = Cut(r.Reason, 500);
        await doc.Update(dbContext);
    }

    private static string Cut(string s, int max) => s.Length > max ? s[..max] : s;
}
```

`System.IO.File` and the entity `File` clash, so use `System.IO.File.OpenRead` / `System.IO.File.Exists` /
`System.IO.File.Delete` explicitly if the compiler reports an ambiguity.

- [ ] **Step 6: Implement the controller**

`Controllers/InboxHubController.cs`:

```csharp
using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Models.Inbox;
using BackendConfiguration.Pn.Services.InboundMail;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BackendConfiguration.Pn.Controllers;

/// <summary>
/// Called only by the central inbound mail service. Anonymous at the auth layer;
/// every request must carry a valid signature (see the tenant-side spec).
/// </summary>
[AllowAnonymous]
[Route("api/backend-configuration-pn/inbox/hub")]
public class InboxHubController(IInboxHubService service, InboundMailRequestVerifier verifier,
    ICustomerNoProvider customerNo) : Controller
{
    private async Task<byte[]?> ReadVerifiedBodyAsync()
    {
        Request.EnableBuffering();
        using var ms = new MemoryStream();
        await Request.Body.CopyToAsync(ms);
        Request.Body.Position = 0;
        var body = ms.ToArray();
        return verifier.Verify(Request.Method, Request.Path.Value ?? "", Request.Headers, body,
            await customerNo.GetAsync(), DateTime.UtcNow) ? body : null;
    }

    private ContentResult Json(object value) =>
        Content(JsonSerializer.Serialize(value, HubJson.Options), "application/json");

    [HttpPost("arrived")]
    public async Task<IActionResult> Arrived()
    {
        if (await ReadVerifiedBodyAsync() is not { } body) return Unauthorized();
        var req = JsonSerializer.Deserialize<ArrivedRequest>(body, HubJson.Options);
        return req == null ? BadRequest() : Json(await service.ArrivedAsync(req));
    }

    [HttpGet("catalog")]
    public async Task<IActionResult> Catalog()
    {
        if (await ReadVerifiedBodyAsync() is null) return Unauthorized();
        return Json(await service.CatalogAsync());
    }

    [HttpPost("deliver")]
    [RequestSizeLimit(31_457_280)]
    public async Task<IActionResult> Deliver()
    {
        if (await ReadVerifiedBodyAsync() is null) return Unauthorized();
        var form = await Request.ReadFormAsync();
        var file = form.Files.GetFile("file");
        var meta = JsonSerializer.Deserialize<DeliverMetadata>(form["metadata"].ToString(), HubJson.Options);
        if (file == null || meta == null) return BadRequest();
        await using var stream = file.OpenReadStream();
        await service.DeliverAsync(meta, stream, file.FileName);
        return NoContent();
    }

    [HttpPost("failed")]
    public async Task<IActionResult> Failed()
    {
        if (await ReadVerifiedBodyAsync() is not { } body) return Unauthorized();
        var req = JsonSerializer.Deserialize<FailedRequest>(body, HubJson.Options);
        if (req == null) return BadRequest();
        await service.FailedAsync(req);
        return NoContent();
    }
}
```

The hub sends the metadata part as `application/json`. `form["metadata"]` returns its text, because ASP.NET
binds non-file parts as form values. If the part arrives with a file name and lands in `form.Files` instead,
read it with `form.Files.GetFile("metadata")`, and keep the test `Deliver_*` passing through the service.

Register in `EformBackendConfigurationPlugin.cs`:

```csharp
        services.AddTransient<Services.InboundMail.SenderVerdictResolver>();
        services.AddTransient<Services.InboundMail.IInboxHubService, Services.InboundMail.InboxHubService>();
```

- [ ] **Step 7: Build**, then **Step 8: review gate, commit `feat(inbox): signed endpoints for the central inbound mail service`.**

---

### Task 4: Manager endpoints (list, details, file, undo, reject, PDF)

**Files:**
- Create: `Infrastructure/Models/Inbox/InboxModels.cs`, `Services/InboundMail/InboxService.cs`, `Controllers/InboxController.cs`
- Modify: `EformBackendConfigurationPlugin.cs` (DI)
- Test: `BackendConfiguration.Pn.Integration.Test/InboxServiceTests.cs`

**Interfaces:**
- Consumes: `IFileArchiver`, `IArchiveStorage`, `InboxTestData`.
- Produces the UI DTOs, as camelCase JSON through the host serializer; enums are ints:
  - `InboxListItem { int Id; string FileName; string? Subject; string FromAddress; DateTime ReceivedAt; DateTime? ReadyBy; int Status; string? FailureReason; bool ReviewedByMicroting; List<InboxSuggestionModel> Suggestions }`
  - `InboxSuggestionModel { int Id; int Kind; int TargetId; string TargetName; int Source; double Confidence; string? Evidence; int? Page; string? Reason }`
  - `FileInboxDocumentRequest { string Name; List<int> PropertyIds; List<int> TagIds }`
- Produces `IInboxService`:
  - `Task<OperationDataResult<List<InboxListItem>>> ListAsync(int? status, string? search)`
  - `Task<OperationDataResult<InboxListItem>> GetAsync(int id)`
  - `Task<Stream?> GetPdfAsync(int id)`
  - `Task<OperationResult> FileAsync(int id, FileInboxDocumentRequest req, int userId)`
  - `Task<OperationResult> UndoAsync(int id, int userId)`
  - `Task<OperationResult> RejectAsync(int id, int userId)`
- Routes, under `api/backend-configuration-pn/inbox`:
  - `GET ?status=&search=`, `GET {id}`, `GET {id}/file`
  - `POST {id}/file`, `POST {id}/undo`, `POST {id}/reject`

- [ ] **Step 1: Write the failing tests**

`BackendConfiguration.Pn.Integration.Test/InboxServiceTests.cs`:

```csharp
using System.Text;
using BackendConfiguration.Pn.Infrastructure.Models.Inbox;
using BackendConfiguration.Pn.Services.FileArchive;
using BackendConfiguration.Pn.Services.InboundMail;
using Microsoft.EntityFrameworkCore;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using NSubstitute;

namespace BackendConfiguration.Pn.Integration.Test;

[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class InboxServiceTests : TestBaseSetup
{
    private IArchiveStorage _storage = null!;
    private InboxService _service = null!;

    [SetUp]
    public async Task SetUpService()
    {
        await InboxTestData.ClearAsync(BackendConfigurationPnDbContext!);
        _storage = Substitute.For<IArchiveStorage>();
        _storage.GetAsync(Arg.Any<string>()).Returns(_ => Task.FromResult<Stream?>(new MemoryStream(Encoding.ASCII.GetBytes("%PDF-1.7 x"))));
        _service = new InboxService(BackendConfigurationPnDbContext!, _storage,
            new FileArchiver(BackendConfigurationPnDbContext!, _storage), new BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService.BackendConfigurationLocalizationService());
    }

    [Test]
    public async Task List_ShowsLiveDocumentsWithSuggestionNames()
    {
        var doc = await InboxTestData.ReadyDocumentAsync(BackendConfigurationPnDbContext!);
        var p = await InboxTestData.PropertyAsync(BackendConfigurationPnDbContext!);
        await new InboxSuggestion { InboxDocumentId = doc.Id, Kind = InboxSuggestionKind.Property, TargetId = p.Id, Confidence = 0.5, CreatedByUserId = 0, UpdatedByUserId = 0 }.Create(BackendConfigurationPnDbContext!);

        var res = await _service.ListAsync(null, null);

        Assert.That(res.Success, Is.True);
        var item = res.Model.Single();
        Assert.That(item.Status, Is.EqualTo((int)InboxDocumentStatus.Ready));
        Assert.That(item.Suggestions.Single().TargetName, Is.EqualTo("Nordvej 12"));
    }

    [Test]
    public async Task List_Search_MatchesFileNameOrSender()
    {
        await InboxTestData.ReadyDocumentAsync(BackendConfigurationPnDbContext!);
        Assert.That((await _service.ListAsync(null, "servicerapport")).Model, Has.Count.EqualTo(1));
        Assert.That((await _service.ListAsync(null, "example.org")).Model, Has.Count.EqualTo(1));
        Assert.That((await _service.ListAsync(null, "faktura")).Model, Is.Empty);
    }

    [Test]
    public async Task File_CreatesArchiveFile_MarksFiledAndAccepted()
    {
        var doc = await InboxTestData.ReadyDocumentAsync(BackendConfigurationPnDbContext!);
        var p = await InboxTestData.PropertyAsync(BackendConfigurationPnDbContext!);
        var t = await InboxTestData.TagAsync(BackendConfigurationPnDbContext!);
        var other = await InboxTestData.TagAsync(BackendConfigurationPnDbContext!, "Brand");
        await new InboxSuggestion { InboxDocumentId = doc.Id, Kind = InboxSuggestionKind.Tag, TargetId = t.Id, Confidence = 0.5, CreatedByUserId = 0, UpdatedByUserId = 0 }.Create(BackendConfigurationPnDbContext!);
        await new InboxSuggestion { InboxDocumentId = doc.Id, Kind = InboxSuggestionKind.Tag, TargetId = other.Id, Confidence = 0.3, CreatedByUserId = 0, UpdatedByUserId = 0 }.Create(BackendConfigurationPnDbContext!);

        var res = await _service.FileAsync(doc.Id, new FileInboxDocumentRequest { Name = "Servicerapport VA-02", PropertyIds = [p.Id], TagIds = [t.Id] }, 7);

        Assert.That(res.Success, Is.True);
        await BackendConfigurationPnDbContext!.Entry(doc).ReloadAsync();
        Assert.That(doc.Status, Is.EqualTo(InboxDocumentStatus.Filed));
        Assert.That(doc.FiledByUserId, Is.EqualTo(7));
        var file = await BackendConfigurationPnDbContext.Files.AsNoTracking().SingleAsync(f => f.Id == doc.FiledFileId);
        Assert.That(file.FileName, Is.EqualTo("Servicerapport VA-02"));
        var accepted = await BackendConfigurationPnDbContext.InboxSuggestions.AsNoTracking().ToDictionaryAsync(s => s.TargetId, s => s.Accepted);
        Assert.That(accepted[t.Id], Is.True);
        Assert.That(accepted[other.Id], Is.False);
    }

    [Test]
    public async Task File_NoProperty_IsRejected()
    {
        var doc = await InboxTestData.ReadyDocumentAsync(BackendConfigurationPnDbContext!);

        var res = await _service.FileAsync(doc.Id, new FileInboxDocumentRequest { Name = "x", PropertyIds = [], TagIds = [] }, 7);

        Assert.That(res.Success, Is.False);
        await BackendConfigurationPnDbContext!.Entry(doc).ReloadAsync();
        Assert.That(doc.Status, Is.EqualTo(InboxDocumentStatus.Ready));
    }

    [Test]
    public async Task File_UnknownPropertyId_IsRejected()
    {
        var doc = await InboxTestData.ReadyDocumentAsync(BackendConfigurationPnDbContext!);
        var res = await _service.FileAsync(doc.Id, new FileInboxDocumentRequest { Name = "x", PropertyIds = [999999], TagIds = [] }, 7);
        Assert.That(res.Success, Is.False);
        Assert.That(await BackendConfigurationPnDbContext!.Files.CountAsync(), Is.EqualTo(0));
    }

    [Test]
    public async Task File_NotReady_IsRejected()
    {
        var doc = await InboxTestData.ReadyDocumentAsync(BackendConfigurationPnDbContext!);
        doc.Status = InboxDocumentStatus.SenderPending;
        await doc.Update(BackendConfigurationPnDbContext!);
        var p = await InboxTestData.PropertyAsync(BackendConfigurationPnDbContext!);

        var res = await _service.FileAsync(doc.Id, new FileInboxDocumentRequest { Name = "x", PropertyIds = [p.Id], TagIds = [] }, 7);

        Assert.That(res.Success, Is.False);
    }

    [Test]
    public async Task Undo_WithinTenMinutes_RemovesFileAndReopens()
    {
        var doc = await InboxTestData.ReadyDocumentAsync(BackendConfigurationPnDbContext!);
        var p = await InboxTestData.PropertyAsync(BackendConfigurationPnDbContext!);
        await _service.FileAsync(doc.Id, new FileInboxDocumentRequest { Name = "x", PropertyIds = [p.Id], TagIds = [] }, 7);

        var res = await _service.UndoAsync(doc.Id, 7);

        Assert.That(res.Success, Is.True);
        await BackendConfigurationPnDbContext!.Entry(doc).ReloadAsync();
        Assert.That(doc.Status, Is.EqualTo(InboxDocumentStatus.Ready));
        Assert.That(doc.FiledFileId, Is.Null);
        Assert.That(await BackendConfigurationPnDbContext.Files.CountAsync(f => f.WorkflowState != "removed"), Is.EqualTo(0));
    }

    [Test]
    public async Task Undo_AfterTenMinutes_IsRejected()
    {
        var doc = await InboxTestData.ReadyDocumentAsync(BackendConfigurationPnDbContext!);
        var p = await InboxTestData.PropertyAsync(BackendConfigurationPnDbContext!);
        await _service.FileAsync(doc.Id, new FileInboxDocumentRequest { Name = "x", PropertyIds = [p.Id], TagIds = [] }, 7);
        await BackendConfigurationPnDbContext!.Entry(doc).ReloadAsync();
        doc.FiledAt = DateTime.UtcNow.AddMinutes(-11);
        await doc.Update(BackendConfigurationPnDbContext);

        Assert.That((await _service.UndoAsync(doc.Id, 7)).Success, Is.False);
    }

    [Test]
    public async Task Reject_SetsRejected()
    {
        var doc = await InboxTestData.ReadyDocumentAsync(BackendConfigurationPnDbContext!);
        Assert.That((await _service.RejectAsync(doc.Id, 7)).Success, Is.True);
        await BackendConfigurationPnDbContext!.Entry(doc).ReloadAsync();
        Assert.That(doc.Status, Is.EqualTo(InboxDocumentStatus.Rejected));
    }
}
```

- [ ] **Step 2: Build to verify it fails**: `dotnet build …/BackendConfiguration.Pn.sln`

- [ ] **Step 3: Implement the models**

`Infrastructure/Models/Inbox/InboxModels.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace BackendConfiguration.Pn.Infrastructure.Models.Inbox;

public class InboxSuggestionModel
{
    public int Id { get; set; }
    public int Kind { get; set; }
    public int TargetId { get; set; }
    public string TargetName { get; set; } = "";
    public int Source { get; set; }
    public double Confidence { get; set; }
    public string? Evidence { get; set; }
    public int? Page { get; set; }
    public string? Reason { get; set; }
}

public class InboxListItem
{
    public int Id { get; set; }
    public string FileName { get; set; } = "";
    public string? Subject { get; set; }
    public string FromAddress { get; set; } = "";
    public DateTime ReceivedAt { get; set; }
    public DateTime? ReadyBy { get; set; }
    public int Status { get; set; }
    public string? FailureReason { get; set; }
    public bool ReviewedByMicroting { get; set; }
    public List<InboxSuggestionModel> Suggestions { get; set; } = new();
}

public class FileInboxDocumentRequest
{
    public string Name { get; set; } = "";
    public List<int> PropertyIds { get; set; } = new();
    public List<int> TagIds { get; set; } = new();
}

public class InboxSenderRuleModel
{
    public int? Id { get; set; }
    public string Pattern { get; set; } = "";
    /// <summary>0 = Allow, 1 = Block.</summary>
    public int Kind { get; set; }
}

public class InboxSettingsModel
{
    public string? Address { get; set; }
    /// <summary>"hold" or "refuse".</summary>
    public string UnknownSenderPolicy { get; set; } = "hold";
    public List<InboxSenderRuleModel> SenderRules { get; set; } = new();
}
```

- [ ] **Step 4: Implement the service**

`Services/InboundMail/InboxService.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Models.Inbox;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using BackendConfiguration.Pn.Services.FileArchive;
using Microsoft.EntityFrameworkCore;
using Microting.eForm.Infrastructure.Constants;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;

namespace BackendConfiguration.Pn.Services.InboundMail;

public interface IInboxService
{
    Task<OperationDataResult<List<InboxListItem>>> ListAsync(int? status, string? search);
    Task<OperationDataResult<InboxListItem>> GetAsync(int id);
    Task<Stream?> GetPdfAsync(int id);
    Task<OperationResult> FileAsync(int id, FileInboxDocumentRequest req, int userId);
    Task<OperationResult> UndoAsync(int id, int userId);
    Task<OperationResult> RejectAsync(int id, int userId);
}

public class InboxService(BackendConfigurationPnDbContext dbContext, IArchiveStorage storage, IFileArchiver archiver,
    IBackendConfigurationLocalizationService localization) : IInboxService
{
    private static readonly TimeSpan UndoWindow = TimeSpan.FromMinutes(10);

    public async Task<OperationDataResult<List<InboxListItem>>> ListAsync(int? status, string? search)
    {
        var query = dbContext.InboxDocuments.Where(d => d.WorkflowState != Constants.WorkflowStates.Removed);
        if (status is { } s) query = query.Where(d => (int)d.Status == s);
        else query = query.Where(d => d.Status != InboxDocumentStatus.Rejected
                                      && (d.Status != InboxDocumentStatus.Filed || d.FiledAt > DateTime.UtcNow.AddDays(-7)));
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(d => d.FileName.ToLower().Contains(term) || d.FromAddress.ToLower().Contains(term)
                                     || (d.Subject != null && d.Subject.ToLower().Contains(term)));
        }
        var docs = await query.OrderByDescending(d => d.ReceivedAt).Take(500)
            .Include(d => d.Suggestions).AsNoTracking().ToListAsync();
        return new OperationDataResult<List<InboxListItem>>(true, await MapAsync(docs));
    }

    public async Task<OperationDataResult<InboxListItem>> GetAsync(int id)
    {
        var doc = await dbContext.InboxDocuments.Include(d => d.Suggestions).AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == id && d.WorkflowState != Constants.WorkflowStates.Removed);
        return doc == null
            ? new OperationDataResult<InboxListItem>(false, localization.GetString("InboxDocumentNotFound"))
            : new OperationDataResult<InboxListItem>(true, (await MapAsync([doc])).Single());
    }

    public async Task<Stream?> GetPdfAsync(int id)
    {
        var md5 = await dbContext.InboxDocuments.Where(d => d.Id == id).Select(d => d.Md5).FirstOrDefaultAsync();
        return md5 == null ? null : await storage.GetAsync(FileArchiver.ObjectName(md5, "pdf"));
    }

    public async Task<OperationResult> FileAsync(int id, FileInboxDocumentRequest req, int userId)
    {
        var doc = await dbContext.InboxDocuments.Include(d => d.Suggestions).FirstOrDefaultAsync(d => d.Id == id);
        if (doc is not { Status: InboxDocumentStatus.Ready } || doc.Md5 == null)
            return new OperationResult(false, localization.GetString("InboxDocumentNotReady"));
        if (req.PropertyIds.Count == 0)
            return new OperationResult(false, localization.GetString("InboxChooseAtLeastOneProperty"));

        var liveProperties = await dbContext.Properties.Where(p => req.PropertyIds.Contains(p.Id)
            && p.WorkflowState != Constants.WorkflowStates.Removed).CountAsync();
        var liveTags = await dbContext.FileTags.Where(t => req.TagIds.Contains(t.Id)
            && t.WorkflowState != Constants.WorkflowStates.Removed).CountAsync();
        if (liveProperties != req.PropertyIds.Distinct().Count() || liveTags != req.TagIds.Distinct().Count())
            return new OperationResult(false, localization.GetString("InboxUnknownPropertyOrTag"));

        await using var pdf = await storage.GetAsync(FileArchiver.ObjectName(doc.Md5, "pdf"));
        if (pdf == null) return new OperationResult(false, localization.GetString("InboxDocumentNotReady"));

        var name = string.IsNullOrWhiteSpace(req.Name) ? Path.GetFileNameWithoutExtension(doc.FileName) : req.Name.Trim();
        var fileId = await archiver.ArchiveAsync(pdf, name, "pdf", req.PropertyIds.Distinct().ToList(),
            req.TagIds.Distinct().ToList(), userId);

        foreach (var s in doc.Suggestions)
        {
            s.Accepted = s.Kind == InboxSuggestionKind.Property ? req.PropertyIds.Contains(s.TargetId) : req.TagIds.Contains(s.TargetId);
            await s.Update(dbContext);
        }
        doc.Status = InboxDocumentStatus.Filed;
        doc.FiledFileId = fileId;
        doc.FiledAt = DateTime.UtcNow;
        doc.FiledByUserId = userId;
        doc.UpdatedByUserId = userId;
        await doc.Update(dbContext);
        return new OperationResult(true, localization.GetString("InboxDocumentFiled"));
    }

    public async Task<OperationResult> UndoAsync(int id, int userId)
    {
        var doc = await dbContext.InboxDocuments.Include(d => d.Suggestions).FirstOrDefaultAsync(d => d.Id == id);
        if (doc is not { Status: InboxDocumentStatus.Filed, FiledFileId: { } fileId, FiledAt: { } filedAt }
            || DateTime.UtcNow - filedAt > UndoWindow)
            return new OperationResult(false, localization.GetString("InboxUndoNotPossible"));

        foreach (var pf in await dbContext.PropertyFiles.Where(x => x.FileId == fileId).ToListAsync()) await pf.Delete(dbContext);
        foreach (var ft in await dbContext.FilesTags.Where(x => x.FileId == fileId).ToListAsync()) await ft.Delete(dbContext);
        foreach (var ud in await dbContext.UploadedDatas.Where(x => x.FileId == fileId).ToListAsync()) await ud.Delete(dbContext);
        var file = await dbContext.Files.FirstAsync(f => f.Id == fileId);
        await file.Delete(dbContext);

        foreach (var s in doc.Suggestions) { s.Accepted = null; await s.Update(dbContext); }
        doc.Status = InboxDocumentStatus.Ready;
        doc.FiledFileId = null;
        doc.FiledAt = null;
        doc.FiledByUserId = null;
        doc.UpdatedByUserId = userId;
        await doc.Update(dbContext);
        return new OperationResult(true);
    }

    public async Task<OperationResult> RejectAsync(int id, int userId)
    {
        var doc = await dbContext.InboxDocuments.FirstOrDefaultAsync(d => d.Id == id);
        if (doc is not { Status: InboxDocumentStatus.Ready or InboxDocumentStatus.Failed })
            return new OperationResult(false, localization.GetString("InboxDocumentNotReady"));
        doc.Status = InboxDocumentStatus.Rejected;
        doc.UpdatedByUserId = userId;
        await doc.Update(dbContext);
        return new OperationResult(true, localization.GetString("InboxDocumentRejected"));
    }

    private async Task<List<InboxListItem>> MapAsync(List<Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities.InboxDocument> docs)
    {
        var propertyIds = docs.SelectMany(d => d.Suggestions).Where(s => s.Kind == InboxSuggestionKind.Property).Select(s => s.TargetId).Distinct().ToList();
        var tagIds = docs.SelectMany(d => d.Suggestions).Where(s => s.Kind == InboxSuggestionKind.Tag).Select(s => s.TargetId).Distinct().ToList();
        var propertyNames = await dbContext.Properties.Where(p => propertyIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name);
        var tagNames = await dbContext.FileTags.Where(t => tagIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Name);

        return docs.Select(d => new InboxListItem
        {
            Id = d.Id, FileName = d.FileName, Subject = d.Subject, FromAddress = d.FromAddress,
            ReceivedAt = d.ReceivedAt, ReadyBy = d.ReadyBy, Status = (int)d.Status, FailureReason = d.FailureReason,
            ReviewedByMicroting = d.ReviewedByMicroting,
            Suggestions = d.Suggestions.Where(s => s.WorkflowState != Constants.WorkflowStates.Removed)
                .OrderByDescending(s => s.Confidence).Select(s => new InboxSuggestionModel
                {
                    Id = s.Id, Kind = (int)s.Kind, TargetId = s.TargetId, Source = (int)s.Source, Confidence = s.Confidence,
                    Evidence = s.Evidence, Page = s.Page, Reason = s.Reason,
                    TargetName = (s.Kind == InboxSuggestionKind.Property ? propertyNames : tagNames).GetValueOrDefault(s.TargetId, "")
                }).ToList()
        }).ToList();
    }
}
```

Add these localization keys to the plugin's backend translation resource, in the same resource file where
`ErrorWhileCreateFiles` is defined (grep for it):

| Key | da | en |
|---|---|---|
| `InboxDocumentNotFound` | Dokumentet findes ikke | Document not found |
| `InboxDocumentNotReady` | Dokumentet er ikke klar til at blive arkiveret | Document is not ready to be filed |
| `InboxChooseAtLeastOneProperty` | Vælg mindst én ejendom | Choose at least one property |
| `InboxUnknownPropertyOrTag` | En valgt ejendom eller et tag findes ikke længere | A selected property or tag no longer exists |
| `InboxDocumentFiled` | Arkiveret | Filed |
| `InboxUndoNotPossible` | Arkiveringen kan ikke længere fortrydes | Filing can no longer be undone |
| `InboxDocumentRejected` | Dokumentet er afvist | Document rejected |
| `InboxSenderApproved` | Afsenderen er godkendt | Sender approved |
| `InboxNotConfigured` | Indbakken er ikke sat op endnu | The inbox is not set up yet |

- [ ] **Step 5: Implement the controller**

`Controllers/InboxController.cs`:

```csharp
using System.Collections.Generic;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Models.Inbox;
using BackendConfiguration.Pn.Services.InboundMail;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.EformBackendConfigurationBase.Infrastructure.Const;

namespace BackendConfiguration.Pn.Controllers;

[Authorize(Policy = BackendConfigurationClaims.EnableInbox)]
[Route("api/backend-configuration-pn/inbox")]
public class InboxController(IInboxService inbox, IInboxSettingsService settings, IUserService userService) : Controller
{
    [HttpGet]
    public Task<OperationDataResult<List<InboxListItem>>> List([FromQuery] int? status, [FromQuery] string? search) =>
        inbox.ListAsync(status, search);

    [HttpGet("{id:int}")]
    public Task<OperationDataResult<InboxListItem>> Get(int id) => inbox.GetAsync(id);

    [HttpGet("{id:int}/file")]
    public async Task<IActionResult> Pdf(int id) =>
        await inbox.GetPdfAsync(id) is { } stream ? File(stream, "application/pdf") : NotFound();

    [HttpPost("{id:int}/file")]
    public Task<OperationResult> FileDocument(int id, [FromBody] FileInboxDocumentRequest req) =>
        inbox.FileAsync(id, req, userService.UserId);

    [HttpPost("{id:int}/undo")]
    public Task<OperationResult> Undo(int id) => inbox.UndoAsync(id, userService.UserId);

    [HttpPost("{id:int}/reject")]
    public Task<OperationResult> Reject(int id) => inbox.RejectAsync(id, userService.UserId);

    [HttpPost("{id:int}/approve-sender")]
    public Task<OperationResult> ApproveSender(int id) => settings.ApproveSenderAsync(id, userService.UserId);

    [HttpPost("{id:int}/reject-sender")]
    public Task<OperationResult> RejectSender(int id, [FromQuery] bool block) =>
        settings.RejectSenderAsync(id, block, userService.UserId);

    [HttpGet("settings")]
    public Task<OperationDataResult<InboxSettingsModel>> GetSettings() => settings.GetAsync(userService.UserId);

    [HttpPut("settings")]
    public Task<OperationResult> PutSettings([FromBody] InboxSettingsModel model) => settings.UpdateAsync(model, userService.UserId);

    [HttpPost("settings/rotate-address")]
    public Task<OperationDataResult<InboxSettingsModel>> Rotate() => settings.RotateAddressAsync(userService.UserId);
}
```

The controller references `IInboxSettingsService`, which Task 5 creates. For this task only, create
`Services/InboundMail/InboxSettingsService.cs` with the interface and a class whose methods return
`new OperationResult(false, "not implemented")`, so the build is green. Task 5 replaces the class body.

```csharp
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Models.Inbox;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;

namespace BackendConfiguration.Pn.Services.InboundMail;

public interface IInboxSettingsService
{
    Task<OperationDataResult<InboxSettingsModel>> GetAsync(int userId);
    Task<OperationResult> UpdateAsync(InboxSettingsModel model, int userId);
    Task<OperationDataResult<InboxSettingsModel>> RotateAddressAsync(int userId);
    Task<OperationResult> ApproveSenderAsync(int inboxDocumentId, int userId);
    Task<OperationResult> RejectSenderAsync(int inboxDocumentId, bool block, int userId);
}
```

Register:

```csharp
        services.AddTransient<Services.InboundMail.IInboxService, Services.InboundMail.InboxService>();
        services.AddTransient<Services.InboundMail.IInboxSettingsService, Services.InboundMail.InboxSettingsService>();
```

- [ ] **Step 6: Build**, then **Step 7: review gate, commit `feat(inbox): manager endpoints to list, file, undo and reject`.**

---

### Task 5: Sender decisions, settings, address rotation and the outbound hub client

**Files:**
- Create: `Services/InboundMail/InboundMailHubClient.cs`, `Services/InboundMail/InboxAddressGenerator.cs`
- Modify: `Services/InboundMail/InboxSettingsService.cs` (full implementation)
- Modify: `EformBackendConfigurationPlugin.cs` (DI, HttpClient)
- Test: `BackendConfiguration.Pn.Integration.Test/InboxSettingsServiceTests.cs`

**Interfaces:**
- Consumes: `InboundMailSignature`, `ICustomerNoProvider`, `InboundMailHubOptions`.
- Produces:
  - `IInboundMailHubClient`:
    - `bool IsConfigured`
    - `Task RegisterAddressAsync(string tokenHash, string? previousTokenHash, DateTime? graceUntil)`
    - `Task SenderDecisionAsync(string hubDocumentId, bool approve)`
  - `static (string Address, string TokenHash) InboxAddressGenerator.New(int customerNo, string mailDomain)`
  - `static string InboxAddressGenerator.HashToken(string token)` (lower-case sha256 hex)

- [ ] **Step 1: Write the failing tests**

`BackendConfiguration.Pn.Integration.Test/InboxSettingsServiceTests.cs`:

```csharp
using System.Text.RegularExpressions;
using BackendConfiguration.Pn.Infrastructure.Models.Inbox;
using BackendConfiguration.Pn.Infrastructure.Models.Settings;
using BackendConfiguration.Pn.Services.InboundMail;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microting.eFormApi.BasePn.Infrastructure.Database.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;
using NSubstitute;

namespace BackendConfiguration.Pn.Integration.Test;

[Parallelizable(ParallelScope.Fixtures)]
[TestFixture]
public class InboxSettingsServiceTests : TestBaseSetup
{
    private IInboundMailHubClient _hub = null!;
    private InboxSettingsService _service = null!;

    [SetUp]
    public async Task SetUpService()
    {
        await InboxTestData.ClearAsync(BackendConfigurationPnDbContext!);
        if (!await BackendConfigurationPnDbContext!.PluginConfigurationValues.AnyAsync(x => x.Name == SenderVerdictResolver.PolicyName))
        {
            BackendConfigurationPnDbContext.PluginConfigurationValues.Add(new PluginConfigurationValue { Name = SenderVerdictResolver.PolicyName, Value = "hold" });
            await BackendConfigurationPnDbContext.SaveChangesAsync();
        }
        _hub = Substitute.For<IInboundMailHubClient>();
        _hub.IsConfigured.Returns(true);
        var customerNo = Substitute.For<ICustomerNoProvider>();
        customerNo.GetAsync().Returns(4711);
        _service = new InboxSettingsService(BackendConfigurationPnDbContext, _hub, customerNo,
            Options.Create(new InboundMailHubOptions { MailDomain = "indbakke.microting.dk" }),
            new BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService.BackendConfigurationLocalizationService());
    }

    [Test]
    public void Generator_MakesValidAddress()
    {
        var (address, hash) = InboxAddressGenerator.New(4711, "indbakke.microting.dk");
        var m = Regex.Match(address, "^4711-([a-z2-7]{10})@indbakke\\.microting\\.dk$");
        Assert.That(m.Success, Is.True, address);
        Assert.That(hash, Is.EqualTo(InboxAddressGenerator.HashToken(m.Groups[1].Value)));
    }

    [Test]
    public async Task Get_FirstTime_CreatesAndRegistersAddress()
    {
        var res = await _service.GetAsync(1);

        Assert.That(res.Success, Is.True);
        Assert.That(res.Model.Address, Does.StartWith("4711-"));
        var row = await BackendConfigurationPnDbContext!.InboxAddresses.SingleAsync();
        await _hub.Received(1).RegisterAddressAsync(row.TokenHash, null, null);
    }

    [Test]
    public async Task Rotate_KeepsOldAddressForSevenDays()
    {
        var first = (await _service.GetAsync(1)).Model.Address;

        var res = await _service.RotateAddressAsync(1);

        Assert.That(res.Model.Address, Is.Not.EqualTo(first));
        var rows = await BackendConfigurationPnDbContext!.InboxAddresses.OrderBy(a => a.Id).ToListAsync();
        Assert.That(rows[0].Active, Is.False);
        Assert.That(rows[0].GraceUntil, Is.EqualTo(DateTime.UtcNow.AddDays(7)).Within(TimeSpan.FromMinutes(1)));
        Assert.That(rows[1].Active, Is.True);
        await _hub.Received(1).RegisterAddressAsync(rows[1].TokenHash, rows[0].TokenHash, rows[0].GraceUntil);
    }

    [Test]
    public async Task Update_ReplacesRulesAndPolicy()
    {
        await _service.UpdateAsync(new InboxSettingsModel
        {
            UnknownSenderPolicy = "refuse",
            SenderRules = [new() { Pattern = "@example.org", Kind = 0 }, new() { Pattern = "spam@example.net", Kind = 1 }]
        }, 1);
        await _service.UpdateAsync(new InboxSettingsModel { UnknownSenderPolicy = "refuse", SenderRules = [new() { Pattern = "@example.org", Kind = 0 }] }, 1);

        var live = await BackendConfigurationPnDbContext!.InboxSenderRules.Where(r => r.WorkflowState != "removed").ToListAsync();
        Assert.That(live.Select(r => r.Pattern), Is.EquivalentTo(new[] { "@example.org" }));
        Assert.That((await BackendConfigurationPnDbContext.PluginConfigurationValues.SingleAsync(x => x.Name == SenderVerdictResolver.PolicyName)).Value, Is.EqualTo("refuse"));
    }

    [Test]
    public async Task Update_InvalidPattern_IsRejected()
    {
        var res = await _service.UpdateAsync(new InboxSettingsModel { SenderRules = [new() { Pattern = "not an address", Kind = 0 }] }, 1);
        Assert.That(res.Success, Is.False);
    }

    [Test]
    public async Task ApproveSender_AddsAllowRule_TellsHub_StaysPreparing()
    {
        var doc = await InboxTestData.ReadyDocumentAsync(BackendConfigurationPnDbContext!);
        doc.Status = InboxDocumentStatus.SenderPending;
        doc.FromAddress = "post@example.net";
        await doc.Update(BackendConfigurationPnDbContext!);

        var res = await _service.ApproveSenderAsync(doc.Id, 1);

        Assert.That(res.Success, Is.True);
        await BackendConfigurationPnDbContext!.Entry(doc).ReloadAsync();
        Assert.That(doc.Status, Is.EqualTo(InboxDocumentStatus.Preparing));
        Assert.That(await BackendConfigurationPnDbContext.InboxSenderRules.AnyAsync(r => r.Pattern == "post@example.net" && r.Kind == InboxSenderRuleKind.Allow), Is.True);
        await _hub.Received(1).SenderDecisionAsync(doc.HubDocumentId, true);
    }

    [Test]
    public async Task RejectSender_WithBlock_AddsBlockRule_Rejected()
    {
        var doc = await InboxTestData.ReadyDocumentAsync(BackendConfigurationPnDbContext!);
        doc.Status = InboxDocumentStatus.SenderPending;
        doc.FromAddress = "post@example.net";
        await doc.Update(BackendConfigurationPnDbContext!);

        await _service.RejectSenderAsync(doc.Id, true, 1);

        await BackendConfigurationPnDbContext!.Entry(doc).ReloadAsync();
        Assert.That(doc.Status, Is.EqualTo(InboxDocumentStatus.Rejected));
        Assert.That(await BackendConfigurationPnDbContext.InboxSenderRules.AnyAsync(r => r.Kind == InboxSenderRuleKind.Block), Is.True);
        await _hub.Received(1).SenderDecisionAsync(doc.HubDocumentId, false);
    }
}
```

- [ ] **Step 2: Build to verify it fails**: `dotnet build …/BackendConfiguration.Pn.sln`

- [ ] **Step 3: Implement the generator and the hub client**

`Services/InboundMail/InboxAddressGenerator.cs`:

```csharp
using System;
using System.Security.Cryptography;
using System.Text;

namespace BackendConfiguration.Pn.Services.InboundMail;

public static class InboxAddressGenerator
{
    private const string Alphabet = "abcdefghijklmnopqrstuvwxyz234567";

    public static (string Address, string TokenHash) New(int customerNo, string mailDomain)
    {
        var localPart = string.Create(10, 0, (span, _) =>
        {
            for (var i = 0; i < span.Length; i++) span[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        });
        return ($"{customerNo}-{localPart}@{mailDomain}", HashToken(localPart));
    }

    public static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token.ToLowerInvariant()))).ToLowerInvariant();
}
```

`Services/InboundMail/InboundMailHubClient.cs`:

```csharp
using System;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Models.Inbox;
using BackendConfiguration.Pn.Infrastructure.Models.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BackendConfiguration.Pn.Services.InboundMail;

public interface IInboundMailHubClient
{
    bool IsConfigured { get; }
    Task RegisterAddressAsync(string tokenHash, string? previousTokenHash, DateTime? graceUntil);
    Task SenderDecisionAsync(string hubDocumentId, bool approve);
}

public class InboundMailHubClient(HttpClient http, IOptions<InboundMailHubOptions> options,
    ICustomerNoProvider customerNo, ILogger<InboundMailHubClient> logger) : IInboundMailHubClient
{
    public bool IsConfigured => !string.IsNullOrEmpty(options.Value.HubUrl) && !string.IsNullOrEmpty(options.Value.TenantSigningKey);

    public async Task RegisterAddressAsync(string tokenHash, string? previousTokenHash, DateTime? graceUntil)
    {
        var n = await customerNo.GetAsync();
        await SendAsync(HttpMethod.Put, $"/api/tenants/{n}/address", n,
            new { tokenHash, previousTokenHash, graceUntil, tenantName = $"Kunde {n}" });
    }

    public async Task SenderDecisionAsync(string hubDocumentId, bool approve)
    {
        var n = await customerNo.GetAsync();
        await SendAsync(HttpMethod.Post, $"/api/tenants/{n}/documents/{hubDocumentId}/sender-decision", n,
            new { decision = approve ? "approve" : "reject" });
    }

    private async Task SendAsync(HttpMethod method, string path, int n, object payload)
    {
        if (!IsConfigured)
        {
            logger.LogWarning("Inbound mail service not configured; skipped {Method} {Path}", method, path);
            return;
        }
        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, HubJson.Options));
        var date = DateTime.UtcNow.ToString("R", CultureInfo.InvariantCulture);
        var id = Guid.NewGuid().ToString();
        var sig = InboundMailSignature.Sign(options.Value.TenantSigningKey,
            InboundMailSignature.Canonical(method.Method, path, n, id, date, InboundMailSignature.BodyHash(body)));
        using var req = new HttpRequestMessage(method, options.Value.HubUrl.TrimEnd('/') + path)
        {
            Content = new ByteArrayContent(body) { Headers = { ContentType = new("application/json") } }
        };
        req.Headers.TryAddWithoutValidation("Authorization", InboundMailSignature.Scheme + sig);
        req.Headers.TryAddWithoutValidation("Date", date);
        req.Headers.TryAddWithoutValidation("X-Request-Id", id);
        req.Headers.TryAddWithoutValidation("X-Customer-No", n.ToString(CultureInfo.InvariantCulture));
        using var res = await http.SendAsync(req);
        res.EnsureSuccessStatusCode();
    }
}
```

- [ ] **Step 4: Implement the settings service**

Replace the placeholder class in `Services/InboundMail/InboxSettingsService.cs`. Keep the interface from Task 4
above it.

```csharp
using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BackendConfiguration.Pn.Infrastructure.Models.Inbox;
using BackendConfiguration.Pn.Infrastructure.Models.Settings;
using BackendConfiguration.Pn.Services.BackendConfigurationLocalizationService;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microting.eForm.Infrastructure.Constants;
using Microting.eFormApi.BasePn.Infrastructure.Models.API;
using Microting.EformBackendConfigurationBase.Infrastructure.Data;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;
using Microting.EformBackendConfigurationBase.Infrastructure.Enum;

namespace BackendConfiguration.Pn.Services.InboundMail;

public partial class InboxSettingsService(BackendConfigurationPnDbContext dbContext, IInboundMailHubClient hub,
    ICustomerNoProvider customerNo, IOptions<InboundMailHubOptions> options,
    IBackendConfigurationLocalizationService localization) : IInboxSettingsService
{
    private static readonly TimeSpan Grace = TimeSpan.FromDays(7);

    [GeneratedRegex(@"^(@[a-z0-9.-]+\.[a-z]{2,}|[^@\s]+@[a-z0-9.-]+\.[a-z]{2,})$")]
    private static partial Regex PatternRegex();

    public async Task<OperationDataResult<InboxSettingsModel>> GetAsync(int userId)
    {
        var address = await dbContext.InboxAddresses.FirstOrDefaultAsync(a => a.Active && a.WorkflowState != Constants.WorkflowStates.Removed);
        if (address == null)
        {
            var (value, hash) = InboxAddressGenerator.New(await customerNo.GetAsync(), options.Value.MailDomain);
            address = new InboxAddress { Address = value, TokenHash = hash, Active = true, CreatedByUserId = userId, UpdatedByUserId = userId };
            await address.Create(dbContext);
        }
        // Idempotent: also heals a registration that failed or happened before the service was configured.
        await hub.RegisterAddressAsync(address.TokenHash, null, null);
        return new OperationDataResult<InboxSettingsModel>(true, await ModelAsync(address.Address));
    }

    public async Task<OperationResult> UpdateAsync(InboxSettingsModel model, int userId)
    {
        var patterns = model.SenderRules.Select(r => r.Pattern.Trim().ToLowerInvariant()).ToList();
        if (patterns.Any(p => !PatternRegex().IsMatch(p)) || model.UnknownSenderPolicy is not ("hold" or "refuse"))
            return new OperationResult(false, localization.GetString("InboxInvalidSenderRule"));

        var existing = await dbContext.InboxSenderRules.Where(r => r.WorkflowState != Constants.WorkflowStates.Removed).ToListAsync();
        foreach (var rule in existing.Where(r => !model.SenderRules.Any(m => m.Pattern.Trim().ToLowerInvariant() == r.Pattern && m.Kind == (int)r.Kind)))
            await rule.Delete(dbContext);
        foreach (var m in model.SenderRules.Where(m => !existing.Any(r => r.Pattern == m.Pattern.Trim().ToLowerInvariant() && (int)r.Kind == m.Kind)))
            await new InboxSenderRule { Pattern = m.Pattern.Trim().ToLowerInvariant(), Kind = (InboxSenderRuleKind)m.Kind, CreatedByUserId = userId, UpdatedByUserId = userId }.Create(dbContext);

        var policy = await dbContext.PluginConfigurationValues.SingleAsync(x => x.Name == SenderVerdictResolver.PolicyName);
        policy.Value = model.UnknownSenderPolicy;
        policy.UpdatedAt = DateTime.UtcNow;
        policy.UpdatedByUserId = userId;
        await dbContext.SaveChangesAsync();
        return new OperationResult(true);
    }

    public async Task<OperationDataResult<InboxSettingsModel>> RotateAddressAsync(int userId)
    {
        var old = await dbContext.InboxAddresses.FirstOrDefaultAsync(a => a.Active && a.WorkflowState != Constants.WorkflowStates.Removed);
        if (old != null)
        {
            old.Active = false;
            old.GraceUntil = DateTime.UtcNow + Grace;
            old.UpdatedByUserId = userId;
            await old.Update(dbContext);
        }
        var (value, hash) = InboxAddressGenerator.New(await customerNo.GetAsync(), options.Value.MailDomain);
        await new InboxAddress { Address = value, TokenHash = hash, Active = true, CreatedByUserId = userId, UpdatedByUserId = userId }.Create(dbContext);
        await hub.RegisterAddressAsync(hash, old?.TokenHash, old?.GraceUntil);
        return new OperationDataResult<InboxSettingsModel>(true, await ModelAsync(value));
    }

    public async Task<OperationResult> ApproveSenderAsync(int inboxDocumentId, int userId)
    {
        var doc = await dbContext.InboxDocuments.FirstOrDefaultAsync(d => d.Id == inboxDocumentId);
        if (doc is not { Status: InboxDocumentStatus.SenderPending })
            return new OperationResult(false, localization.GetString("InboxDocumentNotReady"));
        var pattern = doc.FromAddress.Trim().ToLowerInvariant();
        if (!await dbContext.InboxSenderRules.AnyAsync(r => r.Pattern == pattern && r.Kind == InboxSenderRuleKind.Allow && r.WorkflowState != Constants.WorkflowStates.Removed))
            await new InboxSenderRule { Pattern = pattern, Kind = InboxSenderRuleKind.Allow, CreatedByUserId = userId, UpdatedByUserId = userId }.Create(dbContext);
        await hub.SenderDecisionAsync(doc.HubDocumentId, true);
        doc.Status = InboxDocumentStatus.Preparing;
        doc.UpdatedByUserId = userId;
        await doc.Update(dbContext);
        return new OperationResult(true, localization.GetString("InboxSenderApproved"));
    }

    public async Task<OperationResult> RejectSenderAsync(int inboxDocumentId, bool block, int userId)
    {
        var doc = await dbContext.InboxDocuments.FirstOrDefaultAsync(d => d.Id == inboxDocumentId);
        if (doc is not { Status: InboxDocumentStatus.SenderPending })
            return new OperationResult(false, localization.GetString("InboxDocumentNotReady"));
        if (block)
            await new InboxSenderRule { Pattern = doc.FromAddress.Trim().ToLowerInvariant(), Kind = InboxSenderRuleKind.Block, CreatedByUserId = userId, UpdatedByUserId = userId }.Create(dbContext);
        await hub.SenderDecisionAsync(doc.HubDocumentId, false);
        doc.Status = InboxDocumentStatus.Rejected;
        doc.UpdatedByUserId = userId;
        await doc.Update(dbContext);
        return new OperationResult(true, localization.GetString("InboxDocumentRejected"));
    }

    private async Task<InboxSettingsModel> ModelAsync(string address) => new()
    {
        Address = address,
        UnknownSenderPolicy = await dbContext.PluginConfigurationValues.Where(x => x.Name == SenderVerdictResolver.PolicyName)
            .Select(x => x.Value).FirstOrDefaultAsync() ?? "hold",
        SenderRules = await dbContext.InboxSenderRules.Where(r => r.WorkflowState != Constants.WorkflowStates.Removed)
            .OrderBy(r => r.Pattern).Select(r => new InboxSenderRuleModel { Id = r.Id, Pattern = r.Pattern, Kind = (int)r.Kind }).ToListAsync()
    };
}
```

Add the localization key `InboxInvalidSenderRule`: da "Skriv en e-mailadresse eller et domæne som @firma.dk",
en "Enter an email address or a domain such as @company.com".

Register in `EformBackendConfigurationPlugin.cs`:

```csharp
        services.AddHttpClient<Services.InboundMail.IInboundMailHubClient, Services.InboundMail.InboundMailHubClient>(
            c => c.Timeout = TimeSpan.FromSeconds(15));
```

- [ ] **Step 5: Build**, then **Step 6: review gate, commit `feat(inbox): settings, address rotation and sender decisions`.**

---

### Task 6: Angular Indbakke, review dialog and Indstillinger e-mail

**Files:**
- Create: `eform-client/src/app/plugins/modules/backend-configuration-pn/models/inbox/inbox.models.ts`
- Modify: `…/models/index.ts` (export)
- Create: `…/services/backend-configuration-pn-inbox.service.ts`; modify `…/services/index.ts`
- Modify: `…/enums/backend-configuration-pn-claims.const.ts` (add `enableInbox: 'inbox_enable'`)
- Create: `…/modules/files/components/archive-section-nav/archive-section-nav.component.{ts,html}`
- Create: `…/modules/files/components/inbox-container/inbox-container.component.{ts,html}`
- Create: `…/modules/files/components/inbox-review-dialog/inbox-review-dialog.component.{ts,html}`
- Create: `…/modules/files/components/inbox-settings/inbox-settings.component.{ts,html}`
- Modify: `…/modules/files/components/index.ts`, `…/modules/files/files.module.ts`, `…/modules/files/files.routing.ts`,
  `…/modules/files/components/files-container/files-container.component.html` (section nav at the top)
- Modify: `…/i18n/enUS.ts`, `…/i18n/da.ts`
- Test: covered by Task 7's Playwright spec. Angular has no unit-test setup in this module.

**Interfaces:**
- Consumes: the Task 4 and Task 5 routes and DTOs.
- Produces the routes `/plugins/backend-configuration-pn/files/inbox` and `/plugins/backend-configuration-pn/files/settings`.
  The element ids used by Playwright:
  - `#archiveNavInbox`, `#archiveNavArchive`, `#archiveNavSettings`
  - `#inboxTable`, with rows `.inbox-row` carrying `[data-inbox-id]`
  - `#inboxReviewBtn-<id>`, `#inboxApproveSenderBtn-<id>`
  - in the dialog: `#inboxFileName`, `.inbox-choice` (with `[data-kind]`, `[data-target-id]`, `aria-pressed`),
    `#inboxFileBtn`, `#inboxRejectBtn`, `#inboxUndoBtn`
  - on the settings page: `#inboxAddress`, `#inboxCopyBtn`, `#inboxRotateBtn`, `#inboxRulesInput`, `#inboxPolicyHold`,
    `#inboxPolicyRefuse`, `#inboxSaveBtn`

- [ ] **Step 1: Models and service**

`models/inbox/inbox.models.ts`:

```ts
export enum InboxDocumentStatus { Preparing = 0, SenderPending = 1, Ready = 2, Failed = 3, Filed = 4, Rejected = 5 }
export enum InboxSuggestionKind { Property = 0, Tag = 1 }

export interface InboxSuggestionModel {
  id: number;
  kind: InboxSuggestionKind;
  targetId: number;
  targetName: string;
  source: number;
  confidence: number;
  evidence: string | null;
  page: number | null;
  reason: string | null;
}

export interface InboxListItemModel {
  id: number;
  fileName: string;
  subject: string | null;
  fromAddress: string;
  receivedAt: string;
  readyBy: string | null;
  status: InboxDocumentStatus;
  failureReason: string | null;
  reviewedByMicroting: boolean;
  suggestions: InboxSuggestionModel[];
}

export interface FileInboxDocumentModel {
  name: string;
  propertyIds: number[];
  tagIds: number[];
}

export interface InboxSenderRuleModel { id?: number; pattern: string; kind: 0 | 1; }

export interface InboxSettingsModel {
  address: string | null;
  unknownSenderPolicy: 'hold' | 'refuse';
  senderRules: InboxSenderRuleModel[];
}
```

Add `export * from './inbox/inbox.models';` to `models/index.ts`.

`services/backend-configuration-pn-inbox.service.ts`:

```ts
import {Injectable} from '@angular/core';
import {Observable} from 'rxjs';
import {ApiBaseService} from 'src/app/common/services';
import {OperationDataResult, OperationResult} from 'src/app/common/models';
import {FileInboxDocumentModel, InboxListItemModel, InboxSettingsModel} from '../models';

export const BackendConfigurationPnInboxMethods = {
  Inbox: 'api/backend-configuration-pn/inbox',
  Settings: 'api/backend-configuration-pn/inbox/settings',
};

@Injectable({providedIn: 'root'})
export class BackendConfigurationPnInboxService {
  constructor(private apiBaseService: ApiBaseService) {}

  list(status: number | null, search: string): Observable<OperationDataResult<InboxListItemModel[]>> {
    const params: Record<string, string> = {};
    if (status !== null) { params.status = String(status); }
    if (search) { params.search = search; }
    return this.apiBaseService.get(BackendConfigurationPnInboxMethods.Inbox, params);
  }

  getPdf(id: number): Observable<Blob> {
    return this.apiBaseService.getBlobData(`${BackendConfigurationPnInboxMethods.Inbox}/${id}/file`);
  }

  file(id: number, model: FileInboxDocumentModel): Observable<OperationResult> {
    return this.apiBaseService.post(`${BackendConfigurationPnInboxMethods.Inbox}/${id}/file`, model);
  }

  undo(id: number): Observable<OperationResult> {
    return this.apiBaseService.post(`${BackendConfigurationPnInboxMethods.Inbox}/${id}/undo`, {});
  }

  reject(id: number): Observable<OperationResult> {
    return this.apiBaseService.post(`${BackendConfigurationPnInboxMethods.Inbox}/${id}/reject`, {});
  }

  approveSender(id: number): Observable<OperationResult> {
    return this.apiBaseService.post(`${BackendConfigurationPnInboxMethods.Inbox}/${id}/approve-sender`, {});
  }

  rejectSender(id: number, block: boolean): Observable<OperationResult> {
    return this.apiBaseService.post(`${BackendConfigurationPnInboxMethods.Inbox}/${id}/reject-sender?block=${block}`, {});
  }

  getSettings(): Observable<OperationDataResult<InboxSettingsModel>> {
    return this.apiBaseService.get(BackendConfigurationPnInboxMethods.Settings);
  }

  updateSettings(model: InboxSettingsModel): Observable<OperationResult> {
    return this.apiBaseService.put(BackendConfigurationPnInboxMethods.Settings, model);
  }

  rotateAddress(): Observable<OperationDataResult<InboxSettingsModel>> {
    return this.apiBaseService.post(`${BackendConfigurationPnInboxMethods.Settings}/rotate-address`, {});
  }
}
```

Check `ApiBaseService.get`'s second parameter (query params) and `getBlobData` in
`src/app/common/services/api-base.service.ts`. If `get` does not accept params, build the query string into
the URL instead. Add `export * from './backend-configuration-pn-inbox.service';` to `services/index.ts`.

- [ ] **Step 2: Section navigation**

`components/archive-section-nav/archive-section-nav.component.ts`:

```ts
import {Component, inject} from '@angular/core';
import {AuthStateService} from 'src/app/common/store';
import {BackendConfigurationPnClaims} from '../../../../enums';

@Component({
  selector: 'app-archive-section-nav',
  templateUrl: './archive-section-nav.component.html',
  standalone: false,
})
export class ArchiveSectionNavComponent {
  private authStateService = inject(AuthStateService);
  get canUseInbox(): boolean {
    return this.authStateService.checkClaim(BackendConfigurationPnClaims.enableInbox);
  }
}
```

Check the import path of `AuthStateService` by grepping
`property-worker-create-edit-modal.component.ts`, which uses `authStateService.checkClaim`, and use the same
import.

`components/archive-section-nav/archive-section-nav.component.html`:

```html
<nav class="d-flex flex-row flex-wrap gap-8 mb-2" [attr.aria-label]="'Archive sections' | translate">
  <a *ngIf="canUseInbox" id="archiveNavInbox" mat-stroked-button routerLink="/plugins/backend-configuration-pn/files/inbox"
     routerLinkActive="mat-primary" [routerLinkActiveOptions]="{exact: true}" ariaCurrentWhenActive="page">
    {{ 'Inbox' | translate }}
  </a>
  <a id="archiveNavArchive" mat-stroked-button routerLink="/plugins/backend-configuration-pn/files"
     routerLinkActive="mat-primary" [routerLinkActiveOptions]="{exact: true}" ariaCurrentWhenActive="page">
    {{ 'Archive' | translate }}
  </a>
  <a *ngIf="canUseInbox" id="archiveNavSettings" mat-stroked-button routerLink="/plugins/backend-configuration-pn/files/settings"
     routerLinkActive="mat-primary" [routerLinkActiveOptions]="{exact: true}" ariaCurrentWhenActive="page">
    {{ 'Email settings' | translate }}
  </a>
</nav>
```

At the top of `files-container.component.html`, inside the `mat-card`, before the `<h2>`, add
`<app-archive-section-nav></app-archive-section-nav>`.

- [ ] **Step 3: The Indbakke container**

`components/inbox-container/inbox-container.component.ts`:

```ts
import {Component, OnInit, inject} from '@angular/core';
import {MatDialog} from '@angular/material/dialog';
import {ToastrService} from 'ngx-toastr';
import {TranslateService} from '@ngx-translate/core';
import {BackendConfigurationPnInboxService} from '../../../../services';
import {InboxDocumentStatus, InboxListItemModel, InboxSuggestionKind} from '../../../../models';
import {InboxReviewDialogComponent} from '../inbox-review-dialog/inbox-review-dialog.component';

@Component({
  selector: 'app-inbox-container',
  templateUrl: './inbox-container.component.html',
  standalone: false,
})
export class InboxContainerComponent implements OnInit {
  private inboxService = inject(BackendConfigurationPnInboxService);
  private dialog = inject(MatDialog);
  private toastr = inject(ToastrService);
  private translate = inject(TranslateService);

  readonly Status = InboxDocumentStatus;
  documents: InboxListItemModel[] = [];
  statusFilter: number | null = null;
  search = '';

  ngOnInit(): void { this.load(); }

  load(): void {
    this.inboxService.list(this.statusFilter, this.search).subscribe(res => {
      if (res?.success) { this.documents = res.model; }
    });
  }

  openCount(): number {
    return this.documents.filter(d => d.status === InboxDocumentStatus.Ready || d.status === InboxDocumentStatus.SenderPending).length;
  }

  properties(d: InboxListItemModel) { return d.suggestions.filter(s => s.kind === InboxSuggestionKind.Property && s.confidence >= 0.5); }
  tags(d: InboxListItemModel) { return d.suggestions.filter(s => s.kind === InboxSuggestionKind.Tag && s.confidence >= 0.5); }

  confidenceClass(confidence: number): string {
    return confidence >= 0.75 ? 'inbox-dot inbox-dot--high' : confidence >= 0.5 ? 'inbox-dot inbox-dot--medium' : 'inbox-dot inbox-dot--low';
  }

  statusLabel(status: InboxDocumentStatus): string {
    return ['Being prepared', 'Approve sender', 'Ready to file', 'Could not be read', 'Filed', 'Rejected'][status];
  }

  review(d: InboxListItemModel): void {
    this.dialog.open(InboxReviewDialogComponent, {data: d, width: '1040px', maxWidth: '96vw', autoFocus: false})
      .afterClosed().subscribe(changed => { if (changed) { this.load(); } });
  }

  approveSender(d: InboxListItemModel): void {
    this.inboxService.approveSender(d.id).subscribe(res => {
      if (res?.success) { this.toastr.success(this.translate.instant('Sender approved')); this.load(); }
    });
  }

  rejectSender(d: InboxListItemModel): void {
    this.inboxService.rejectSender(d.id, true).subscribe(res => { if (res?.success) { this.load(); } });
  }
}
```

`components/inbox-container/inbox-container.component.html`:

```html
<mat-card class="eform-sub-header d-flex flex-column gap-12 mb-3 p-3">
  <app-archive-section-nav></app-archive-section-nav>
  <h2 style="margin: 0">{{ 'Inbox' | translate }}</h2>
  <p class="mb-0">{{ 'PDFs sent or forwarded to your archive address. Property and tags are suggested from the document; you approve before anything is filed.' | translate }}</p>
  <div class="d-flex flex-row flex-wrap align-items-end gap-12">
    <mat-form-field>
      <mat-label>{{ 'Status' | translate }}</mat-label>
      <mat-select id="inboxStatusFilter" [(ngModel)]="statusFilter" (selectionChange)="load()">
        <mat-option [value]="null">{{ 'All open' | translate }}</mat-option>
        <mat-option [value]="Status.Ready">{{ 'Ready to file' | translate }}</mat-option>
        <mat-option [value]="Status.Preparing">{{ 'Being prepared' | translate }}</mat-option>
        <mat-option [value]="Status.SenderPending">{{ 'Approve sender' | translate }}</mat-option>
        <mat-option [value]="Status.Failed">{{ 'Could not be read' | translate }}</mat-option>
        <mat-option [value]="Status.Filed">{{ 'Filed' | translate }}</mat-option>
      </mat-select>
    </mat-form-field>
    <mat-form-field>
      <mat-label>{{ 'Search' | translate }}</mat-label>
      <input matInput id="inboxSearch" [(ngModel)]="search" (keyup.enter)="load()" [placeholder]="'PDF name, sender …' | translate">
    </mat-form-field>
  </div>
</mat-card>

<mat-card class="p-3">
  <table id="inboxTable" class="table">
    <thead>
      <tr>
        <th>{{ 'PDF / subject' | translate }}</th>
        <th>{{ 'Sender' | translate }}</th>
        <th>{{ 'Received' | translate }}</th>
        <th>{{ 'Property' | translate }}</th>
        <th>{{ 'Tags' | translate }}</th>
        <th>{{ 'Status' | translate }}</th>
        <th class="text-end">{{ 'Action' | translate }}</th>
      </tr>
    </thead>
    <tbody>
      <tr *ngFor="let d of documents" class="inbox-row" [attr.data-inbox-id]="d.id">
        <td><strong>{{ d.subject || d.fileName }}</strong><br><small>{{ d.fileName }}</small></td>
        <td>{{ d.fromAddress }}</td>
        <td>{{ d.receivedAt | date: 'dd.MM.yyyy HH:mm' }}</td>
        <td><span *ngFor="let s of properties(d)" class="inbox-chip"><span [class]="confidenceClass(s.confidence)"></span>{{ s.targetName }}</span></td>
        <td><span *ngFor="let s of tags(d)" class="inbox-chip"><span [class]="confidenceClass(s.confidence)"></span>{{ s.targetName }}</span></td>
        <td>
          {{ statusLabel(d.status) | translate }}
          <div *ngIf="d.status === Status.Preparing && d.readyBy"><small>{{ 'Ready by' | translate }} {{ d.readyBy | date: 'HH:mm' }}</small></div>
          <div *ngIf="d.reviewedByMicroting"><small>{{ 'Checked by Microting' | translate }}</small></div>
          <div *ngIf="d.status === Status.Failed"><small>{{ d.failureReason }}</small></div>
        </td>
        <td class="text-end">
          <button *ngIf="d.status === Status.Ready" mat-button color="primary" [id]="'inboxReviewBtn-' + d.id" (click)="review(d)">{{ 'Review' | translate }}</button>
          <ng-container *ngIf="d.status === Status.SenderPending">
            <button mat-button color="primary" [id]="'inboxApproveSenderBtn-' + d.id" (click)="approveSender(d)">{{ 'Approve sender' | translate }}</button>
            <button mat-button [id]="'inboxRejectSenderBtn-' + d.id" (click)="rejectSender(d)">{{ 'Reject' | translate }}</button>
          </ng-container>
        </td>
      </tr>
      <tr *ngIf="documents.length === 0">
        <td colspan="7">{{ 'No documents waiting. Forward a mail with a PDF to your archive address to get started.' | translate }}</td>
      </tr>
    </tbody>
  </table>
</mat-card>
```

- [ ] **Step 4: The review dialog**

`components/inbox-review-dialog/inbox-review-dialog.component.ts`:

```ts
import {Component, OnDestroy, OnInit, inject} from '@angular/core';
import {MAT_DIALOG_DATA, MatDialogRef} from '@angular/material/dialog';
import {ToastrService} from 'ngx-toastr';
import {TranslateService} from '@ngx-translate/core';
import {BackendConfigurationPnInboxService} from '../../../../services';
import {InboxListItemModel, InboxSuggestionKind, InboxSuggestionModel} from '../../../../models';

interface Choice { suggestion: InboxSuggestionModel; on: boolean; }

@Component({
  selector: 'app-inbox-review-dialog',
  templateUrl: './inbox-review-dialog.component.html',
  standalone: false,
})
export class InboxReviewDialogComponent implements OnInit, OnDestroy {
  readonly data: InboxListItemModel = inject(MAT_DIALOG_DATA);
  private dialogRef = inject(MatDialogRef<InboxReviewDialogComponent>);
  private inboxService = inject(BackendConfigurationPnInboxService);
  private toastr = inject(ToastrService);
  private translate = inject(TranslateService);

  pdfUrl: string | null = null;
  name = this.data.fileName.replace(/\.pdf$/i, '');
  propertyChoices: Choice[] = [];
  tagChoices: Choice[] = [];
  focused: InboxSuggestionModel | null = null;
  filed = false;
  private objectUrl: string | null = null;

  ngOnInit(): void {
    const toChoice = (s: InboxSuggestionModel): Choice => ({suggestion: s, on: s.confidence >= 0.5});
    this.propertyChoices = this.data.suggestions.filter(s => s.kind === InboxSuggestionKind.Property).map(toChoice);
    this.tagChoices = this.data.suggestions.filter(s => s.kind === InboxSuggestionKind.Tag).map(toChoice);
    this.inboxService.getPdf(this.data.id).subscribe(blob => {
      this.objectUrl = URL.createObjectURL(blob);
      this.pdfUrl = this.objectUrl;
    });
  }

  ngOnDestroy(): void { if (this.objectUrl) { URL.revokeObjectURL(this.objectUrl); } }

  toggle(c: Choice): void { c.on = !c.on; this.focused = c.suggestion; }

  fileIt(): void {
    this.inboxService.file(this.data.id, {
      name: this.name,
      propertyIds: this.propertyChoices.filter(c => c.on).map(c => c.suggestion.targetId),
      tagIds: this.tagChoices.filter(c => c.on).map(c => c.suggestion.targetId),
    }).subscribe(res => {
      if (res?.success) { this.filed = true; this.toastr.success(this.translate.instant('Filed')); }
    });
  }

  undo(): void {
    this.inboxService.undo(this.data.id).subscribe(res => { if (res?.success) { this.filed = false; } });
  }

  reject(): void {
    this.inboxService.reject(this.data.id).subscribe(res => { if (res?.success) { this.dialogRef.close(true); } });
  }

  close(): void { this.dialogRef.close(this.filed); }
}
```

`components/inbox-review-dialog/inbox-review-dialog.component.html`:

```html
<h3 mat-dialog-title>{{ 'Review and file' | translate }}</h3>
<div mat-dialog-content>
  <p class="mb-2">{{ data.subject || data.fileName }} · {{ data.fromAddress }} · {{ data.receivedAt | date: 'dd.MM.yyyy HH:mm' }}</p>
  <p *ngIf="data.reviewedByMicroting" class="mb-2"><mat-icon inline>verified</mat-icon> {{ 'Microting checked these suggestions before they reached you.' | translate }}</p>
  <div class="d-flex flex-row flex-wrap gap-12">
    <div style="flex: 1 1 420px; min-width: 0">
      <pdf-viewer *ngIf="pdfUrl" [src]="pdfUrl" [render-text]="true" [original-size]="false" style="display: block; height: 520px"></pdf-viewer>
    </div>
    <div style="flex: 1 1 320px; min-width: 0" class="d-flex flex-column gap-12">
      <div>
        <strong>{{ 'Property' | translate }}</strong>
        <div class="d-flex flex-row flex-wrap gap-8 mt-1">
          <button *ngFor="let c of propertyChoices" mat-stroked-button class="inbox-choice" type="button"
                  [attr.data-kind]="'property'" [attr.data-target-id]="c.suggestion.targetId" [attr.aria-pressed]="c.on"
                  [color]="c.on ? 'primary' : undefined" (click)="toggle(c)" (focus)="focused = c.suggestion" (mouseenter)="focused = c.suggestion">
            <mat-icon *ngIf="c.on">check</mat-icon>{{ c.suggestion.targetName }}
          </button>
        </div>
      </div>
      <div>
        <strong>{{ 'Tags' | translate }}</strong>
        <div class="d-flex flex-row flex-wrap gap-8 mt-1">
          <button *ngFor="let c of tagChoices" mat-stroked-button class="inbox-choice" type="button"
                  [attr.data-kind]="'tag'" [attr.data-target-id]="c.suggestion.targetId" [attr.aria-pressed]="c.on"
                  [color]="c.on ? 'primary' : undefined" (click)="toggle(c)" (focus)="focused = c.suggestion" (mouseenter)="focused = c.suggestion">
            <mat-icon *ngIf="c.on">check</mat-icon>{{ c.suggestion.targetName }}
          </button>
        </div>
      </div>
      <p class="mb-0">
        <ng-container *ngIf="focused; else hint"><strong>{{ focused.targetName }}:</strong> {{ focused.reason }}
          <span *ngIf="focused.evidence"> “{{ focused.evidence }}”<span *ngIf="focused.page"> ({{ 'page' | translate }} {{ focused.page }})</span></span>
        </ng-container>
        <ng-template #hint>{{ 'Point at a suggestion to see why it was suggested and where it appears in the document.' | translate }}</ng-template>
      </p>
      <mat-form-field>
        <mat-label>{{ 'File name in the archive' | translate }}</mat-label>
        <input matInput id="inboxFileName" [(ngModel)]="name">
      </mat-form-field>
    </div>
  </div>
</div>
<div mat-dialog-actions class="d-flex justify-content-end gap-12">
  <button *ngIf="!filed" mat-stroked-button id="inboxRejectBtn" (click)="reject()">{{ 'Reject document' | translate }}</button>
  <button *ngIf="!filed" mat-flat-button color="primary" id="inboxFileBtn" (click)="fileIt()">{{ 'File in archive' | translate }}</button>
  <button *ngIf="filed" mat-stroked-button id="inboxUndoBtn" (click)="undo()">{{ 'Undo' | translate }}</button>
  <button mat-button id="inboxCloseBtn" (click)="close()">{{ 'Close' | translate }}</button>
</div>
```

The text-layer highlight of the evidence inside `pdf-viewer` is a follow-up. Phase 1 shows the evidence text
next to the PDF. Do not block on highlighting.

- [ ] **Step 5: The settings page**

`components/inbox-settings/inbox-settings.component.ts`:

```ts
import {Component, OnInit, inject} from '@angular/core';
import {ToastrService} from 'ngx-toastr';
import {TranslateService} from '@ngx-translate/core';
import {BackendConfigurationPnInboxService} from '../../../../services';
import {InboxSettingsModel} from '../../../../models';

@Component({
  selector: 'app-inbox-settings',
  templateUrl: './inbox-settings.component.html',
  standalone: false,
})
export class InboxSettingsComponent implements OnInit {
  private inboxService = inject(BackendConfigurationPnInboxService);
  private toastr = inject(ToastrService);
  private translate = inject(TranslateService);

  settings: InboxSettingsModel | null = null;
  allowList = '';
  copied = false;

  ngOnInit(): void { this.inboxService.getSettings().subscribe(res => { if (res?.success) { this.apply(res.model); } }); }

  private apply(model: InboxSettingsModel): void {
    this.settings = model;
    this.allowList = model.senderRules.filter(r => r.kind === 0).map(r => r.pattern).join(', ');
  }

  copy(): void {
    if (!this.settings?.address) { return; }
    navigator.clipboard.writeText(this.settings.address).then(() => (this.copied = true), () => (this.copied = false));
  }

  rotate(): void {
    this.inboxService.rotateAddress().subscribe(res => {
      if (res?.success) { this.apply(res.model); this.toastr.success(this.translate.instant('New address created. The old address keeps working for 7 days.')); }
    });
  }

  save(): void {
    if (!this.settings) { return; }
    const blocks = this.settings.senderRules.filter(r => r.kind === 1);
    const allows = this.allowList.split(',').map(s => s.trim()).filter(s => s).map(pattern => ({pattern, kind: 0 as const}));
    this.inboxService.updateSettings({...this.settings, senderRules: [...allows, ...blocks]}).subscribe(res => {
      if (res?.success) { this.toastr.success(this.translate.instant('Saved')); }
    });
  }
}
```

`components/inbox-settings/inbox-settings.component.html`:

```html
<mat-card class="p-3 d-flex flex-column gap-12" *ngIf="settings">
  <app-archive-section-nav></app-archive-section-nav>
  <h2 style="margin: 0">{{ 'Email settings' | translate }}</h2>

  <section class="d-flex flex-column gap-8">
    <h3 class="mb-0">{{ 'Address for the inbox' | translate }}</h3>
    <p class="mb-0">{{ 'PDFs in emails sent or forwarded to this address arrive in the Inbox. Emails without a PDF are ignored; several PDFs in one email are fine.' | translate }}</p>
    <div class="d-flex flex-row flex-wrap align-items-center gap-12">
      <mat-form-field style="min-width: 360px">
        <mat-label>{{ 'Archive email' | translate }}</mat-label>
        <input matInput id="inboxAddress" readonly [value]="settings.address">
      </mat-form-field>
      <button mat-stroked-button id="inboxCopyBtn" (click)="copy()"><mat-icon>content_copy</mat-icon>{{ (copied ? 'Copied' : 'Copy') | translate }}</button>
      <button mat-button color="primary" id="inboxRotateBtn" (click)="rotate()"><mat-icon>autorenew</mat-icon>{{ 'Create new address' | translate }}</button>
    </div>
  </section>

  <section class="d-flex flex-column gap-8">
    <h3 class="mb-0">{{ 'Who can send' | translate }}</h3>
    <p class="mb-0">{{ 'Everyone with a user in this account can send. Also allow (addresses or domains, separated by commas):' | translate }}</p>
    <mat-form-field>
      <mat-label>{{ 'Allowed senders' | translate }}</mat-label>
      <textarea matInput id="inboxRulesInput" rows="3" [(ngModel)]="allowList" placeholder="@example-supplier.dk, scanner@example.org"></textarea>
    </mat-form-field>
    <mat-radio-group [(ngModel)]="settings.unknownSenderPolicy" class="d-flex flex-column gap-8">
      <mat-radio-button id="inboxPolicyHold" value="hold">{{ 'Hold documents from other senders in the Inbox until someone approves the sender' | translate }}</mat-radio-button>
      <mat-radio-button id="inboxPolicyRefuse" value="refuse">{{ 'Refuse documents from other senders' | translate }}</mat-radio-button>
    </mat-radio-group>
  </section>

  <section class="d-flex flex-column gap-8">
    <h3 class="mb-0">{{ 'Suggestions for property and tags' | translate }}</h3>
    <p class="mb-0">{{ 'New documents automatically get suggestions for property and tags. Microting may check the suggestions before the document reaches your Inbox. You always decide what is filed.' | translate }}</p>
  </section>

  <section class="d-flex flex-column gap-8">
    <h3 class="mb-0">{{ 'Retention' | translate }}</h3>
    <p class="mb-0">{{ 'Filed documents stay in the archive. Rejected documents, and documents not filed within 30 days, are deleted. The email text itself is never stored.' | translate }}</p>
  </section>

  <div class="d-flex justify-content-end">
    <button mat-flat-button color="primary" id="inboxSaveBtn" (click)="save()">{{ 'Save' | translate }}</button>
  </div>
</mat-card>
```

- [ ] **Step 6: Module, routing, claim constant and translations**

In `files.routing.ts`, add to `routes`:

```ts
  { path: 'inbox', component: InboxContainerComponent },
  { path: 'settings', component: InboxSettingsComponent },
```

Import them from `./components`.

In `components/index.ts`:

```ts
export * from './archive-section-nav/archive-section-nav.component';
export * from './inbox-container/inbox-container.component';
export * from './inbox-review-dialog/inbox-review-dialog.component';
export * from './inbox-settings/inbox-settings.component';
```

In `files.module.ts`:
- add the four components to `declarations`;
- add `MatSelectModule` (`@angular/material/select`) and `MatRadioModule` (`@angular/material/radio`) to
  `imports`. `FormsModule`, `MatDialogModule`, `MatIconModule`, `MatInputModule`, `PdfViewerModule` and the
  router are already imported.

In `enums/backend-configuration-pn-claims.const.ts`, add `enableInbox: 'inbox_enable',` after
`enableFilesManagement`.

Translations: add every new UI string to `i18n/enUS.ts` (key = value) and `i18n/da.ts`.

| Key | da |
|---|---|
| Inbox | Indbakke |
| Archive | Arkiv |
| Email settings | Indstillinger e-mail |
| Archive sections | Arkiv sektioner |
| PDFs sent or forwarded to your archive address. Property and tags are suggested from the document; you approve before anything is filed. | PDF’er sendt eller videresendt til jeres arkiv-adresse. Ejendom og tags foreslås ud fra dokumentet; I godkender, før noget arkiveres. |
| All open | Alle åbne |
| Ready to file | Klar til gennemgang |
| Being prepared | Klargøres |
| Approve sender | Godkend afsender |
| Could not be read | Kan ikke læses |
| Filed | Arkiveret |
| Rejected | Afvist |
| PDF name, sender … | PDF-navn, afsender … |
| PDF / subject | Primær PDF / emne |
| Sender | Afsender |
| Received | Modtaget |
| Action | Handling |
| Ready by | Klar senest |
| Checked by Microting | Kontrolleret af Microting |
| Review | Gennemgå |
| Reject | Afvis |
| Sender approved | Afsenderen er godkendt |
| No documents waiting. Forward a mail with a PDF to your archive address to get started. | Ingen dokumenter venter. Videresend en mail med en PDF til jeres arkiv-adresse for at komme i gang. |
| Review and file | Gennemgå og arkivér |
| Microting checked these suggestions before they reached you. | Microting har kontrolleret forslagene, før dokumentet kom i jeres indbakke. |
| Point at a suggestion to see why it was suggested and where it appears in the document. | Peg på et forslag for at se, hvorfor det er foreslået, og hvor det står i dokumentet. |
| page | side |
| File name in the archive | Filnavn i arkivet |
| Reject document | Afvis dokument |
| File in archive | Arkivér |
| Undo | Fortryd |
| Close | Luk |
| Address for the inbox | Adresse til indbakken |
| PDFs in emails sent or forwarded to this address arrive in the Inbox. Emails without a PDF are ignored; several PDFs in one email are fine. | PDF’er i e-mails sendt eller videresendt hertil kommer i Indbakken. E-mails uden PDF ignoreres; flere PDF’er i samme mail er tilladt. |
| Archive email | Arkiv e-mail |
| Copy | Kopiér |
| Copied | Kopieret |
| Create new address | Lav ny adresse |
| New address created. The old address keeps working for 7 days. | Ny adresse oprettet. Den gamle virker i 7 dage endnu. |
| Who can send | Hvem må sende |
| Everyone with a user in this account can send. Also allow (addresses or domains, separated by commas): | Alle med en bruger i denne konto må sende. Tillad også (adresser eller domæner, adskilt med komma): |
| Allowed senders | Tilladte afsendere |
| Hold documents from other senders in the Inbox until someone approves the sender | Hold dokumenter fra andre afsendere i Indbakken, til nogen godkender afsenderen |
| Refuse documents from other senders | Afvis dokumenter fra andre afsendere |
| Suggestions for property and tags | Forslag til ejendom og tags |
| New documents automatically get suggestions for property and tags. Microting may check the suggestions before the document reaches your Inbox. You always decide what is filed. | Nye dokumenter får automatisk forslag til ejendom og tags. Microting kan kontrollere forslagene, før dokumentet kommer i Indbakken. I bestemmer altid selv, hvad der arkiveres. |
| Retention | Opbevaring |
| Filed documents stay in the archive. Rejected documents, and documents not filed within 30 days, are deleted. The email text itself is never stored. | Arkiverede dokumenter bliver i arkivet. Afviste dokumenter og dokumenter, der ikke er arkiveret efter 30 dage, slettes. Selve mailteksten gemmes aldrig. |
| Saved | Gemt |

Keys that already exist (`Status`, `Search`, `Property`, `Tags`, `Save`) are reused. Grep first, and do not
duplicate them.

- [ ] **Step 7: Styles**

Search `eform-angular-frontend/eform-client/src/scss` for existing chip and confidence styles first:
`grep -rn "arkiv-inbox\|status-chip\|ejendom-chip" eform-angular-frontend/eform-client/src/scss`. If
`.inbox-chip` and `.inbox-dot--*` have no equivalent, add them in a **separate PR to `eform-angular-frontend`**
(target `stable`) in `src/scss/components/_inbox.scss`, imported from `src/scss/components/_index.scss`
(or wherever component partials are imported):

```scss
.inbox-chip { display: inline-flex; align-items: center; gap: 6px; padding: 2px 8px; margin: 0 4px 4px 0;
  border-radius: var(--radius-sm, 4px); background: var(--md-surface-variant); color: var(--md-on-surface);
  font-size: 0.8rem; font-weight: 500; }
.inbox-dot { width: 9px; height: 9px; border-radius: 50%; flex-shrink: 0; display: inline-block; box-sizing: border-box; }
.inbox-dot--high { background: var(--md-primary); }
.inbox-dot--medium { background: color-mix(in srgb, var(--md-primary) 55%, var(--md-on-surface-variant)); }
.inbox-dot--low { border: 2px solid color-mix(in srgb, var(--md-alert) 65%, var(--md-on-surface-variant)); }
```

That PR is reviewed and merged before this plugin PR is released. Without it, the chips render as plain text,
which is not a functional break.

- [ ] **Step 8: Build check**

Angular builds need `node_modules`. If `eform-client/node_modules` exists, run `yarn build` from
`eform-client`; otherwise state in the PR that the Angular build is verified by CI only.
Then run `dotnet build eFormAPI/Plugins/BackendConfiguration.Pn/BackendConfiguration.Pn.sln`.

- [ ] **Step 9: Review gate, then commit `feat(inbox): Indbakke, review dialog and email settings pages`**, staging each new or changed file by name.

---

### Task 7: Playwright coverage and the CI signing key

**Files:**
- Create: `eform-client/playwright/e2e/plugins/backend-configuration-pn/inbox-hub-seed.ts`
- Create: `eform-client/playwright/e2e/plugins/backend-configuration-pn/i/inbox.spec.ts`
- Modify: `.github/workflows/dotnet-core-pr.yml:173` and `.github/workflows/dotnet-core-master.yml:169` (the
  `docker run` of the app container)

**Interfaces:**
- Consumes:
  - the Task 3 hub endpoints and the Task 6 element ids;
  - `waitForApiResponse`, `UI_TIMEOUT`, `API_TIMEOUT`, `customerDatabase` and `DatabaseConfigurationConstants.customerNo`;
  - `LoginPage`, `BackendConfigurationPropertiesPage`.
- Produces: `seedInboxDocument(page, opts): Promise<string>`, which returns the hub document id.

- [ ] **Step 1: Add the CI-only signing key to the app container**

In both workflow files, add this to the `docker run --name my-container …` line, right after
`-e ALLOW_FAKE_TRANSLATE=true`:

```
-e InboundMailHub__TenantSigningKey=ci-only-inbox-signing-key-0123456789abcdef
```

This key exists only in CI and signs nothing in production. `HubUrl` stays empty, so outbound calls are
skipped (Task 5).

- [ ] **Step 2: Write the seeding helper**

`inbox-hub-seed.ts`:

```ts
import { APIRequestContext } from '@playwright/test';
import { createHash, createHmac, randomUUID } from 'crypto';
import { DatabaseConfigurationConstants } from '../../../Constants/DatabaseConfigurationConstants';
import { API_TIMEOUT } from './wait-helpers';

/** Must equal InboundMailHub__TenantSigningKey in the CI workflows. CI-only, not a secret. */
export const CI_INBOX_SIGNING_KEY = 'ci-only-inbox-signing-key-0123456789abcdef';
const BASE = 'http://localhost:4200';

function signedHeaders(method: string, path: string, body: Buffer, contentType: string) {
  const customerNo = String(DatabaseConfigurationConstants.customerNo);
  const date = new Date().toUTCString();
  const requestId = randomUUID();
  const bodyHash = createHash('sha256').update(body).digest('hex');
  const canonical = [method, path, customerNo, requestId, date, bodyHash].join('\n');
  const sig = createHmac('sha256', CI_INBOX_SIGNING_KEY).update(canonical).digest('hex');
  return {
    Authorization: `HMAC-SHA256 ${sig}`, Date: date, 'X-Request-Id': requestId,
    'X-Customer-No': customerNo, 'Content-Type': contentType,
  };
}

async function post(request: APIRequestContext, path: string, body: Buffer, contentType: string, what: string) {
  const res = await request.post(BASE + path, {
    data: body, headers: signedHeaders('POST', path, body, contentType), timeout: API_TIMEOUT,
  });
  if (!res.ok()) {
    throw new Error(`Seeding the inbox failed at ${what}: HTTP ${res.status()} ${await res.text()}`);
  }
  return res;
}

/** A minimal valid one-page PDF. */
export function tinyPdf(text: string): Buffer {
  const stream = `BT /F1 12 Tf 72 720 Td (${text}) Tj ET`;
  const objects = [
    '<< /Type /Catalog /Pages 2 0 R >>',
    '<< /Type /Pages /Kids [3 0 R] /Count 1 >>',
    '<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>',
    `<< /Length ${stream.length} >>\nstream\n${stream}\nendstream`,
    '<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>',
  ];
  let pdf = '%PDF-1.4\n';
  const offsets: number[] = [];
  objects.forEach((o, i) => { offsets.push(pdf.length); pdf += `${i + 1} 0 obj\n${o}\nendobj\n`; });
  const xref = pdf.length;
  pdf += `xref\n0 ${objects.length + 1}\n0000000000 65535 f \n` +
    offsets.map(o => `${String(o).padStart(10, '0')} 00000 n \n`).join('') +
    `trailer\n<< /Size ${objects.length + 1} /Root 1 0 R >>\nstartxref\n${xref}\n%%EOF`;
  return Buffer.from(pdf, 'latin1');
}

export interface SeedOptions {
  fromAddress: string;
  fileName: string;
  /** Property and tag ids to suggest (looked up by the spec via the UI-created property/tag). */
  suggestions?: { kind: 'property' | 'tag'; targetId: number }[];
  deliver?: boolean;
}

export async function seedInboxDocument(request: APIRequestContext, opts: SeedOptions): Promise<string> {
  const hubDocumentId = randomUUID();
  const arrived = Buffer.from(JSON.stringify({
    hubDocumentId, fromAddress: opts.fromAddress, subject: 'Fwd: Servicerapport', receivedAt: new Date().toISOString(),
    fileName: opts.fileName, sizeBytes: 1000, spfResult: 'not-checked', dkimResult: 'not-checked', readyBy: null,
  }));
  await post(request, '/api/backend-configuration-pn/inbox/hub/arrived', arrived, 'application/json', 'arrived');
  if (opts.deliver === false) { return hubDocumentId; }

  const boundary = `----inbox${hubDocumentId}`;
  const metadata = JSON.stringify({
    hubDocumentId, pageCount: 1, reviewedByMicroting: false,
    suggestions: (opts.suggestions ?? []).map(s => ({ ...s, source: 'textMatch', confidence: 0.5, evidence: 'Nordvej 12', page: 1, reason: 'Adressen står i dokumentet.' })),
  });
  const body = Buffer.concat([
    Buffer.from(`--${boundary}\r\nContent-Disposition: form-data; name="metadata"\r\nContent-Type: application/json\r\n\r\n${metadata}\r\n`),
    Buffer.from(`--${boundary}\r\nContent-Disposition: form-data; name="file"; filename="${opts.fileName}"\r\nContent-Type: application/pdf\r\n\r\n`),
    tinyPdf('Adresse: Nordvej 12'),
    Buffer.from(`\r\n--${boundary}--\r\n`),
  ]);
  await post(request, '/api/backend-configuration-pn/inbox/hub/deliver', body, `multipart/form-data; boundary=${boundary}`, 'deliver');
  return hubDocumentId;
}
```

Check the import path of `DatabaseConfigurationConstants` by grepping how `db-helpers.ts` imports it, and use
the same path.

- [ ] **Step 3: Write the spec**

`i/inbox.spec.ts`:

```ts
import { test, expect, Page } from '@playwright/test';
import { LoginPage } from '../../../../Page objects/Login.page';
import { generateRandmString } from '../../../../helper-functions';
import { BackendConfigurationPropertiesPage, PropertyCreateUpdate } from '../BackendConfigurationProperties.page';
import { customerDatabase, runMariadbSql } from '../db-helpers';
import { seedInboxDocument } from '../inbox-hub-seed';
import { API_TIMEOUT, UI_TIMEOUT, waitForApiResponse } from '../wait-helpers';

/** The CI admin's login email. Import the constant LoginPage.login() signs in with (grep Login.page.ts) — never a literal address. */
const ADMIN_EMAIL: string = /* that constant */ '';

/**
 * Indbakke (inbound mail inbox), tenant side. Documents are seeded through the
 * signed hub endpoints with the CI-only key, exactly as the central service would.
 * I1 file a document → it is in Arkiv. I2 approve an unknown sender. I3 reject.
 * I4 settings save and rotate.
 */
test.describe.serial('Indbakke', () => {
  let page: Page;
  const property: PropertyCreateUpdate = {
    name: `Inbox ${generateRandmString(5)}`, chrNumber: generateRandmString(5),
    address: 'Nordvej 12, 8000 Aarhus C', cvrNumber: '1111111',
  };
  let propertyId = 0;

  test.beforeAll(async ({ browser }) => {
    page = await browser.newPage();
    await page.goto('http://localhost:4200');
    await new LoginPage(page).login();
    const properties = new BackendConfigurationPropertiesPage(page);
    await properties.goToProperties();
    await properties.createProperty(property);
    const id = await runMariadbSql(
      `SELECT Id FROM Properties WHERE Name = '${property.name.replace(/'/g, "''")}' AND WorkflowState <> 'removed'`,
      'read the seeded property id', customerDatabase('eform-backend-configuration-plugin'));
    propertyId = Number(id.trim());
    expect(propertyId, 'property id from the database').toBeGreaterThan(0);
  });

  test.afterAll(async () => { await page.close(); });

  async function openInbox() {
    const listed = waitForApiResponse(page, 'GET inbox list',
      r => r.url().includes('/api/backend-configuration-pn/inbox?') || r.url().endsWith('/api/backend-configuration-pn/inbox'), API_TIMEOUT);
    await page.goto('http://localhost:4200/plugins/backend-configuration-pn/files/inbox');
    await listed;
    await expect(page.locator('#inboxTable')).toBeVisible({ timeout: UI_TIMEOUT });
  }

  test('I1 file a delivered document into the archive', async () => {
    const fileName = `Servicerapport ${generateRandmString(4)}.pdf`;
    await seedInboxDocument(page.request, {
      fromAddress: ADMIN_EMAIL, fileName, suggestions: [{ kind: 'property', targetId: propertyId }],
    });
    await openInbox();
    const row = page.locator('#inboxTable .inbox-row', { hasText: fileName });
    await expect(row).toHaveCount(1, { timeout: UI_TIMEOUT });
    await row.getByRole('button', { name: /Gennemgå|Review/ }).click();

    const dialog = page.locator('mat-dialog-container');
    const choice = dialog.locator(`.inbox-choice[data-kind="property"][data-target-id="${propertyId}"]`);
    await expect(choice).toHaveAttribute('aria-pressed', 'true', { timeout: UI_TIMEOUT });
    const filed = waitForApiResponse(page, 'POST inbox file', r => /\/inbox\/\d+\/file$/.test(r.url()) && r.request().method() === 'POST', API_TIMEOUT);
    await dialog.locator('#inboxFileBtn').click();
    expect((await filed).ok()).toBe(true);
    await expect(dialog.locator('#inboxUndoBtn')).toBeVisible({ timeout: UI_TIMEOUT });
    await dialog.locator('#inboxCloseBtn').click();
    await expect(dialog).toBeHidden({ timeout: UI_TIMEOUT });

    const archive = waitForApiResponse(page, 'POST files index', r => r.url().endsWith('/api/backend-configuration-pn/files') && r.request().method() === 'POST', API_TIMEOUT);
    await page.locator('#archiveNavArchive').click();
    await archive;
    await expect(page.locator('app-files-table').getByText(fileName.replace(/\.pdf$/, ''))).toBeVisible({ timeout: UI_TIMEOUT });
  });

  test('I2 approve an unknown sender', async () => {
    const fileName = `scan_${generateRandmString(4)}.pdf`;
    await seedInboxDocument(page.request, { fromAddress: `post-${generateRandmString(4)}@example.net`, fileName, deliver: false });
    await openInbox();
    const row = page.locator('#inboxTable .inbox-row', { hasText: fileName });
    await expect(row).toContainText(/Godkend afsender|Approve sender/, { timeout: UI_TIMEOUT });
    const approved = waitForApiResponse(page, 'POST approve-sender', r => r.url().includes('/approve-sender'), API_TIMEOUT);
    await row.getByRole('button', { name: /Godkend afsender|Approve sender/ }).click();
    expect((await approved).ok()).toBe(true);
    await expect(row).toContainText(/Klargøres|Being prepared/, { timeout: UI_TIMEOUT });
  });

  test('I3 reject a document', async () => {
    const fileName = `Faktura ${generateRandmString(4)}.pdf`;
    await seedInboxDocument(page.request, { fromAddress: ADMIN_EMAIL, fileName });
    await openInbox();
    const row = page.locator('#inboxTable .inbox-row', { hasText: fileName });
    await row.getByRole('button', { name: /Gennemgå|Review/ }).click();
    const dialog = page.locator('mat-dialog-container');
    const rejected = waitForApiResponse(page, 'POST reject', r => /\/inbox\/\d+\/reject$/.test(r.url()), API_TIMEOUT);
    await dialog.locator('#inboxRejectBtn').click();
    expect((await rejected).ok()).toBe(true);
    await expect(dialog).toBeHidden({ timeout: UI_TIMEOUT });
    await expect(row).toHaveCount(0, { timeout: UI_TIMEOUT });
  });

  test('I4 settings: save allowed senders and rotate the address', async () => {
    const loaded = waitForApiResponse(page, 'GET inbox settings', r => r.url().endsWith('/inbox/settings') && r.request().method() === 'GET', API_TIMEOUT);
    await page.goto('http://localhost:4200/plugins/backend-configuration-pn/files/settings');
    await loaded;
    const address = page.locator('#inboxAddress');
    await expect(address).toHaveValue(/^\d+-[a-z2-7]{10}@indbakke\.microting\.dk$/, { timeout: UI_TIMEOUT });
    const first = await address.inputValue();

    await page.locator('#inboxRulesInput').fill('@example-supplier.dk');
    const saved = waitForApiResponse(page, 'PUT inbox settings', r => r.url().endsWith('/inbox/settings') && r.request().method() === 'PUT', API_TIMEOUT);
    await page.locator('#inboxSaveBtn').click();
    expect((await saved).ok()).toBe(true);

    const rotated = waitForApiResponse(page, 'POST rotate-address', r => r.url().endsWith('/rotate-address'), API_TIMEOUT);
    await page.locator('#inboxRotateBtn').click();
    expect((await rotated).ok()).toBe(true);
    await expect(address).not.toHaveValue(first, { timeout: UI_TIMEOUT });
  });
});
```

Before running, check four things:
- `ADMIN_EMAIL` must be the constant `LoginPage.login()` signs in with, imported (not a literal), so the sender
  counts as allowed. Grep `LoginPage` and the Playwright constants for it.
- That the admin role has the new `inbox_enable` claim. Admin roles get every plugin permission in this
  platform. If the inbox tab is missing, grant the permission in the spec's `beforeAll` through the existing
  security-group page helpers.
- The names of `createProperty` / `goToProperties` in `BackendConfigurationProperties.page.ts`.
- The relative import depth (`../../../../`) against an existing spec in `i/`.

- [ ] **Step 4: Review gate, commit, push and open the PR**

```bash
git add eform-client/playwright/e2e/plugins/backend-configuration-pn/inbox-hub-seed.ts \
  eform-client/playwright/e2e/plugins/backend-configuration-pn/i/inbox.spec.ts \
  .github/workflows/dotnet-core-pr.yml .github/workflows/dotnet-core-master.yml
git commit -m "test(inbox): Playwright coverage for the Indbakke

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
git push -u origin feat/inbox-tenant-side
gh pr create --base stable --title "feat: PDF archive Indbakke (tenant side)" --body "$(cat <<'EOF'
Tenant side of the PDF archive Indbakke (spec: docs/superpowers/specs/2026-10-04-inbox-tenant-side-design.md):

- signed endpoints for the central inbound mail service (arrived, catalog, deliver, failed)
- manager endpoints: list, review, file (one transaction via FileArchiver), undo, reject, sender decisions
- Indstillinger e-mail: address, rotation with 7-day grace, allowed senders, unknown-sender policy
- Angular Indbakke, review dialog and settings; new permission `inbox_enable`
- Playwright spec i/inbox.spec.ts seeds documents via the signed endpoints with a CI-only key

Not verified locally: integration tests and Playwright run in CI only.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

- [ ] **Step 5: Watch CI to a verdict, then consider the Copilot review**

Run `gh pr checks <n>` until every check is done. Classify each red check by its failed step: infrastructure
(`Wait for app`, `DB Configuration`, `yarn install`) or a real failure in a named spec. Compare against
`stable`'s latest run. Fix real failures through the review gate. Read the Copilot review comments, act on the
valid ones and explain why the others do not apply. Remind the user to test in the browser on the staging
tenant after release.
