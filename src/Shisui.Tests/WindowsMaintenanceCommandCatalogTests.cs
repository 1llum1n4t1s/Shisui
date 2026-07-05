using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shisui.Core.Services.Windows;

namespace Shisui.Tests;

[TestClass]
public class WindowsMaintenanceCommandCatalogTests
{
    [TestMethod]
    public void All_HasUniqueIds()
    {
        var ids = WindowsMaintenanceCommandCatalog.All.Select(c => c.Definition.Id).ToList();
        CollectionAssert.AllItemsAreUnique(ids);
    }

    [TestMethod]
    [DataRow("nbtstat-purge-reload", "nbtstat", "-R")]
    [DataRow("nbtstat-reregister", "nbtstat", "-RR")]
    [DataRow("ipconfig-registerdns", "ipconfig", "/registerdns")]
    [DataRow("ipconfig-flushdns", "ipconfig", "/flushdns")]
    [DataRow("ipconfig-release", "ipconfig", "/release")]
    [DataRow("ipconfig-release6", "ipconfig", "/release6")]
    [DataRow("ipconfig-renew", "ipconfig", "/renew")]
    [DataRow("ipconfig-renew6", "ipconfig", "/renew6")]
    [DataRow("netcfg-delete", "netcfg", "-d")]
    [DataRow("netsh-http-flush-logbuffer", "netsh", "http flush logbuffer")]
    [DataRow("netsh-http-delete-cache", "netsh", "http delete cache")]
    [DataRow("netsh-winhttp-reset-proxy", "netsh", "winhttp reset proxy")]
    [DataRow("netsh-winhttp-reset-autoproxy", "netsh", "winhttp reset autoproxy")]
    [DataRow("netsh-advfirewall-reset", "netsh", "advfirewall reset")]
    [DataRow("netsh-winsock-reset", "netsh", "winsock reset")]
    [DataRow("netsh-int-tcp-reset", "netsh", "int tcp reset")]
    [DataRow("netsh-int-tcp-set-global-default", "netsh", "int tcp set global default")]
    [DataRow("netsh-int-ip-reset", "netsh", "int ip reset")]
    [DataRow("route-clear", "route", "/f")]
    [DataRow("arp-clear", "arp", "-d *")]
    public void Find_ProducesExpectedFileNameAndArguments(string id, string expectedFileName, string expectedArguments)
    {
        var command = WindowsMaintenanceCommandCatalog.Find(id);

        Assert.IsNotNull(command, $"コマンド ID '{id}' がカタログに存在しません");
        Assert.AreEqual(expectedFileName, command.FileName);
        Assert.AreEqual(expectedArguments, command.Arguments);
    }

    [TestMethod]
    public void Find_UnknownId_ReturnsNull()
    {
        Assert.IsNull(WindowsMaintenanceCommandCatalog.Find("does-not-exist"));
    }

    [TestMethod]
    public void BatchableCategoryLabels_CoverReacquireProxyCache_ExcludeDangerAndComponent()
    {
        var labels = WindowsMaintenanceCommandCatalog.BatchableCategoryLabels;

        // カテゴリ名の定数は private なので、実データ (各コマンドの Category) 経由で照合する。
        string CategoryOf(string id) => WindowsMaintenanceCommandCatalog.Find(id)!.Definition.Category;

        // 一括実行の意味があるカテゴリは対象に含まれる。
        Assert.IsTrue(labels.ContainsKey(CategoryOf("ipconfig-renew")), "IP 再取得は一括対象であるべき");
        Assert.IsTrue(labels.ContainsKey(CategoryOf("netsh-winhttp-reset-proxy")), "プロキシリセットは一括対象であるべき");
        Assert.IsTrue(labels.ContainsKey(CategoryOf("ipconfig-flushdns")), "キャッシュ・登録は一括対象であるべき");

        // 危険なスタックリセット・単一の最終手段は一括対象にしない。
        Assert.IsFalse(labels.ContainsKey(CategoryOf("netsh-winsock-reset")), "スタックリセット (危険) は一括対象にしない");
        Assert.IsFalse(labels.ContainsKey(CategoryOf("netcfg-delete")), "コンポーネント再検出は一括対象にしない");

        Assert.AreEqual(3, labels.Count, "一括対象カテゴリは 3 つ (キャッシュ / IP再取得 / プロキシ)");
        foreach (var label in labels.Values)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(label), "ボタンラベルは非空であるべき");
        }
    }

    [TestMethod]
    public void DestructiveCommands_AreFlaggedCorrectly()
    {
        string[] destructiveIds =
        [
            "ipconfig-release", "ipconfig-release6",
            "netsh-advfirewall-reset", "netsh-winsock-reset", "netsh-int-tcp-reset",
            "netsh-int-tcp-set-global-default", "netsh-int-ip-reset", "route-clear", "netcfg-delete",
        ];

        foreach (var id in destructiveIds)
        {
            var command = WindowsMaintenanceCommandCatalog.Find(id);
            Assert.IsNotNull(command);
            Assert.IsTrue(command.Definition.IsDestructive, $"{id} は破壊的コマンドとしてマークされているべきです");
        }
    }
}
