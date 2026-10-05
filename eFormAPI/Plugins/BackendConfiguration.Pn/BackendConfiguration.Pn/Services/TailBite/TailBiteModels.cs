/*
The MIT License (MIT)

Copyright (c) 2007 - 2022 Microting A/S

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/

#nullable enable

namespace BackendConfiguration.Pn.Services.TailBite;

using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;

public static class TailBiteDefaults
{
    // The proposed seeded default rule (Global Constraints): root, 5 bitten pigs or 1 severe within 7 days, summed per stable.
    public static TailBiteRule DefaultRule(int rootLocationId) => new()
    {
        LocationId = rootLocationId, MinBittenPigs = 5, MinSevere = 1, WindowDays = 7, CountDepth = 1
    };

    public static readonly (string Code, string Name)[] ActionTypes =
    [
        ("FLYTTET", "Flyttet grise"), ("TAGET_UD", "Taget ud"), ("BEHANDLET", "Behandlet"), ("REB", "Reb / rodemateriale"),
        ("HALM", "Halm"), ("PULVER", "Pulver på hale"), ("ANDET", "Andet")
    ];

    // 16 random bytes, base64url, no padding: 22 characters.
    public static string NewQrCode()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(16)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

public sealed record RegistrationLocationInput(int LocationId, int Minor, int Severe);
public sealed record CreateRegistrationCommand(Guid ClientUuid, int PropertyId, DateTime RegisteredAtUtc,
    IReadOnlyList<RegistrationLocationInput> Locations, IReadOnlyList<int> ActionTypeIds, string? Comment);
public sealed record OutbreakOutcome(int OutbreakId, bool Opened);
public sealed record CreateRegistrationResult(int RegistrationId, IReadOnlyList<OutbreakOutcome> Outbreaks);
public sealed record RecentRegistration(int Id, DateTime EffectiveAt, IReadOnlyList<RegistrationLocationInput> Locations, bool Cancelled);

// Setup (Task 8). Depth is the absolute distance from the root (root = 0); Removed nodes stay in the tree for history.
public sealed record LocationNode(int Id, int? ParentId, string Name, int SortOrder, string QrCode, int Depth, bool Removed);
public sealed record LocationTree(int PropertyId, long TreeVersion, IReadOnlyList<LocationNode> Locations, IReadOnlyList<(int Id, string Code, string Name)> ActionTypes);
public sealed record RuleInput(int LocationId, int? MinBittenPigs, int? MinSevere, int WindowDays, int CountDepth);
public sealed record RulePreview(int OutbreaksWouldOpen, IReadOnlyDictionary<int, int> PerSummingLocation);

// A photo belongs to a registration only when the uuid, the property AND the uploading site all match (Global Constraints).
// Every query that joins photos to registrations goes through this; Task 13 inlines the same predicate.
public static class TailBitePhotoOwnership
{
    public static Expression<Func<TailBiteRegistrationPhoto, bool>> BelongsTo(TailBiteRegistration reg)
    {
        var (clientUuid, propertyId, siteId) = (reg.ClientUuid, reg.PropertyId, reg.SiteId);
        return p => p.RegistrationClientUuid == clientUuid && p.PropertyId == propertyId && p.UploadedBySiteId == siteId;
    }
}

// Declared here, implemented in Task 12. The registration service calls it after commit when it is registered.
public interface ITailBiteOutbreakNotifier { Task NotifyOpenedAsync(int propertyId, IReadOnlyList<OutbreakOutcome> outcomes); }
