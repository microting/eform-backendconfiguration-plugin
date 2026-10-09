/*
The MIT License (MIT)

Copyright (c) 2007 - 2026 Microting A/S

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
using Microting.eForm.Infrastructure.Constants;
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

// A worker on a property and whether the SDK worker has resigned (resigned workers keep their PropertyWorker row).
public sealed record TailBiteWorkerSite(int SiteId, bool Resigned);

public sealed record RegistrationLocationInput(int LocationId, int Minor, int Severe);
public sealed record CreateRegistrationCommand(Guid ClientUuid, int PropertyId, DateTime RegisteredAtUtc,
    IReadOnlyList<RegistrationLocationInput> Locations, IReadOnlyList<int> ActionTypeIds, string? Comment);
// LocationName: the outbreak's summing location, filled by the registration service after evaluation and on replay.
public sealed record OutbreakOutcome(int OutbreakId, bool Opened, string LocationName = "");
public sealed record CreateRegistrationResult(int RegistrationId, IReadOnlyList<OutbreakOutcome> Outbreaks);
public sealed record RecentRegistration(int Id, DateTime EffectiveAt, IReadOnlyList<RegistrationLocationInput> Locations, bool Cancelled);

// Setup (Task 8). Depth is the absolute distance from the root (root = 0); Removed nodes stay in the tree for history.
public sealed record LocationNode(int Id, int? ParentId, string Name, int SortOrder, string QrCode, int Depth, bool Removed);
public sealed record ActionTypeDto(int Id, string Code, string Name, int SortOrder);
public sealed record LocationTree(int PropertyId, long TreeVersion, IReadOnlyList<LocationNode> Locations, IReadOnlyList<ActionTypeDto> ActionTypes);
public sealed record RuleInput(int LocationId, int? MinBittenPigs, int? MinSevere, int WindowDays, int CountDepth);
public sealed record RulePreview(int OutbreaksWouldOpen, IReadOnlyDictionary<int, int> PerSummingLocation);

// Outbreaks (Task 9).
public sealed record FactorAnswers(bool Water, bool Feed, bool ActivityMaterial, bool Climate, bool Health, bool Management)
{
    public IReadOnlyDictionary<TailBiteFactor, bool> ToDictionary() => new Dictionary<TailBiteFactor, bool>
    {
        [TailBiteFactor.Water] = Water, [TailBiteFactor.Feed] = Feed, [TailBiteFactor.ActivityMaterial] = ActivityMaterial,
        [TailBiteFactor.Climate] = Climate, [TailBiteFactor.Health] = Health, [TailBiteFactor.Management] = Management
    };

    public static FactorAnswers FromAssessment(TailBiteRiskAssessment a) => new(a.Water, a.Feed, a.ActivityMaterial, a.Climate, a.Health, a.Management);
}
public sealed record ActionInput(TailBiteFactor Factor, string Description, int ResponsibleSiteId, DateTime FollowUpDate);
public sealed record OutbreakSummary(int Id, int LocationId, DateTime OpenedAt, bool Assessed, int OpenActions, bool Closed,
    int BittenPigs = 0, int SeverePigs = 0);
public sealed record OutbreakActionDetail(int Id, TailBiteFactor Factor, string Description, int ResponsibleSiteId, DateTime FollowUpDate,
    DateTime? DoneAt, DateTime? WithdrawnAt);
public sealed record OutbreakDetail(OutbreakSummary Summary, int RuleId, int RuleVersion, IReadOnlyList<int> RegistrationIds,
    FactorAnswers? Answers, IReadOnlyList<OutbreakActionDetail> Actions);

// A photo belongs to a registration only when the uuid, the property AND the uploading site all match (Global Constraints),
// and only once its bytes are stored. Every query that joins photos to registrations goes through this; Task 13 inlines
// the same predicate.
public static class TailBitePhotoOwnership
{
    /// <summary>The bytes are stored: a row with SdkUploadedDataId 0 is only a reservation of the photo uuid.</summary>
    public static readonly Expression<Func<TailBiteRegistrationPhoto, bool>> IsStored = p => p.SdkUploadedDataId != 0;

    /// <summary>A reservation released because storing its bytes failed; a retry of the same photo may claim it again.</summary>
    public static readonly Expression<Func<TailBiteRegistrationPhoto, bool>> FailedReservation
        = p => p.SdkUploadedDataId == 0 && p.WorkflowState == Constants.WorkflowStates.Removed;

    public static readonly Func<TailBiteRegistrationPhoto, bool> IsStoredFunc = IsStored.Compile();
    public static readonly Func<TailBiteRegistrationPhoto, bool> FailedReservationFunc = FailedReservation.Compile();

    public static Expression<Func<TailBiteRegistrationPhoto, bool>> BelongsTo(TailBiteRegistration reg)
    {
        var (clientUuid, propertyId, siteId) = (reg.ClientUuid, reg.PropertyId, reg.SiteId);
        Expression<Func<TailBiteRegistrationPhoto, bool>> owner
            = p => p.RegistrationClientUuid == clientUuid && p.PropertyId == propertyId && p.UploadedBySiteId == siteId;
        var stored = new Rebind(IsStored.Parameters[0], owner.Parameters[0]).Visit(IsStored.Body);
        return Expression.Lambda<Func<TailBiteRegistrationPhoto, bool>>(Expression.AndAlso(owner.Body, stored), owner.Parameters);
    }

    /// <summary>
    /// The reservation is still held by the upload that wrote <paramref name="stamp"/> into UpdatedAt, or by nobody
    /// (a failed reservation), so that upload may record its stored bytes.
    /// </summary>
    public static Expression<Func<TailBiteRegistrationPhoto, bool>> HeldByOrFree(DateTime stamp)
    {
        Expression<Func<TailBiteRegistrationPhoto, bool>> held = p => p.UpdatedAt == stamp;
        var free = new Rebind(FailedReservation.Parameters[0], held.Parameters[0]).Visit(FailedReservation.Body);
        return Expression.Lambda<Func<TailBiteRegistrationPhoto, bool>>(Expression.OrElse(held.Body, free), held.Parameters);
    }

    // Points one lambda's body at another lambda's parameter, so the two bodies can be combined.
    private sealed class Rebind(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : node;
    }
}

// Declared here, implemented in Task 12. The registration service calls it after commit when it is registered.
public interface ITailBiteOutbreakNotifier { Task NotifyOpenedAsync(int propertyId, IReadOnlyList<OutbreakOutcome> outcomes); }
