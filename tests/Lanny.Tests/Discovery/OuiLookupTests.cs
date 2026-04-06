using Lanny.Discovery;

namespace Lanny.Tests.Discovery;

public class OuiLookupTests
{
    [Theory]
    [InlineData("70:85:C2:00:11:22", "Apple")]
    [InlineData("70-85-C2-00-11-22", "Apple")]
    [InlineData("00:15:5D:AA:BB:CC", "Microsoft (Hyper-V)")]
    public void Resolve_KnownPrefix_ReturnsVendor(string mac, string expectedVendor)
    {
        var vendor = OuiLookup.Resolve(mac);

        Assert.Equal(expectedVendor, vendor);
    }

    [Fact]
    public void Resolve_UnknownPrefix_ReturnsNull()
    {
        var vendor = OuiLookup.Resolve("DC:AD:BE:EF:00:01");

        Assert.Null(vendor);
    }

    [Fact]
    public void Resolve_InvalidMac_ReturnsNull()
    {
        var vendor = OuiLookup.Resolve(" ");

        Assert.Null(vendor);
    }
}