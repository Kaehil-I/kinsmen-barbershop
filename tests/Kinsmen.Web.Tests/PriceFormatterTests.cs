using System.Globalization;
using Kinsmen.Web.Helpers;

namespace Kinsmen.Web.Tests;

public sealed class PriceFormatterTests
{
    [Theory]
    [InlineData(0, "R0")]
    [InlineData(100, "R1")]
    [InlineData(10000, "R100")]
    [InlineData(20000, "R200")]
    public void WholeRandAmountsDropTheCents(int cents, string expected)
        => Assert.Equal(expected, PriceFormatter.FormatZarCents(cents));

    [Theory]
    [InlineData(1, "R0.01")]
    [InlineData(50, "R0.50")]
    [InlineData(5050, "R50.50")]
    [InlineData(19999, "R199.99")]
    public void FractionalAmountsShowTwoDecimalPlaces(int cents, string expected)
        => Assert.Equal(expected, PriceFormatter.FormatZarCents(cents));

    // The booking page's JS always prints a dot. On a South African machine the current
    // culture's decimal separator is a comma, so without an explicit invariant culture
    // the server-rendered price would read "R199,99" - this pins that down. The culture
    // is built from a clone of the invariant one (rather than new CultureInfo("de-DE"))
    // so the test doesn't depend on ICU/culture data being installed on the machine.
    [Fact]
    public void DecimalSeparatorIsADotEvenWhenTheMachineCultureUsesAComma()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            var commaCulture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
            commaCulture.NumberFormat.NumberDecimalSeparator = ",";
            CultureInfo.CurrentCulture = commaCulture;

            Assert.Equal("R199.99", PriceFormatter.FormatZarCents(19999));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
