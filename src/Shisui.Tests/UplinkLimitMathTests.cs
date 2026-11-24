using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shisui.Core.Services;

namespace Shisui.Tests;

[TestClass]
public class UplinkLimitMathTests
{
    // 候補は 95/90/85/80/75/70 (添字 0〜5)。大きい % ほど速い。

    [TestMethod]
    public void NextSearchIndex_StartsInTheMiddle()
    {
        Assert.AreEqual(2, UplinkLimitMath.NextSearchIndex([]));
    }

    [TestMethod]
    public void NextSearchIndex_AfterPass_MovesTowardFasterCandidates()
    {
        Assert.AreEqual(0, UplinkLimitMath.NextSearchIndex([(2, true)]));
    }

    [TestMethod]
    public void NextSearchIndex_AfterFail_MovesTowardSlowerCandidates()
    {
        Assert.AreEqual(4, UplinkLimitMath.NextSearchIndex([(2, false)]));
    }

    [TestMethod]
    public void NextSearchIndex_PassThenFail_PicksTheRemainingOne()
    {
        Assert.AreEqual(1, UplinkLimitMath.NextSearchIndex([(2, true), (0, false)]));
    }

    [TestMethod]
    public void NextSearchIndex_EndsWhenNoCandidateRemains()
    {
        Assert.IsNull(UplinkLimitMath.NextSearchIndex([(2, true), (0, true)]));
        Assert.IsNull(UplinkLimitMath.NextSearchIndex([(2, false), (4, false), (5, false)]));
    }

    [TestMethod]
    public void NextSearchIndex_StopsAtThreeTrials()
    {
        Assert.IsNull(UplinkLimitMath.NextSearchIndex([(2, false), (4, true), (3, true)]));
    }

    [TestMethod]
    public void NextSearchIndex_AnyOutcomeSequence_FinishesWithinThreeTrials()
    {
        for (var mask = 0; mask < 8; mask++)
        {
            var history = new List<(int Index, bool Passed)>();
            while (UplinkLimitMath.NextSearchIndex(history) is { } index)
            {
                history.Add((index, (mask >> history.Count & 1) == 1));
            }

            Assert.IsTrue(history.Count <= UplinkLimitMath.MaxSearchTrials, $"mask={mask}");
        }
    }
}
