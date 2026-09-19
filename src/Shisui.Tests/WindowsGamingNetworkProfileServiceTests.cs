using System.Runtime.Versioning;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shisui.Core.Interfaces;
using Shisui.Core.Models;
using Shisui.Core.Services.Windows;

namespace Shisui.Tests;

[TestClass]
[SupportedOSPlatform("windows")]
public sealed class WindowsGamingNetworkProfileServiceTests
{
    private static readonly Guid AdapterGuid = Guid.Parse("fb283a95-51d8-466b-b72f-c8d27361ca9b");

    [TestMethod]
    public async Task ApplyAsync_SuccessPersistsOriginalsBeforeWritesAndIsIdempotent()
    {
        var executor = new FakeExecutor();
        executor.Success(State(("*InterruptModeration", "1"), ("*EEE", "1")));
        executor.Success("UPDATED=*InterruptModeration;VALUE=0");
        executor.Success("UPDATED=*EEE;VALUE=0");
        executor.Success(State(("*InterruptModeration", "0"), ("*EEE", "0")));
        var settings = new FakeSettingsService();
        var gate = new FakeGate();
        var service = new WindowsGamingNetworkProfileService(executor, settings, gate);

        var first = await service.ApplyAsync("Ethernet");

        Assert.AreEqual(4, executor.Calls.Count);
        Assert.AreEqual(1, settings.SaveCount);
        var journal = settings.Current.GamingNetworkProfileSnapshots.Single();
        Assert.AreEqual(AdapterGuid, journal.AdapterGuid);
        CollectionAssert.AreEquivalent(
            new[] { "*InterruptModeration=1", "*EEE=1" },
            journal.Properties.Select(property => $"{property.RegistryKeyword}={property.OriginalValue}").ToArray());
        StringAssert.Contains(first[^1].StandardOutput, "読み戻し確認済み");
        StringAssert.Contains(first[^1].StandardOutput, "PC の再起動が必要");
        StringAssert.Contains(first[^1].StandardOutput, "実際のゲーム性能・遅延は未検証");

        executor.Success(State(("*InterruptModeration", "0"), ("*EEE", "0")));
        var second = await service.ApplyAsync("Ethernet");

        Assert.AreEqual(5, executor.Calls.Count);
        Assert.AreEqual(1, settings.SaveCount);
        Assert.AreEqual("1", journal.Properties.Single(property => property.RegistryKeyword == "*EEE").OriginalValue);
        StringAssert.Contains(second[^1].StandardOutput, "既に 0");
        Assert.AreEqual(2, gate.EnterCount);
    }

    [TestMethod]
    public async Task ApplyAsync_AlreadyTargetWithoutJournal_DoesNotCreateSnapshotOrSave()
    {
        var executor = new FakeExecutor();
        executor.Success(State(("*InterruptModeration", "0"), ("*EEE", "0")));
        var settings = new FakeSettingsService();
        var service = Create(executor, settings);

        var results = await service.ApplyAsync("Ethernet");

        Assert.HasCount(1, executor.Calls);
        Assert.AreEqual(0, settings.SaveCount);
        Assert.IsEmpty(settings.Current.GamingNetworkProfileSnapshots);
        Assert.IsTrue(results[^1].Success);
    }

    [TestMethod]
    public async Task ApplyAsync_SaveFailureDoesNotWriteAndRollsBackMemory()
    {
        var executor = new FakeExecutor();
        executor.Success(State(("*EEE", "1")));
        var settings = new FakeSettingsService { SaveException = new IOException("disk full") };
        var service = Create(executor, settings);

        var results = await service.ApplyAsync("Ethernet");

        Assert.HasCount(1, executor.Calls);
        Assert.AreEqual(1, settings.SaveCount);
        Assert.IsEmpty(settings.Current.GamingNetworkProfileSnapshots);
        Assert.IsFalse(results[^1].Success);
        StringAssert.Contains(results[^1].StandardError, "保存できない");
    }

    [TestMethod]
    public async Task ApplyAsync_CancellationDuringJournalSaveRollsBackMemoryAndDoesNotWrite()
    {
        var executor = new FakeExecutor();
        executor.Success(State(("*EEE", "1")));
        var settings = new FakeSettingsService { CancelOnSave = true };
        var service = Create(executor, settings);

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => service.ApplyAsync("Ethernet"));

        Assert.HasCount(1, executor.Calls);
        Assert.AreEqual(1, settings.SaveCount);
        Assert.IsEmpty(settings.Current.GamingNetworkProfileSnapshots);
    }

    [TestMethod]
    public async Task ApplyAsync_PartialWriteFailureRetainsRestorationJournal()
    {
        var executor = new FakeExecutor();
        executor.Success(State(("*InterruptModeration", "1"), ("*EEE", "1")));
        executor.Success("UPDATED=*InterruptModeration;VALUE=0");
        executor.Failure("driver rejected EEE");
        var settings = new FakeSettingsService();
        var service = Create(executor, settings);

        var results = await service.ApplyAsync("Ethernet");

        Assert.AreEqual(3, executor.Calls.Count);
        Assert.HasCount(1, settings.Current.GamingNetworkProfileSnapshots);
        Assert.HasCount(2, settings.Current.GamingNetworkProfileSnapshots[0].Properties);
        Assert.IsFalse(results[^1].Success);
        StringAssert.Contains(results[^1].StandardError, "復元情報は保持");
    }

    [TestMethod]
    public async Task ApplyAsync_CancellationDuringSecondWriteRetainsPersistedJournal()
    {
        var executor = new FakeExecutor { CancelOnCall = 3 };
        executor.Success(State(("*InterruptModeration", "1"), ("*EEE", "1")));
        executor.Success("UPDATED=*InterruptModeration;VALUE=0");
        var settings = new FakeSettingsService();
        var service = Create(executor, settings);

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => service.ApplyAsync("Ethernet"));

        Assert.HasCount(3, executor.Calls);
        Assert.AreEqual(1, settings.SaveCount);
        Assert.HasCount(1, settings.Current.GamingNetworkProfileSnapshots);
        Assert.HasCount(2, settings.Current.GamingNetworkProfileSnapshots[0].Properties);
    }

    [TestMethod]
    public async Task ApplyAsync_CancellationDuringReadbackRetainsPersistedJournal()
    {
        var executor = new FakeExecutor { CancelOnCall = 3 };
        executor.Success(State(("*EEE", "1")));
        executor.Success("UPDATED=*EEE;VALUE=0");
        var settings = new FakeSettingsService();
        var service = Create(executor, settings);

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => service.ApplyAsync("Ethernet"));

        Assert.HasCount(3, executor.Calls);
        Assert.AreEqual(1, settings.SaveCount);
        Assert.HasCount(1, settings.Current.GamingNetworkProfileSnapshots);
    }

    [TestMethod]
    public async Task ApplyAsync_VerificationMismatchRetainsJournal()
    {
        var executor = new FakeExecutor();
        executor.Success(State(("*EEE", "1")));
        executor.Success("UPDATED=*EEE;VALUE=0");
        executor.Success(State(("*EEE", "1")));
        var settings = new FakeSettingsService();
        var service = Create(executor, settings);

        var results = await service.ApplyAsync("Ethernet");

        Assert.HasCount(1, settings.Current.GamingNetworkProfileSnapshots);
        Assert.IsFalse(results[^1].Success);
        StringAssert.Contains(results[^1].StandardError, "読み戻し確認できません");
    }

    [TestMethod]
    public async Task ApplyAsync_VerificationReadFailureDoesNotClaimNothingWasChanged()
    {
        var executor = new FakeExecutor();
        executor.Success(State(("*EEE", "1")));
        executor.Success("UPDATED=*EEE;VALUE=0");
        executor.Failure("readback failed");
        var settings = new FakeSettingsService();
        var service = Create(executor, settings);

        var results = await service.ApplyAsync("Ethernet");

        Assert.HasCount(1, settings.Current.GamingNetworkProfileSnapshots);
        var messages = string.Join('\n', results.Select(result => result.StandardError));
        StringAssert.Contains(messages, "状態の読み取りに失敗");
        Assert.DoesNotContain("変更していません", messages);
        StringAssert.Contains(results[^1].StandardError, "復元情報は保持");
    }

    [TestMethod]
    public async Task ApplyAsync_NonPhysicalAdapterIsSkippedWithoutMutation()
    {
        var executor = new FakeExecutor();
        executor.Success(WindowsGamingNetworkProfileParserTests.State(
            hardwareInterface: false,
            interfaceType: 71,
            properties: []));
        var settings = new FakeSettingsService();
        var service = Create(executor, settings);

        var results = await service.ApplyAsync("Wi-Fi");

        Assert.HasCount(1, executor.Calls);
        Assert.AreEqual(0, settings.SaveCount);
        Assert.IsEmpty(settings.Current.GamingNetworkProfileSnapshots);
        StringAssert.Contains(results[^1].StandardError, "仮想 NIC");
    }

    [TestMethod]
    public async Task ApplyAsync_UnknownWifiWithoutStandardPropertyIsInformativeNoOp()
    {
        var executor = new FakeExecutor();
        executor.Success(WindowsGamingNetworkProfileParserTests.State(
            interfaceType: 71,
            description: "Unknown Wi-Fi",
            driverProvider: "Unknown Vendor",
            pnpDeviceId: "PCI\\VEN_9999&DEV_0001",
            properties: []));
        var settings = new FakeSettingsService();
        var service = Create(executor, settings);

        var results = await service.ApplyAsync("Wi-Fi");

        Assert.HasCount(1, executor.Calls);
        Assert.AreEqual(0, settings.SaveCount);
        Assert.IsEmpty(settings.Current.GamingNetworkProfileSnapshots);
        StringAssert.Contains(results[^1].StandardError, "*InterruptModeration");
        StringAssert.Contains(results[^1].StandardError, "未知の機種を推測せず");
    }

    [TestMethod]
    public async Task ApplyAsync_MediaTekWifiAppliesOnlyChangedPowerPropertyAndReportsTradeoff()
    {
        var executor = new FakeExecutor();
        executor.Success(WifiState(("LowPowerEnable", "1"), ("UAPSDSupport", "0")));
        executor.Success("UPDATED=LowPowerEnable;VALUE=0");
        executor.Success(WifiState(("LowPowerEnable", "0"), ("UAPSDSupport", "0")));
        var settings = new FakeSettingsService();
        var service = Create(executor, settings);

        var results = await service.ApplyAsync("Wi-Fi");

        Assert.IsTrue(results[^1].Success);
        Assert.AreEqual(3, executor.Calls.Count);
        var properties = settings.Current.GamingNetworkProfileSnapshots.Single().Properties;
        Assert.HasCount(1, properties);
        Assert.AreEqual("LowPowerEnable", properties[0].RegistryKeyword);
        StringAssert.Contains(results[^1].StandardOutput, "バッテリー駆動時間の短縮");
        StringAssert.Contains(results[^1].StandardOutput, "PC の再起動が必要");
        StringAssert.Contains(results[^1].StandardOutput, "実際のゲーム性能・遅延は未検証");
    }

    [TestMethod]
    public async Task ApplyAsync_PhysicalEthernetWithNoAllowlistedPropertiesIsIncompatibleNoOp()
    {
        var executor = new FakeExecutor();
        executor.Success(State());
        var settings = new FakeSettingsService();
        var service = Create(executor, settings);

        var results = await service.ApplyAsync("Ethernet");

        Assert.HasCount(1, executor.Calls);
        Assert.AreEqual(0, settings.SaveCount);
        Assert.IsEmpty(settings.Current.GamingNetworkProfileSnapshots);
        Assert.IsFalse(results[^1].Success);
        StringAssert.Contains(results[^1].StandardError, "安全に確認できる対象プロパティ");
        StringAssert.Contains(results[^1].StandardError, "再起動も不要");
    }

    [TestMethod]
    public async Task ApplyAsync_MissingStandardPropertyIsDocumentedAsUnsupported()
    {
        var executor = new FakeExecutor();
        executor.Success(State(("*EEE", "1")));
        executor.Success("UPDATED=*EEE;VALUE=0");
        executor.Success(State(("*EEE", "0")));
        var settings = new FakeSettingsService();
        var service = Create(executor, settings);

        var results = await service.ApplyAsync("Ethernet");

        Assert.IsTrue(results[^1].Success);
        StringAssert.Contains(results[^1].StandardOutput, "*InterruptModeration");
        StringAssert.Contains(results[^1].StandardOutput, "非対応としてスキップ");
    }

    [TestMethod]
    public async Task ApplyAsync_SnapshotContainsOnlyPropertiesActuallyChanged()
    {
        var executor = new FakeExecutor();
        executor.Success(State(("*InterruptModeration", "1"), ("*EEE", "0")));
        executor.Success("UPDATED=*InterruptModeration;VALUE=0");
        executor.Success(State(("*InterruptModeration", "0"), ("*EEE", "0")));
        var settings = new FakeSettingsService();
        var service = Create(executor, settings);

        var results = await service.ApplyAsync("Ethernet");

        Assert.IsTrue(results[^1].Success);
        var properties = settings.Current.GamingNetworkProfileSnapshots.Single().Properties;
        Assert.HasCount(1, properties);
        Assert.AreEqual("*InterruptModeration", properties[0].RegistryKeyword);
        Assert.AreEqual("1", properties[0].OriginalValue);
    }

    [TestMethod]
    public async Task ApplyAsync_NullJournalEntryFailsSafelyWithoutWriting()
    {
        var executor = new FakeExecutor();
        executor.Success(State(("*EEE", "1")));
        var settings = new FakeSettingsService();
        settings.Current.GamingNetworkProfileSnapshots.Add(null!);
        var service = Create(executor, settings);

        var results = await service.ApplyAsync("Ethernet");

        Assert.HasCount(1, executor.Calls);
        Assert.AreEqual(0, settings.SaveCount);
        Assert.IsFalse(results[^1].Success);
        StringAssert.Contains(results[^1].StandardError, "null");
    }

    [TestMethod]
    public async Task RestoreAsync_SuccessRestoresAndRemovesJournalOnlyAfterVerification()
    {
        var executor = new FakeExecutor();
        executor.Success(State(("*EEE", "0")));
        executor.Success("UPDATED=*EEE;VALUE=1");
        executor.Success(State(("*EEE", "1")));
        var settings = SettingsWithJournal("Intel Ethernet Controller", ("*EEE", "1"));
        var service = Create(executor, settings);

        var results = await service.RestoreAsync("Ethernet");

        Assert.AreEqual(3, executor.Calls.Count);
        Assert.AreEqual(1, settings.SaveCount);
        Assert.IsEmpty(settings.Current.GamingNetworkProfileSnapshots);
        Assert.IsTrue(results[^1].Success);
        StringAssert.Contains(results[^1].StandardOutput, "復元情報の削除も保存");
    }

    [TestMethod]
    public async Task RestoreAsync_MediaTekWifiRestoresPowerPropertyAndRemovesJournal()
    {
        var executor = new FakeExecutor();
        executor.Success(WifiState(("LowPowerEnable", "0"), ("UAPSDSupport", "0")));
        executor.Success("UPDATED=LowPowerEnable;VALUE=1");
        executor.Success(WifiState(("LowPowerEnable", "1"), ("UAPSDSupport", "0")));
        var settings = SettingsWithJournal("RZ616 Wi-Fi 6E 160MHz", ("LowPowerEnable", "1"));
        var service = Create(executor, settings);

        var results = await service.RestoreAsync("Wi-Fi");

        Assert.IsTrue(results[^1].Success);
        Assert.IsEmpty(settings.Current.GamingNetworkProfileSnapshots);
        StringAssert.Contains(results[^1].StandardOutput, "バッテリー駆動時間の短縮");
    }

    [TestMethod]
    public async Task RestoreAsync_TamperedCrossMediaJournalFailsBeforeWrite()
    {
        var executor = new FakeExecutor();
        executor.Success(WifiState(("LowPowerEnable", "0")));
        var settings = SettingsWithJournal("RZ616 Wi-Fi 6E 160MHz", ("*EEE", "1"));
        var service = Create(executor, settings);

        var results = await service.RestoreAsync("Wi-Fi");

        Assert.HasCount(1, executor.Calls);
        Assert.HasCount(1, settings.Current.GamingNetworkProfileSnapshots);
        Assert.IsFalse(results[^1].Success);
        StringAssert.Contains(results[^1].StandardError, "対象外");
    }

    [TestMethod]
    public async Task RestoreAsync_TamperedMediaTekJournalOnUnknownWifiFailsBeforeWrite()
    {
        var executor = new FakeExecutor();
        executor.Success(WindowsGamingNetworkProfileParserTests.State(
            interfaceType: 71,
            description: "Unknown Wi-Fi",
            driverProvider: "Unknown Vendor",
            pnpDeviceId: "PCI\\VEN_9999&DEV_0001",
            properties: []));
        var settings = SettingsWithJournal("Unknown Wi-Fi", ("LowPowerEnable", "1"));
        var service = Create(executor, settings);

        var results = await service.RestoreAsync("Wi-Fi");

        Assert.HasCount(1, executor.Calls);
        Assert.HasCount(1, settings.Current.GamingNetworkProfileSnapshots);
        Assert.IsFalse(results[^1].Success);
        StringAssert.Contains(results[^1].StandardError, "対象外");
    }

    [TestMethod]
    public async Task ApplyAsync_ReadbackProviderMismatchRetainsJournal()
    {
        var executor = new FakeExecutor();
        executor.Success(WifiState(("*InterruptModeration", "1")));
        executor.Success("UPDATED=*InterruptModeration;VALUE=0");
        executor.Success(WindowsGamingNetworkProfileParserTests.State(
            interfaceType: 71,
            description: "RZ616 Wi-Fi 6E 160MHz",
            driverProvider: "Unknown Vendor",
            pnpDeviceId: "PCI\\VEN_9999&DEV_0001",
            properties: [("*InterruptModeration", "0")]));
        var settings = new FakeSettingsService();
        var service = Create(executor, settings);

        var results = await service.ApplyAsync("Wi-Fi");

        Assert.HasCount(1, settings.Current.GamingNetworkProfileSnapshots);
        Assert.IsFalse(results[^1].Success);
        StringAssert.Contains(results[^1].StandardError, "読み戻し確認できません");
    }

    [TestMethod]
    public async Task RestoreAsync_IdentityMismatchDoesNotWriteAndRetainsJournal()
    {
        var executor = new FakeExecutor();
        executor.Success(WindowsGamingNetworkProfileParserTests.State(
            description: "Different Controller", properties: [("*EEE", "0")]));
        var settings = SettingsWithJournal("Intel Ethernet Controller", ("*EEE", "1"));
        var service = Create(executor, settings);

        var results = await service.RestoreAsync("Ethernet");

        Assert.HasCount(1, executor.Calls);
        Assert.HasCount(1, settings.Current.GamingNetworkProfileSnapshots);
        StringAssert.Contains(results[^1].StandardError, "一致しません");
    }

    [TestMethod]
    public async Task RestoreAsync_ExternalDriftDoesNotOverwriteAndRetainsJournal()
    {
        var executor = new FakeExecutor();
        executor.Success(State(("*EEE", "1")));
        var settings = SettingsWithJournal("Intel Ethernet Controller", ("*EEE", "0"));
        var service = Create(executor, settings);

        var results = await service.RestoreAsync("Ethernet");

        Assert.HasCount(1, executor.Calls);
        Assert.AreEqual(0, settings.SaveCount);
        Assert.HasCount(1, settings.Current.GamingNetworkProfileSnapshots);
        StringAssert.Contains(results[^1].StandardError, "外部変更を上書きしません");
    }

    [TestMethod]
    public async Task RestoreAsync_MissingNicDoesNotMutateJournal()
    {
        var executor = new FakeExecutor();
        executor.Failure("Adapter resolution was not unique");
        var settings = SettingsWithJournal("Intel Ethernet Controller", ("*EEE", "1"));
        var service = Create(executor, settings);

        var results = await service.RestoreAsync("Missing Ethernet");

        Assert.HasCount(1, executor.Calls);
        Assert.HasCount(1, settings.Current.GamingNetworkProfileSnapshots);
        Assert.IsFalse(results[^1].Success);
        StringAssert.Contains(results[^1].StandardError, "一意に取得できず");
    }

    [TestMethod]
    public async Task RestoreAsync_SaveFailureRollsBackJournalRemoval()
    {
        var executor = new FakeExecutor();
        executor.Success(State(("*EEE", "0")));
        executor.Success("UPDATED=*EEE;VALUE=1");
        executor.Success(State(("*EEE", "1")));
        var settings = SettingsWithJournal("Intel Ethernet Controller", ("*EEE", "1"));
        settings.SaveException = new IOException("disk full");
        var service = Create(executor, settings);

        var results = await service.RestoreAsync("Ethernet");

        Assert.HasCount(1, settings.Current.GamingNetworkProfileSnapshots);
        Assert.IsFalse(results[^1].Success);
        StringAssert.Contains(results[^1].StandardError, "保存できない");
    }

    [TestMethod]
    public async Task RestoreAsync_PartialFailureCanRetryRemainingPropertyWithoutLosingJournal()
    {
        var firstExecutor = new FakeExecutor();
        firstExecutor.Success(State(("*InterruptModeration", "0"), ("*EEE", "0")));
        firstExecutor.Success("UPDATED=*InterruptModeration;VALUE=1");
        firstExecutor.Failure("driver temporarily rejected EEE");
        var settings = SettingsWithJournal(
            "Intel Ethernet Controller",
            ("*InterruptModeration", "1"),
            ("*EEE", "1"));
        var firstService = Create(firstExecutor, settings);

        var first = await firstService.RestoreAsync("Ethernet");

        Assert.IsFalse(first[^1].Success);
        Assert.HasCount(1, settings.Current.GamingNetworkProfileSnapshots);

        var retryExecutor = new FakeExecutor();
        retryExecutor.Success(State(("*InterruptModeration", "1"), ("*EEE", "0")));
        retryExecutor.Success("UPDATED=*EEE;VALUE=1");
        retryExecutor.Success(State(("*InterruptModeration", "1"), ("*EEE", "1")));
        var retryService = Create(retryExecutor, settings);

        var retry = await retryService.RestoreAsync("Ethernet");

        Assert.IsTrue(retry[^1].Success);
        Assert.AreEqual(3, retryExecutor.Calls.Count);
        Assert.IsEmpty(settings.Current.GamingNetworkProfileSnapshots);
        Assert.AreEqual(1, settings.SaveCount);
    }

    private static WindowsGamingNetworkProfileService Create(FakeExecutor executor, FakeSettingsService settings) =>
        new(executor, settings, new FakeGate());

    private static string State(params (string Keyword, string Value)[] properties) =>
        WindowsGamingNetworkProfileParserTests.State(properties: properties);

    private static string WifiState(params (string Keyword, string Value)[] properties) =>
        WindowsGamingNetworkProfileParserTests.State(
            interfaceType: 71,
            description: "RZ616 Wi-Fi 6E 160MHz",
            driverProvider: "MediaTek, Inc.",
            pnpDeviceId: "PCI\\VEN_14C3&DEV_0616&SUBSYS_061614C3",
            properties: properties);

    private static FakeSettingsService SettingsWithJournal(
        string description,
        params (string Keyword, string OriginalValue)[] properties)
    {
        var settings = new FakeSettingsService();
        settings.Current.GamingNetworkProfileSnapshots.Add(new GamingNetworkProfileSnapshot
        {
            AdapterGuid = AdapterGuid,
            AdapterDescription = description,
            Properties = properties.Select(property => new GamingNetworkPropertySnapshot
            {
                RegistryKeyword = property.Keyword,
                OriginalValue = property.OriginalValue,
            }).ToList(),
        });
        return settings;
    }

    private sealed class FakeExecutor : ICommandExecutor
    {
        private readonly Queue<CommandExecutionResult> responses = new();

        public List<(string FileName, string Arguments)> Calls { get; } = [];

        public int? CancelOnCall { get; init; }

        public void Success(string output) => responses.Enqueue(new(true, "fake", 0, output, string.Empty));

        public void Failure(string error) => responses.Enqueue(new(false, "fake", 1, string.Empty, error));

        public Task<CommandExecutionResult> RunAsync(string fileName, string arguments, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Calls.Add((fileName, arguments));
            if (Calls.Count == CancelOnCall)
            {
                throw new OperationCanceledException(ct);
            }

            return Task.FromResult(responses.Dequeue());
        }
    }

    private sealed class FakeSettingsService : ISettingsService
    {
        public AppSettings Current { get; } = new();

        public int SaveCount { get; private set; }

        public Exception? SaveException { get; set; }

        public bool CancelOnSave { get; init; }

        public Task SaveAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            SaveCount++;
            if (CancelOnSave)
            {
                return Task.FromException(new OperationCanceledException(ct));
            }

            return SaveException is null ? Task.CompletedTask : Task.FromException(SaveException);
        }
    }

    private sealed class FakeGate : INetworkMutationGate
    {
        public int EnterCount { get; private set; }

        public Task<IDisposable> EnterAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            EnterCount++;
            return Task.FromResult<IDisposable>(new Lease());
        }

        private sealed class Lease : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}
