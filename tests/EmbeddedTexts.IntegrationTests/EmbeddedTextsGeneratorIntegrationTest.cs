using PodNet.Analyzers.Testing.CSharp;

namespace PodNet.EmbeddedTexts.IntegrationTests;

/// <summary>
/// This tests that, given the provided files are correctly added to <c>AdditionaFiles</c>, the correct 
/// classes are generated in the correct namespaces with the correct API surface (property), and it returns 
/// the file content as the result (unless ignored). Most of these "tests" won't even compile if the correct
/// APIs are not generated.
/// </summary>
/// <remarks>
/// Note that testing comments in integrated scenarios is not possible, due to the comments
/// only being available to the IDE and not the runtime. Unit tests can be used to test them.
/// </remarks>
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
        var compilation = PodCSharpCompilation.Create([$$"""
            class ShouldError
            {
                string WontCompile() => {{undefined}}.Content;
            };
            """]);
        var diagnostics = compilation.GetDiagnostics();
        Assert.IsTrue(diagnostics.Any(d => d is
        {
            Severity: Microsoft.CodeAnalysis.DiagnosticSeverity.Error,
            Id: "CS0234", // The type or namespace name '{0}' does not exist in the namespace '{1}' (are you missing an assembly reference?)
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

    [TestMethod]
    public void ConstAndPropertyCanBeCustomized()
    {
        // The following won't compile if the symbol is a property instead of a const.
        const string? content = Files.Const_txt.Content;
        Assert.IsNotNull(content);

        Assert.IsNotNull(Files.CustomPropertyName_txt.TestProperty);
    }
}
