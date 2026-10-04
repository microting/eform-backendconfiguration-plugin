#nullable enable
using System;
using System.Security.Cryptography;
using System.Text;

namespace BackendConfiguration.Pn.Services.InboundMail;

/// <summary>Inbound mail addresses: <c>{customerNo}-{token}@{mailDomain}</c>, token = 10 chars of [a-z2-7].</summary>
public static class InboxAddressGenerator
{
    private const string Alphabet = "abcdefghijklmnopqrstuvwxyz234567";
    private const int TokenLength = 10;

    public static (string Address, string TokenHash) New(int customerNo, string mailDomain)
    {
        var localPart = string.Create(TokenLength, 0, (span, _) =>
        {
            for (var i = 0; i < span.Length; i++) span[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        });
        return ($"{customerNo}-{localPart}@{mailDomain}", HashToken(localPart));
    }

    /// <summary>Lower-case hex SHA-256 of the lower-cased token; the only form the hub ever sees.</summary>
    public static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token.ToLowerInvariant()))).ToLowerInvariant();
}
