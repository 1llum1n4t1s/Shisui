using System.Net.NetworkInformation;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shisui.Core.Services.Windows;

namespace Shisui.Tests;

[TestClass]
public class WindowsNetworkAdapterFilterTests
{
    // 実機 (Windows 10.0.26200、2026-07-03) の NetworkInterface.GetAllNetworkInterfaces() 出力から採取。
    // (Description, Type, Status) の組。ncpa.cpl に見える本物は先頭 4 件のみで、残りは全部ノイズ。
    private static readonly (string Desc, NetworkInterfaceType Type, OperationalStatus Status)[] RealMachineSample =
    [
        // ── ncpa.cpl に表示される本物のアダプタ (これらだけ残ってほしい) ──
        ("Realtek Gaming 2.5GbE Family Controller", NetworkInterfaceType.Ethernet, OperationalStatus.Down),
        ("Realtek Gaming 2.5GbE Family Controller #2", NetworkInterfaceType.Ethernet, OperationalStatus.Down),
        ("RZ616 Wi-Fi 6E 160MHz", NetworkInterfaceType.Wireless80211, OperationalStatus.Up),
        ("Bluetooth Device (Personal Area Network)", NetworkInterfaceType.Ethernet, OperationalStatus.Down),

        // ── Wi-Fi Direct 仮想アダプタ (除外したい) ──
        ("Microsoft Wi-Fi Direct Virtual Adapter", NetworkInterfaceType.Wireless80211, OperationalStatus.Down),
        ("Microsoft Wi-Fi Direct Virtual Adapter #2", NetworkInterfaceType.Wireless80211, OperationalStatus.Down),

        // ── Loopback / Tunnel (除外したい) ──
        ("Software Loopback Interface 1", NetworkInterfaceType.Loopback, OperationalStatus.Up),
        ("Microsoft Teredo Tunneling Adapter", NetworkInterfaceType.Tunnel, OperationalStatus.Up),
        ("Microsoft IP-HTTPS Platform Adapter", NetworkInterfaceType.Tunnel, OperationalStatus.NotPresent),
        ("Microsoft 6to4 Adapter", NetworkInterfaceType.Tunnel, OperationalStatus.NotPresent),

        // ── NotPresent な疑似デバイス (除外したい) ──
        ("Microsoft Kernel Debug Network Adapter", NetworkInterfaceType.Ethernet, OperationalStatus.NotPresent),

        // ── NDIS フィルタ / バインディングの子インターフェイス (除外したい) ──
        ("Realtek Gaming 2.5GbE Family Controller #2-WFP Native MAC Layer LightWeight Filter-0000", NetworkInterfaceType.Ethernet, OperationalStatus.Down),
        ("Realtek Gaming 2.5GbE Family Controller #2-QoS Packet Scheduler-0000", NetworkInterfaceType.Ethernet, OperationalStatus.Down),
        ("Realtek Gaming 2.5GbE Family Controller #2-WFP 802.3 MAC Layer LightWeight Filter-0000", NetworkInterfaceType.Ethernet, OperationalStatus.Down),
        ("Realtek Gaming 2.5GbE Family Controller-WFP Native MAC Layer LightWeight Filter-0000", NetworkInterfaceType.Ethernet, OperationalStatus.Down),
        ("Realtek Gaming 2.5GbE Family Controller-QoS Packet Scheduler-0000", NetworkInterfaceType.Ethernet, OperationalStatus.Down),
        ("Realtek Gaming 2.5GbE Family Controller-WFP 802.3 MAC Layer LightWeight Filter-0000", NetworkInterfaceType.Ethernet, OperationalStatus.Down),
        ("RZ616 Wi-Fi 6E 160MHz-WFP Native MAC Layer LightWeight Filter-0000", NetworkInterfaceType.Wireless80211, OperationalStatus.Up),
        ("RZ616 Wi-Fi 6E 160MHz-Virtual WiFi Filter Driver-0000", NetworkInterfaceType.Wireless80211, OperationalStatus.Up),
        ("RZ616 Wi-Fi 6E 160MHz-Native WiFi Filter Driver-0000", NetworkInterfaceType.Wireless80211, OperationalStatus.Up),
        ("RZ616 Wi-Fi 6E 160MHz-QoS Packet Scheduler-0000", NetworkInterfaceType.Wireless80211, OperationalStatus.Up),
        ("RZ616 Wi-Fi 6E 160MHz-WFP 802.3 MAC Layer LightWeight Filter-0000", NetworkInterfaceType.Wireless80211, OperationalStatus.Up),
        ("Microsoft Wi-Fi Direct Virtual Adapter-QoS Packet Scheduler-0000", NetworkInterfaceType.Wireless80211, OperationalStatus.Down),
    ];

    private static IReadOnlyList<string> AllDescriptions => RealMachineSample.Select(x => x.Desc).ToList();

    [TestMethod]
    public void RealMachineSample_KeepsExactlyTheFourNcpaAdapters()
    {
        var kept = RealMachineSample
            .Where(x => WindowsNetworkAdapterFilter.IsUserConfigurable(x.Desc, x.Type, x.Status, AllDescriptions))
            .Select(x => x.Desc)
            .ToList();

        CollectionAssert.AreEquivalent(new[]
        {
            "Realtek Gaming 2.5GbE Family Controller",
            "Realtek Gaming 2.5GbE Family Controller #2",
            "RZ616 Wi-Fi 6E 160MHz",
            "Bluetooth Device (Personal Area Network)",
        }, kept);
    }

    [TestMethod]
    public void FilterBindingChild_DetectsLightWeightFilterChild()
    {
        Assert.IsTrue(WindowsNetworkAdapterFilter.IsFilterBindingChild(
            "RZ616 Wi-Fi 6E 160MHz-QoS Packet Scheduler-0000", AllDescriptions));
    }

    [TestMethod]
    public void FilterBindingChild_RealAdapterIsNotAChild()
    {
        // "...Controller" は "...Controller #2" の接頭辞だが、区切りが "-" ではなく空白なので子と誤判定しない。
        Assert.IsFalse(WindowsNetworkAdapterFilter.IsFilterBindingChild(
            "Realtek Gaming 2.5GbE Family Controller #2", AllDescriptions));
        Assert.IsFalse(WindowsNetworkAdapterFilter.IsFilterBindingChild(
            "Realtek Gaming 2.5GbE Family Controller", AllDescriptions));
    }

    [TestMethod]
    public void VpnTapAdapter_IsKept()
    {
        // OpenVPN 等の TAP 仮想アダプタは本物の設定対象として残す (ncpa.cpl にも表示される)。
        string[] all = ["TAP-Windows Adapter V9"];
        Assert.IsTrue(WindowsNetworkAdapterFilter.IsUserConfigurable(
            "TAP-Windows Adapter V9", NetworkInterfaceType.Ethernet, OperationalStatus.Down, all));
    }

    [TestMethod]
    public void HyperVVirtualAdapter_IsKept()
    {
        string[] all = ["Hyper-V Virtual Ethernet Adapter"];
        Assert.IsTrue(WindowsNetworkAdapterFilter.IsUserConfigurable(
            "Hyper-V Virtual Ethernet Adapter", NetworkInterfaceType.Ethernet, OperationalStatus.Up, all));
    }

    [TestMethod]
    [DataRow(NetworkInterfaceType.Loopback)]
    [DataRow(NetworkInterfaceType.Tunnel)]
    public void NonEthernetOrWireless_IsExcluded(NetworkInterfaceType type)
    {
        string[] all = ["whatever"];
        Assert.IsFalse(WindowsNetworkAdapterFilter.IsUserConfigurable("whatever", type, OperationalStatus.Up, all));
    }

    [TestMethod]
    public void NotPresentDevice_IsExcluded()
    {
        string[] all = ["Microsoft Kernel Debug Network Adapter"];
        Assert.IsFalse(WindowsNetworkAdapterFilter.IsUserConfigurable(
            "Microsoft Kernel Debug Network Adapter", NetworkInterfaceType.Ethernet, OperationalStatus.NotPresent, all));
    }
}
