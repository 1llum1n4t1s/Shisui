using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shisui.Core.Models;

namespace Shisui.Tests;

[TestClass]
public class NetworkDiagnosticTargetCatalogTests
{
    [TestMethod]
    public void All_ContainsExpectedTargetsWithoutDuplicateIds()
    {
        var targets = NetworkDiagnosticTargetCatalog.All;

        CollectionAssert.AreEqual(
            new[] { "127.0.0.1", "1.1.1.1", "8.8.8.8", "9.9.9.9", "google.com", "github.com" },
            targets.Select(target => target.Host).ToArray());
        Assert.AreEqual(targets.Count, targets.Select(target => target.Id).Distinct().Count());
        Assert.IsTrue(targets.All(target =>
            !string.IsNullOrWhiteSpace(target.Name) && !string.IsNullOrWhiteSpace(target.Host)));
    }
}
