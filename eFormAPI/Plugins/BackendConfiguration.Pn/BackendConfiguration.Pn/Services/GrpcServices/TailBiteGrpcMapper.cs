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
using System;
using System.Globalization;
using System.Linq;
using BackendConfiguration.Pn.Grpc.TailBite;
using BackendConfiguration.Pn.Services.TailBite;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microting.EformBackendConfigurationBase.Infrastructure.Data.Entities;

namespace BackendConfiguration.Pn.Services.GrpcServices;

/// <summary>
/// Wire parsing (ids, UUIDs, timestamps, factors) and proto/model mapping for <see cref="TailBiteGrpcService"/>.
/// Every parse failure is an <see cref="RpcException"/> with <see cref="StatusCode.InvalidArgument"/>.
/// </summary>
internal static class TailBiteGrpcMapper
{
    public static RpcException Invalid(string message) => new(new Status(StatusCode.InvalidArgument, message));

    // ---------- wire parsing ----------

    public static int Id(string raw, string field)
        => int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value > 0
            ? value
            : throw Invalid($"{field} must be a positive numeric id.");

    public static Guid Uuid(string raw, string field)
        => Guid.TryParseExact(raw, "D", out var value) ? value : throw Invalid($"{field} must be a UUID.");

    /// <summary>A required timestamp: missing or outside the DateTime range is InvalidArgument.</summary>
    public static DateTime Utc(Timestamp? value, string field)
        => OptionalUtc(value, field) ?? throw Invalid($"{field} is required.");

    /// <summary>An optional timestamp: null when unset, InvalidArgument when outside the DateTime range.</summary>
    public static DateTime? OptionalUtc(Timestamp? value, string field)
    {
        if (value is null) return null;
        try
        {
            return value.ToDateTime();
        }
        catch (InvalidOperationException)
        {
            throw Invalid($"{field} is out of range.");
        }
    }

    // Request side: UNSPECIFIED (the proto3 default, i.e. the field was not set) and values this server does not know
    // (a newer client, or garbage) are rejected rather than guessed at.
    public static TailBiteFactor Factor(TbFactor wire) => wire switch
    {
        TbFactor.Water => TailBiteFactor.Water,
        TbFactor.Feed => TailBiteFactor.Feed,
        TbFactor.ActivityMaterial => TailBiteFactor.ActivityMaterial,
        TbFactor.Climate => TailBiteFactor.Climate,
        TbFactor.Health => TailBiteFactor.Health,
        TbFactor.Management => TailBiteFactor.Management,
        _ => throw Invalid("factor is required and must be a known value.")
    };

    // ---------- model to wire ----------

    public static string S(int value) => value.ToString(CultureInfo.InvariantCulture);

    public static Timestamp Ts(DateTime value) => Timestamp.FromDateTime(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    public static TbFactor Factor(TailBiteFactor factor) => factor switch
    {
        TailBiteFactor.Water => TbFactor.Water,
        TailBiteFactor.Feed => TbFactor.Feed,
        TailBiteFactor.ActivityMaterial => TbFactor.ActivityMaterial,
        TailBiteFactor.Climate => TbFactor.Climate,
        TailBiteFactor.Health => TbFactor.Health,
        TailBiteFactor.Management => TbFactor.Management,
        _ => throw new InvalidOperationException($"Unmapped tail-bite factor {factor}.")
    };

    public static TbLocationTree MapTree(LocationTree tree)
    {
        var response = new TbLocationTree { PropertyId = S(tree.PropertyId), TreeVersion = tree.TreeVersion };
        response.Locations.AddRange(tree.Locations.Select(l => new TbLocation
        {
            Id = S(l.Id), ParentId = l.ParentId is { } parent ? S(parent) : string.Empty, Name = l.Name,
            SortOrder = l.SortOrder, QrCode = l.QrCode, Depth = l.Depth, Removed = l.Removed
        }));
        response.ActionTypes.AddRange(tree.ActionTypes.Select(a => new TbActionType { Id = S(a.Id), Code = a.Code, Name = a.Name }));
        return response;
    }

    public static TbRecentRegistration MapRecent(RecentRegistration r)
    {
        var item = new TbRecentRegistration { Id = S(r.Id), EffectiveAt = Ts(r.EffectiveAt), Cancelled = r.Cancelled };
        item.Locations.AddRange(r.Locations.Select(l => new TbRegistrationLocation { LocationId = S(l.LocationId), Minor = l.Minor, Severe = l.Severe }));
        return item;
    }

    public static TbOutbreakSummary MapSummary(OutbreakSummary s) => new()
    {
        Id = S(s.Id), LocationId = S(s.LocationId), OpenedAt = Ts(s.OpenedAt), Assessed = s.Assessed,
        OpenActions = s.OpenActions, Closed = s.Closed,
        BittenPigs = s.BittenPigs, SeverePigs = s.SeverePigs
    };

    public static TbOutbreakDetail MapDetail(OutbreakDetail d)
    {
        var detail = new TbOutbreakDetail
        {
            Summary = MapSummary(d.Summary), RuleId = S(d.RuleId), RuleVersion = d.RuleVersion, HasAnswers = d.Answers is not null
        };
        detail.RegistrationIds.AddRange(d.RegistrationIds.Select(S));
        if (d.Answers is { } a)
        {
            detail.Answers = new TbAnswers
            {
                Water = a.Water, Feed = a.Feed, ActivityMaterial = a.ActivityMaterial, Climate = a.Climate, Health = a.Health, Management = a.Management
            };
        }

        detail.Actions.AddRange(d.Actions.Select(MapAction));
        return detail;
    }

    private static TbAction MapAction(OutbreakActionDetail a)
    {
        var action = new TbAction
        {
            Id = S(a.Id), Factor = Factor(a.Factor), Description = a.Description,
            ResponsibleSiteId = S(a.ResponsibleSiteId), FollowUpDate = Ts(a.FollowUpDate)
        };
        if (a.DoneAt is { } doneAt) action.DoneAt = Ts(doneAt);
        if (a.WithdrawnAt is { } withdrawnAt) action.WithdrawnAt = Ts(withdrawnAt);
        return action;
    }
}
