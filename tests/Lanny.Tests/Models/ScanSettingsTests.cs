using Lanny.Models;

namespace Lanny.Tests.Models;

public class ScanSettingsTests
{
    [Fact]
    public void Constructor_DefaultOfflineDeviceRetentionHours_IsTwentyFourHours()
    {
        var settings = new ScanSettings();

        Assert.Equal(24, settings.OfflineDeviceRetentionHours);
    }
}
