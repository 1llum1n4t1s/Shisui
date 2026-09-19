using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shisui.Core.Models;
using Shisui.Core.Serialization;

namespace Shisui.Tests;

[TestClass]
public sealed class GamingNetworkProfileSerializationTests
{
    [TestMethod]
    public void AppSettings_RoundTrip_PreservesGamingNetworkJournal()
    {
        var settings = new AppSettings
        {
            GamingNetworkProfileSnapshots =
            [
                new GamingNetworkProfileSnapshot
                {
                    AdapterGuid = Guid.Parse("fb283a95-51d8-466b-b72f-c8d27361ca9b"),
                    AdapterDescription = "Intel Ethernet Controller",
                    Properties =
                    [
                        new GamingNetworkPropertySnapshot
                        {
                            RegistryKeyword = "*InterruptModeration",
                            OriginalValue = "1",
                        },
                    ],
                },
            ],
        };

        var json = JsonSerializer.Serialize(settings, ShisuiJsonContext.Default.AppSettings);
        var restored = JsonSerializer.Deserialize(json, ShisuiJsonContext.Default.AppSettings);

        Assert.IsNotNull(restored);
        Assert.HasCount(1, restored.GamingNetworkProfileSnapshots);
        var snapshot = restored.GamingNetworkProfileSnapshots[0];
        Assert.AreEqual(settings.GamingNetworkProfileSnapshots[0].AdapterGuid, snapshot.AdapterGuid);
        Assert.AreEqual("Intel Ethernet Controller", snapshot.AdapterDescription);
        Assert.HasCount(1, snapshot.Properties);
        Assert.AreEqual("1", snapshot.Properties[0].OriginalValue);
    }

    [TestMethod]
    public void AppSettings_RoundTrip_PreservesMediaTekWifiPropertyKeyword()
    {
        var settings = new AppSettings
        {
            GamingNetworkProfileSnapshots =
            [
                new GamingNetworkProfileSnapshot
                {
                    AdapterGuid = Guid.Parse("16f7d589-fc75-41af-97ef-90dc504b6179"),
                    AdapterDescription = "RZ616 Wi-Fi 6E 160MHz",
                    Properties =
                    [
                        new GamingNetworkPropertySnapshot
                        {
                            RegistryKeyword = "LowPowerEnable",
                            OriginalValue = "1",
                        },
                    ],
                },
            ],
        };

        var json = JsonSerializer.Serialize(settings, ShisuiJsonContext.Default.AppSettings);
        var restored = JsonSerializer.Deserialize(json, ShisuiJsonContext.Default.AppSettings);

        Assert.IsNotNull(restored);
        Assert.AreEqual("LowPowerEnable", restored.GamingNetworkProfileSnapshots.Single().Properties.Single().RegistryKeyword);
    }
}
