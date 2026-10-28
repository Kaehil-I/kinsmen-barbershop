using Kinsmen.Web.Helpers;

namespace Kinsmen.Web.Tests;

public sealed class PhoneFormatterTests
{
    [Theory]
    [InlineData("+27821234567", "082 123 4567")]
    [InlineData("+27315550101", "031 555 0101")]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("+4420794609", "+4420794609")]
    public void StoredNumbersAreShownTheLocalWay(string? stored, string expected)
        => Assert.Equal(expected, PhoneFormatter.Local(stored));
}
