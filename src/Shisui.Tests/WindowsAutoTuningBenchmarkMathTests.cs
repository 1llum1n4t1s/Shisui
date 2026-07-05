using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shisui.Core.Services.Windows;

namespace Shisui.Tests;

[TestClass]
public class WindowsAutoTuningBenchmarkMathTests
{
    [TestMethod]
    public void ComputeThroughputMbps_OneMegabytePerSecond_ReturnsEightMbps()
    {
        var mbps = WindowsAutoTuningBenchmarkMath.ComputeThroughputMbps(1_000_000, TimeSpan.FromSeconds(1));
        Assert.AreEqual(8.0, mbps!.Value, 0.0001);
    }

    [TestMethod]
    public void ComputeThroughputMbps_ZeroBytes_ReturnsNull()
    {
        Assert.IsNull(WindowsAutoTuningBenchmarkMath.ComputeThroughputMbps(0, TimeSpan.FromSeconds(1)));
    }

    [TestMethod]
    public void ComputeThroughputMbps_ZeroElapsed_ReturnsNull()
    {
        Assert.IsNull(WindowsAutoTuningBenchmarkMath.ComputeThroughputMbps(1_000_000, TimeSpan.Zero));
    }

    [TestMethod]
    public void ComputeThroughputMbps_TwentyMegabytesInTwoSeconds_ReturnsEightyMbps()
    {
        var mbps = WindowsAutoTuningBenchmarkMath.ComputeThroughputMbps(20_000_000, TimeSpan.FromSeconds(2));
        Assert.AreEqual(80.0, mbps!.Value, 0.0001);
    }

    [TestMethod]
    public void Summarize_EmptyList_ReturnsNull()
    {
        Assert.IsNull(WindowsAutoTuningBenchmarkMath.Summarize([]));
    }

    [TestMethod]
    public void Summarize_MultipleSamples_ReturnsAverageMinMax()
    {
        var summary = WindowsAutoTuningBenchmarkMath.Summarize([50.0, 60.0, 40.0]);

        Assert.IsNotNull(summary);
        Assert.AreEqual(50.0, summary!.Value.Average, 0.0001);
        Assert.AreEqual(40.0, summary.Value.Min, 0.0001);
        Assert.AreEqual(60.0, summary.Value.Max, 0.0001);
    }

    [TestMethod]
    public void Summarize_SingleSample_AverageMinMaxAllEqual()
    {
        var summary = WindowsAutoTuningBenchmarkMath.Summarize([75.5]);

        Assert.IsNotNull(summary);
        Assert.AreEqual(75.5, summary!.Value.Average, 0.0001);
        Assert.AreEqual(75.5, summary.Value.Min, 0.0001);
        Assert.AreEqual(75.5, summary.Value.Max, 0.0001);
    }
}
