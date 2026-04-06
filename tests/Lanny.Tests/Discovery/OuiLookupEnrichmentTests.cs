using Lanny.Discovery;

namespace Lanny.Tests.Discovery;

public class OuiLookupEnrichmentTests
{
    [Theory]
    [InlineData("10:06:1C:AA:BB:CC", "Espressif")]
    [InlineData("60:1A:C7:AA:BB:CC", "Nintendo")]
    public void Resolve_AdditionalKnownPrefixes_ReturnsVendor(string macAddress, string expectedVendor)
    {
        var vendor = OuiLookup.Resolve(macAddress);

        Assert.Equal(expectedVendor, vendor);
    }

    [Fact]
    public void Resolve_LocallyAdministeredMacWithoutKnownPrefix_ReturnsLocallyAdministeredLabel()
    {
        var vendor = OuiLookup.Resolve("AE:12:34:56:78:90");

        Assert.Equal("Locally Administered / Randomized", vendor);
    }
}
