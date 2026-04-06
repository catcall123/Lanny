using Lanny.Discovery;

namespace Lanny.Tests.Discovery;

public class OuiVendorDatasetParserTests
{
    [Fact]
    public void ParseLines_WhenPrefixAppearsMultipleTimes_LastEntryWinsDeterministically()
    {
        var dataset = OuiVendorDatasetParser.ParseLines(
        [
            "AA:BB:CC,Vendor A",
            "aa-bb-cc,Vendor B",
        ]);

        Assert.Equal("Vendor B", dataset.GetValueOrDefault("AA:BB:CC"));
    }

    [Fact]
    public void ParseLines_IgnoresBlankLinesAndComments()
    {
        var dataset = OuiVendorDatasetParser.ParseLines(
        [
            "# Comment",
            "",
            "70:85:C2,Apple",
        ]);

        Assert.Single(dataset);
        Assert.Equal("Apple", dataset["70:85:C2"]);
    }

    [Fact]
    public void ParseIeeeCsvLines_ExtractsAssignmentAndVendorFromOfficialFormat()
    {
        var dataset = OuiVendorDatasetParser.ParseIeeeCsvLines(
        [
            "Registry,Assignment,Organization Name,Organization Address",
            "MA-L,7085C2,Apple Inc.,One Apple Park Way Cupertino CA US 95014",
            "CID,10D31A,Contoso Labs,1 Example Way Redmond WA US 98052",
        ]);

        Assert.Equal("Apple Inc.", dataset["70:85:C2"]);
        Assert.Equal("Contoso Labs", dataset["10:D3:1A"]);
    }
}