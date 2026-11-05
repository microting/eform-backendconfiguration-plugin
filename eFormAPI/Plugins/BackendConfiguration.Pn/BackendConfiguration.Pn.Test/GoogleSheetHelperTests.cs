using NUnit.Framework;
using BackendConfiguration.Pn.Infrastructure.Helpers;

namespace BackendConfiguration.Pn.Test;

/// <summary>
/// A push to a configured sheet without a private key used to return without
/// a word, so a worker was saved and silently never got columns in the sheet.
/// </summary>
[TestFixture]
public class GoogleSheetHelperTests
{
    [Test]
    public void MissingCredentials_SheetConfiguredWithoutKey_NamesTheMissingKey()
    {
        Assert.That(GoogleSheetHelper.MissingCredentials("sheet-id", null), Does.Contain("PRIVATE_KEY_ID"));
        Assert.That(GoogleSheetHelper.MissingCredentials("sheet-id", ""), Does.Contain("PRIVATE_KEY_ID"));
    }

    [TestCase("sheet-id", "key-id")]
    [TestCase("", null)]
    [TestCase(null, "key-id")]
    public void MissingCredentials_KeyPresentOrNoSheet_IsNotAProblem(string googleSheetId, string privateKeyId)
    {
        Assert.That(GoogleSheetHelper.MissingCredentials(googleSheetId, privateKeyId), Is.Null);
    }
}
