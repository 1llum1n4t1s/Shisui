using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shisui.Core.Services.Windows;

namespace Shisui.Tests;

[TestClass]
public class WindowsAutoTuningBenchmarkMathTests
{
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
}
