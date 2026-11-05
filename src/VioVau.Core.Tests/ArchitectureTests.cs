namespace VioVau.Core.Tests;

public class ArchitectureTests
{
    [Fact]
    public void Core_DoesNotReferenceGodot()
    {
        var references = typeof(RunState).Assembly.GetReferencedAssemblies().Select(a => a.Name ?? "");
        Assert.DoesNotContain(references, name => name.StartsWith("Godot", StringComparison.OrdinalIgnoreCase));
    }
}
