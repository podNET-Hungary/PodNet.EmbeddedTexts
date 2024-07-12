namespace PodNet.EmbeddedTexts.IntegrationTests;

/// <summary>
/// This tests that, given the provided files are correctly added to <c>AdditionaFiles</c>, the correct 
/// classes are generated in the correct namespaces with the correct API surface (property), and it returns 
/// the file content as the result (unless ignored). These "tests" won't even compile if the correct
/// APIs are not generated.
/// </summary>
[TestClass]
public class EmbeddedTextsGeneratorIntegrationTest
{
    [TestMethod]
    public void TestContentIsEmbedded()
    {
        Assert.AreEqual("""
                        Text Content
                        Is Embedded
                        """, Files.Text_txt.Content);
    }

    [TestMethod]
    public void IgnoredContentIsNotEmbedded()
    {
        // Given that the Files\Ignored\** pattern is excluded by setting "PodNet_EmbedText" to "false", the following shouldn't compile:
        // Files.Ignored.Ignored_txt.Content;
        var undefined = "PodNet.EmbeddedTexts.IntegrationTests.Files.Ignored.Ignored_txt";
        var compilation = Analyzers.Testing.CSharp.PodCSharpCompilation.Create([$$"""
            class ShouldError
            {
                string WontCompile() => {{undefined}}.Content;
            };
            """]);
        var diagnostics = compilation.CurrentCompilation.GetDiagnostics();
        Assert.IsTrue(diagnostics.Any(d => d is
        {
            Severity: Microsoft.CodeAnalysis.DiagnosticSeverity.Error,
            Id: "CS0234", // {The type or namespace name '{0}' does not exist in the namespace '{1}' (are you missing an assembly reference?)}
            Location: { SourceSpan: var span, SourceTree: var tree }
        } && tree?.GetText().GetSubText(span).ToString() == undefined));
    }

    [TestMethod]
    public void UnignoredContentIsEmbedded()
    {
        Assert.AreEqual("Unignored", Files.Ignored.Unignored_txt.Content);
    }

    [TestMethod]
    public void CustomClassAndNamespaceNamesCanBeSupplied()
    {
        Assert.IsNotNull(Files.TestClass.Content);
        Assert.IsNotNull(TestNamespace.TestSubNamespace.CustomNamespace_txt.Content);
        Assert.IsNotNull(TestNamespace.TestSubNamespace.TestClass.Content);
    }
}
