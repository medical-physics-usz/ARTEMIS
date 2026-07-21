using System.Reflection;
using USZ_ARTEMIS.Core.Rules;
using Xunit;

namespace USZ_ARTEMIS.Core.Tests;

public sealed class AssemblyIdentityTests
{
    [Fact]
    public void CoreAssemblyName_IsReleaseSpecific()
    {
        Assembly assembly = typeof(RuleStructureResolutionPolicy).Assembly;

        Assert.StartsWith("USZ_ARTEMIS.Core_v", assembly.GetName().Name);
        Assert.NotEqual("USZ_ARTEMIS.Core", assembly.GetName().Name);
    }
}
