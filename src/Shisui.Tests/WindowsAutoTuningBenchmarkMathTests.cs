using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shisui.Core.Services.Windows;

namespace Shisui.Tests;

[TestClass]
public class WindowsAutoTuningBenchmarkMathTests
{
    [TestMethod]
    public void CalculateMegabitsPerSecond_FiveMegabytesInOneSecond_ReturnsFortyMbps()
    {
        var speed = WindowsAutoTuningBenchmarkMath.CalculateMegabitsPerSecond(
            5_000_000, TimeSpan.FromSeconds(1));

        Assert.AreEqual(40.0, speed!.Value, 0.0001);
    }

    [TestMethod]
    public void CalculateMegabitsPerSecond_InvalidInput_ReturnsNull()
    {
        Assert.IsNull(WindowsAutoTuningBenchmarkMath.CalculateMegabitsPerSecond(0, TimeSpan.FromSeconds(1)));
        Assert.IsNull(WindowsAutoTuningBenchmarkMath.CalculateMegabitsPerSecond(1, TimeSpan.Zero));
    }

    [TestMethod]
    public void SummarizePingMilliseconds_EmptyList_ReturnsNull()
    {
        Assert.IsNull(WindowsAutoTuningBenchmarkMath.SummarizePingMilliseconds([]));
    }

    [TestMethod]
    public void SummarizePingMilliseconds_MultipleSamples_ReturnsAverageMinMax()
    {
        var summary = WindowsAutoTuningBenchmarkMath.SummarizePingMilliseconds([12.0, 18.0, 15.0]);

        Assert.IsNotNull(summary);
        Assert.AreEqual(15.0, summary!.Value.Average, 0.0001);
        Assert.AreEqual(12.0, summary.Value.Min, 0.0001);
        Assert.AreEqual(18.0, summary.Value.Max, 0.0001);
    }

    [TestMethod]
    public void SummarizePingMilliseconds_SingleSample_AverageMinMaxAllEqual()
    {
        var summary = WindowsAutoTuningBenchmarkMath.SummarizePingMilliseconds([9.5]);

        Assert.IsNotNull(summary);
        Assert.AreEqual(9.5, summary!.Value.Average, 0.0001);
        Assert.AreEqual(9.5, summary.Value.Min, 0.0001);
        Assert.AreEqual(9.5, summary.Value.Max, 0.0001);
    }

    [TestMethod]
    public void SummarizeMegabitsPerSecond_MultipleSamples_ReturnsAverageMinMax()
    {
        var summary = WindowsAutoTuningBenchmarkMath.SummarizeMegabitsPerSecond([400.0, 500.0, 600.0]);

        Assert.IsNotNull(summary);
        Assert.AreEqual(500.0, summary!.Value.Average, 0.0001);
        Assert.AreEqual(400.0, summary.Value.Min, 0.0001);
        Assert.AreEqual(600.0, summary.Value.Max, 0.0001);
    }
}
