#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using BackendConfiguration.Pn.Grpc.Internal;
using BackendConfiguration.Pn.Services.GrpcServices;
using Google.Protobuf;
using NUnit.Framework;

namespace BackendConfiguration.Pn.Test.GrpcServices;

/// <summary>
/// The opaque ListChanges cursor (contract_version 2): round trip, size, and
/// that every damaged or foreign cursor decodes to "unusable" instead of
/// throwing or yielding different state.
/// </summary>
[TestFixture]
public class ListChangesCursorTests
{
    private static readonly DateOnly From = new(2026, 11, 20);

    /// <summary>Realistic ids: ARP ids in the thousands, compliance ids in the hundreds of thousands, 2026 versions.</summary>
    private static ListChangesCursorState State(int entries, int siteId = 42, int span = 2)
    {
        var map = new Dictionary<OccurrenceKey, CursorEntry>();
        var random = new Random(7);
        for (var i = 0; i < entries; i++)
        {
            var key = new OccurrenceKey(
                3000 + i / (span + 1),
                From.AddDays(i % (span + 1)).ToString("yyyy-MM-dd"),
                250_000 + i);
            map[key] = new CursorEntry((ulong)random.NextInt64(), (ulong)random.NextInt64(),
                i % 7 == 0 ? null : 1_790_000_000_000_000L + random.NextInt64(1_000_000_000_000L));
        }

        return new ListChangesCursorState(siteId, 3, From, From.AddDays(span), [17, 3, 250], map);
    }

    private static ListChangesCursorState? Decode(byte[] bytes) =>
        ListChangesCursor.TryDecode(bytes, out _);

    /// <summary>Re-seals a payload with a valid checksum, as a forger could.</summary>
    private static byte[] Seal(ListChangesCursorPayload payload)
    {
        var body = payload.ToByteArray();
        return body.Concat(SHA256.HashData(body).Take(ListChangesCursor.ChecksumBytes)).ToArray();
    }

    private static ListChangesCursorPayload PayloadOf(byte[] cursor) =>
        ListChangesCursorPayload.Parser.ParseFrom(cursor[..^ListChangesCursor.ChecksumBytes]);

    [Test]
    public void RoundTrip_KeepsEverything()
    {
        var state = State(50);
        var entries = new Dictionary<OccurrenceKey, CursorEntry>(state.Entries)
        {
            [new OccurrenceKey(1, "not-a-date", 0)] = new(5UL, 6UL, null)
        };
        var withLiteral = state with { Entries = entries };

        var decoded = Decode(ListChangesCursor.Encode(withLiteral));

        Assert.That(decoded, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(decoded!.SdkSiteId, Is.EqualTo(42));
            Assert.That(decoded.LanguageId, Is.EqualTo(3));
            Assert.That(decoded.From, Is.EqualTo(From));
            Assert.That(decoded.To, Is.EqualTo(From.AddDays(2)));
            Assert.That(decoded.PropertyIds, Is.EqualTo(new[] { 3, 17, 250 }));
            Assert.That(decoded.Entries, Is.EquivalentTo(withLiteral.Entries));
        });
    }

    [Test]
    public void Encode_IsDeterministic_RegardlessOfInsertionOrder()
    {
        var state = State(30);
        var reversed = state with
        {
            Entries = state.Entries.Reverse().ToDictionary(kv => kv.Key, kv => kv.Value),
            PropertyIds = state.PropertyIds.Reverse().ToList()
        };

        Assert.That(ListChangesCursor.Encode(reversed), Is.EqualTo(ListChangesCursor.Encode(state)));
    }

    [Test]
    public void Size_PerOccurrence_AndAtTheCap()
    {
        var empty = ListChangesCursor.Encode(State(0)).Length;
        var hundred = ListChangesCursor.Encode(State(100)).Length;
        var full = ListChangesCursor.Encode(State(ListChangesCursor.MaxEntries, span: 37)).Length;
        var perEntry = (hundred - empty) / 100.0;
        TestContext.Out.WriteLine(
            $"cursor bytes: empty={empty}, 100 entries={hundred} ({perEntry:F1} B/entry), {ListChangesCursor.MaxEntries} entries={full}");

        Assert.Multiple(() =>
        {
            Assert.That(perEntry, Is.LessThanOrEqualTo(45));
            Assert.That(full, Is.LessThan(ListChangesCursor.MaxEncodedBytes));
            Assert.That(Decode(ListChangesCursor.Encode(State(ListChangesCursor.MaxEntries, span: 37))), Is.Not.Null);
        });
    }

    [Test]
    public void Encode_MoreThanTheCap_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            ListChangesCursor.Encode(State(ListChangesCursor.MaxEntries + 1, span: 37)));
    }

    [Test]
    public void Empty_IsUnusable()
    {
        Assert.That(ListChangesCursor.TryDecode(ReadOnlySpan<byte>.Empty, out var reason), Is.Null);
        Assert.That(reason, Is.EqualTo("empty"));
    }

    [Test]
    public void Garbage_IsUnusable()
    {
        var random = new Random(1);
        for (var length = 1; length < 300; length += 7)
        {
            var bytes = new byte[length];
            random.NextBytes(bytes);
            Assert.That(Decode(bytes), Is.Null, $"length {length}");
        }
    }

    [Test]
    public void SealedGarbage_IsUnusable_NotAnException()
    {
        var random = new Random(2);
        for (var length = 1; length < 200; length += 3)
        {
            var body = new byte[length];
            random.NextBytes(body);
            var sealedBytes = body.Concat(SHA256.HashData(body).Take(ListChangesCursor.ChecksumBytes)).ToArray();
            ListChangesCursorState? decoded = null;
            Assert.DoesNotThrow(() => decoded = Decode(sealedBytes), $"length {length}");
            Assert.That(decoded, Is.Null, $"length {length}");
        }
    }

    [Test]
    public void EveryTruncation_IsUnusable()
    {
        var bytes = ListChangesCursor.Encode(State(20));
        for (var length = 0; length < bytes.Length; length++)
        {
            Assert.That(Decode(bytes[..length]), Is.Null, $"truncated to {length}");
        }
    }

    [Test]
    public void EverySingleBitFlip_IsUnusable()
    {
        var bytes = ListChangesCursor.Encode(State(5));
        for (var i = 0; i < bytes.Length; i++)
        {
            for (var bit = 0; bit < 8; bit++)
            {
                var copy = (byte[])bytes.Clone();
                copy[i] ^= (byte)(1 << bit);
                Assert.That(Decode(copy), Is.Null, $"byte {i} bit {bit}");
            }
        }
    }

    [Test]
    public void TrailingBytes_AreUnusable()
    {
        var bytes = ListChangesCursor.Encode(State(3));
        Assert.That(Decode([.. bytes, 0]), Is.Null);
    }

    [Test]
    public void AnotherFormatVersion_IsUnusable_EvenWithAValidChecksum()
    {
        var payload = PayloadOf(ListChangesCursor.Encode(State(3)));
        payload.Format = ListChangesCursor.FormatVersion + 1;

        Assert.That(ListChangesCursor.TryDecode(Seal(payload), out var reason), Is.Null);
        Assert.That(reason, Is.EqualTo("format"));
    }

    [Test]
    public void AnotherSchemaFingerprint_IsUnusable_EvenWithAValidChecksum()
    {
        var payload = PayloadOf(ListChangesCursor.Encode(State(3)));
        payload.Schema ^= 0xFFFFFFFF;

        Assert.That(ListChangesCursor.TryDecode(Seal(payload), out var reason), Is.Null);
        Assert.That(reason, Is.EqualTo("schema"));
    }

    [Test]
    public void TooManyEntries_IsUnusable_EvenWithAValidChecksum()
    {
        var payload = PayloadOf(ListChangesCursor.Encode(State(1)));
        payload.Entries.AddRange(Enumerable.Range(0, ListChangesCursor.MaxEntries)
            .Select(_ => new ListChangesCursorEntry { EventIdDelta = 1 }));

        Assert.That(ListChangesCursor.TryDecode(Seal(payload), out var reason), Is.Null);
        Assert.That(reason, Is.EqualTo("entries"));
    }

    [Test]
    public void OutOfRangeDays_AreUnusable_EvenWithAValidChecksum()
    {
        var payload = PayloadOf(ListChangesCursor.Encode(State(2)));
        payload.FromDay = int.MaxValue;

        Assert.That(ListChangesCursor.TryDecode(Seal(payload), out var reason), Is.Null);
        Assert.That(reason, Is.EqualTo("corrupt"));
    }

    [Test]
    public void Oversized_IsUnusable_WithoutParsing()
    {
        Assert.That(ListChangesCursor.TryDecode(new byte[ListChangesCursor.MaxEncodedBytes + 1], out var reason),
            Is.Null);
        Assert.That(reason, Is.EqualTo("length"));
    }

    [Test]
    public void HashFromEtag_IsTheFirst64BitsOfTheEtag()
    {
        Assert.That(ListChangesCursor.HashFromEtag("0123456789abcdef" + new string('f', 48)),
            Is.EqualTo(0x0123456789abcdefUL));
    }
}
