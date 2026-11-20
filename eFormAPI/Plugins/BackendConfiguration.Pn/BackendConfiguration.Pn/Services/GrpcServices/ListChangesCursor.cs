#nullable enable
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using BackendConfiguration.Pn.Grpc.Events;
using BackendConfiguration.Pn.Grpc.Internal;
using Google.Protobuf;

namespace BackendConfiguration.Pn.Services.GrpcServices;

/// <summary>The full wire key of one occurrence (event id, plan day, compliance id).</summary>
internal readonly record struct OccurrenceKey(int EventId, string PlanDayKey, long ComplianceId);

/// <summary>
/// What the client was told about one occurrence: its content hash, the hash
/// of the parts that need no form read, and its version stamp.
/// </summary>
internal readonly record struct CursorEntry(ulong ContentHash, ulong RowHash, long? Version);

/// <summary>Decoded <c>ListChanges</c> cursor.</summary>
internal sealed record ListChangesCursorState(
    int SdkSiteId,
    int LanguageId,
    DateOnly From,
    DateOnly To,
    DateOnly IssuedDay,
    IReadOnlyList<int> PropertyIds,
    IReadOnlyDictionary<OccurrenceKey, CursorEntry> Entries);

/// <summary>
/// The opaque <c>ListChanges</c> cursor: a server-internal
/// <see cref="ListChangesCursorPayload"/> followed by an 8-byte SHA-256
/// prefix. The checksum only detects corruption; it is not a MAC, because
/// the server recomputes every reply from the caller's own authorisation.
/// </summary>
internal static class ListChangesCursor
{
    internal const uint FormatVersion = 3;

    /// <summary>Most occurrences one window may hold; beyond it the call is RESOURCE_EXHAUSTED.</summary>
    internal const int MaxEntries = 5000;

    /// <summary>Most properties a cursor may name.</summary>
    internal const int MaxProperties = 1000;

    /// <summary>Anything longer is not a cursor this server issued; checked before parsing.</summary>
    internal const int MaxEncodedBytes = 512 * 1024;

    /// <summary>Bytes of SHA-256 kept as the checksum trailer.</summary>
    internal const int ChecksumBytes = 8;

    /// <summary>Hex characters of an etag that make the 64-bit hash prefix.</summary>
    private const int EtagHashPrefixHexChars = 16;

    internal const string DateKeyFormat = "yyyy-MM-dd";

    /// <summary>
    /// Fingerprint of events.proto: a cursor from a server with another
    /// schema resets once (its content hashes are not comparable).
    /// </summary>
    /// <summary>Why a parsed payload cannot be used; empty when it can.</summary>
    private static string PayloadRejection(ListChangesCursorPayload payload)
    {
        if (payload.Format != FormatVersion)
        {
            return "format";
        }

        if (payload.Schema != SchemaFingerprint)
        {
            return "schema";
        }

        if (payload.SpanDays < 0)
        {
            return "window";
        }

        if (payload.PropertyIds.Count > MaxProperties)
        {
            return "properties";
        }

        return payload.Entries.Count > MaxEntries ? "entries" : string.Empty;
    }

    private static readonly uint SchemaFingerprint = BinaryPrimitives.ReadUInt32LittleEndian(
        SHA256.HashData(Event.Descriptor.File.SerializedData.ToByteArray()));

    /// <summary>First 64 bits of a hex etag (<see cref="EventsGrpcService.ComputeEtag"/>).</summary>
    internal static ulong HashFromEtag(string etag) =>
        ulong.Parse(etag.AsSpan(0, EtagHashPrefixHexChars), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    internal static byte[] Encode(ListChangesCursorState state)
    {
        if (state.Entries.Count > MaxEntries)
        {
            throw new InvalidOperationException(
                $"A ListChanges cursor holds at most {MaxEntries} occurrences; got {state.Entries.Count}.");
        }

        var payload = new ListChangesCursorPayload
        {
            Format = FormatVersion,
            Schema = SchemaFingerprint,
            SdkSiteId = state.SdkSiteId,
            LanguageId = state.LanguageId,
            FromDay = state.From.DayNumber,
            SpanDays = state.To.DayNumber - state.From.DayNumber,
            IssuedDay = state.IssuedDay.DayNumber
        };
        payload.PropertyIds.AddRange(state.PropertyIds.Distinct().Order());

        var previousEventId = 0;
        foreach (var (key, entry) in state.Entries
                     .OrderBy(e => e.Key.EventId)
                     .ThenBy(e => e.Key.PlanDayKey, StringComparer.Ordinal)
                     .ThenBy(e => e.Key.ComplianceId))
        {
            var encoded = new ListChangesCursorEntry
            {
                EventIdDelta = key.EventId - previousEventId,
                ComplianceId = key.ComplianceId,
                ContentHash = entry.ContentHash,
                RowHash = entry.RowHash
            };
            previousEventId = key.EventId;
            if (DateOnly.TryParseExact(key.PlanDayKey, DateKeyFormat, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var day))
            {
                encoded.DayOffset = day.DayNumber - state.From.DayNumber;
            }
            else
            {
                encoded.DayLiteral = key.PlanDayKey;
            }

            if (entry.Version is { } version)
            {
                encoded.Version = version;
            }

            payload.Entries.Add(encoded);
        }

        var bytes = payload.ToByteArray();
        var result = new byte[bytes.Length + ChecksumBytes];
        bytes.CopyTo(result, 0);
        SHA256.HashData(bytes).AsSpan(0, ChecksumBytes).CopyTo(result.AsSpan(bytes.Length));
        return result;
    }

    /// <summary>
    /// The cursor's state, or null with a short reason when it cannot be used
    /// (empty, too long, corrupt, another format or schema). Never throws.
    /// </summary>
    internal static ListChangesCursorState? TryDecode(ReadOnlySpan<byte> cursor, out string reason)
    {
        if (cursor.IsEmpty)
        {
            reason = "empty";
            return null;
        }

        if (cursor.Length > MaxEncodedBytes || cursor.Length <= ChecksumBytes)
        {
            reason = "length";
            return null;
        }

        var body = cursor[..^ChecksumBytes];
        if (!SHA256.HashData(body).AsSpan(0, ChecksumBytes).SequenceEqual(cursor[^ChecksumBytes..]))
        {
            reason = "checksum";
            return null;
        }

        try
        {
            var payload = ListChangesCursorPayload.Parser.ParseFrom(body.ToArray());
            reason = PayloadRejection(payload);
            if (reason.Length > 0)
            {
                return null;
            }

            var from = DateOnly.FromDayNumber(payload.FromDay);
            var entries = new Dictionary<OccurrenceKey, CursorEntry>(payload.Entries.Count);
            var eventId = 0;
            foreach (var e in payload.Entries)
            {
                eventId = checked(eventId + e.EventIdDelta);
                var day = e.HasDayLiteral
                    ? e.DayLiteral
                    : from.AddDays(e.DayOffset).ToString(DateKeyFormat, CultureInfo.InvariantCulture);
                entries[new OccurrenceKey(eventId, day, e.ComplianceId)] =
                    new CursorEntry(e.ContentHash, e.RowHash, e.HasVersion ? e.Version : null);
            }

            return new ListChangesCursorState(payload.SdkSiteId, payload.LanguageId, from,
                from.AddDays(payload.SpanDays), DateOnly.FromDayNumber(payload.IssuedDay),
                payload.PropertyIds.ToList(), entries);
        }
        catch (Exception ex) when (ex is InvalidProtocolBufferException or OverflowException
                                       or ArgumentOutOfRangeException)
        {
            reason = "corrupt";
            return null;
        }
    }
}
