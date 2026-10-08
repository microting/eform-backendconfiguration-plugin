#nullable enable
using NUnit.Framework;
using BackendConfiguration.Pn.Infrastructure.Helpers;

namespace BackendConfiguration.Pn.Test;

/// <summary>
/// A worker's e-mail is also its login name. Invisible characters picked up by
/// copy-paste (left-to-right marks, zero-width spaces, byte-order marks) made
/// Identity refuse the address, so the worker never got a login.
/// </summary>
[TestFixture]
public class WorkerEmailSanitizerTests
{
    [TestCase("\u200Ejane.doe@example.com", "jane.doe@example.com", TestName = "Clean_LeftToRightMarkInFront_IsRemoved")]
    [TestCase("jane.doe@example.com\u200F", "jane.doe@example.com", TestName = "Clean_RightToLeftMarkAtEnd_IsRemoved")]
    [TestCase("jane.\u200Bdoe@exam\u200Dple.com", "jane.doe@example.com", TestName = "Clean_ZeroWidthCharactersInside_AreRemoved")]
    [TestCase("\uFEFFjane.doe@example.com", "jane.doe@example.com", TestName = "Clean_ByteOrderMark_IsRemoved")]
    [TestCase("jane.doe@example.com\u2060", "jane.doe@example.com", TestName = "Clean_WordJoiner_IsRemoved")]
    [TestCase("jane.doe@example.com\t\r\n", "jane.doe@example.com", TestName = "Clean_ControlCharacters_AreRemoved")]
    [TestCase("  jane.doe@example.com\u00A0", "jane.doe@example.com", TestName = "Clean_SurroundingSpacesAndNbsp_AreTrimmed")]
    [TestCase("\u00A0\u200E jane.doe@example.com", "jane.doe@example.com", TestName = "Clean_MixedLeadingJunk_IsRemoved")]
    public void Clean_RemovesInvisibleCharacters(string input, string expected)
    {
        Assert.That(WorkerEmailSanitizer.Clean(input), Is.EqualTo(expected));
    }

    [TestCase("jane.doe@example.com")]
    [TestCase("Jane.Doe+tag@Example.org")]
    [TestCase("søren.æbelå@firma.dk")]
    [TestCase("user_12_34@microting.invalid")]
    public void Clean_VisibleAddress_IsUnchanged(string input)
    {
        Assert.That(WorkerEmailSanitizer.Clean(input), Is.EqualTo(input));
    }

    [TestCase(null)]
    [TestCase("")]
    public void Clean_NullOrEmpty_IsReturnedAsIs(string? input)
    {
        Assert.That(WorkerEmailSanitizer.Clean(input), Is.EqualTo(input));
    }

    [Test]
    public void Clean_OnlyInvisibleCharacters_BecomesEmpty()
    {
        Assert.That(WorkerEmailSanitizer.Clean("\u200E\u200B "), Is.EqualTo(""));
    }
}
