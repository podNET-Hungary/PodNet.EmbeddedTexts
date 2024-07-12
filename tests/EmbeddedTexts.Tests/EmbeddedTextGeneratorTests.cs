using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using PodNet.Analyzers.Testing.CSharp;
using System.Collections.Immutable;
using Fakes = PodNet.Analyzers.Testing.CodeAnalysis.Fakes;

namespace PodNet.EmbeddedTexts.Tests;

[TestClass]
public class EmbeddedTextGeneratorTests
{
    private const string ProjectRoot = @"//home//Users//source//Project"; // Don't use Windows drive letter as root, it'll break Linux tests.

    [TestMethod]
    public void DoesntGenerateWhenDisabled()
    {
        var result = RunGeneration(false, false);
        Assert.AreEqual(1, result.Results.Length);
        Assert.AreEqual(0, result.Results[0].GeneratedSources.Length);
    }

    [TestMethod]
    public void GeneratesForOptInOnly()
    {
        var result = RunGeneration(false, true);
        // This is a complex assertion that makes sure there is a single result that contains a single property declaration with the expected content in its initializer. Doesn't check for other structural correctness.
        Assert.AreEqual("Test File 2 Content", ((LiteralExpressionSyntax)result.Results.Single().GeneratedSources.Single().SyntaxTree.GetRoot().DescendantNodes().OfType<PropertyDeclarationSyntax>().Single().Initializer!.Value).ToString().Trim('"').Trim());
    }

    [TestMethod]
    public void DoesntGenerateForOptOut()
    {
        var result = RunGeneration(true, false);
        Assert.AreEqual(1, result.Results.Length);
        Assert.AreEqual(5, result.Results[0].GeneratedSources.Length);
    }

    [TestMethod]
    public void GenerateAllWhenGloballyEnabledAndItemIsOptIn()
    {
        var result = RunGeneration(true, true);
        Assert.AreEqual(1, result.Results.Length);
        Assert.AreEqual(6, result.Results[0].GeneratedSources.Length);
    }

    [TestMethod]
    public void GeneratesStructurallyEquivalentResult()
    {
        // Generates a single source as per GeneratesForOptInOnly
        var result = RunGeneration(false, true);
        var source = result.Results.Single().GeneratedSources.Single();

        var expected = CSharpSyntaxTree.ParseText(""""
            namespace Project;
            
            public static partial class Parameterized_Enabled_cs
            {
                public static string Content { get; } = """
            Test File 2 Content
            """;
            }
            """").GetRoot();
        var actual = source.SyntaxTree.GetRoot();
        Assert.IsTrue(SyntaxFactory.AreEquivalent(expected, actual, ignoreChildNode: SyntaxFacts.IsTrivia));
    }

    [DataTestMethod]
    [DataRow("Default_txt", "Project")]
    [DataRow("Parameterized_Enabled_cs", "Project")]
    [DataRow("CustomNamespace_n", "TestNamespace")]
    [DataRow("TestClassName", "Project")]
    [DataRow("Empty", "Project")]
    [DataRow("Empty_ini", "Project.Subdirectory._2_Another____Subdirectory")]
    public void GeneratesCorrectClassNamesAndNamespaces(string expectedClassName, string expectedNamespace)
    {
        var result = RunGeneration(true, true);
        var source = result.Results.Single().GeneratedSources.SingleOrDefault(s => s.HintName.EndsWith($"{expectedClassName}.g.cs"));
        Assert.IsNotNull(source);
        var @namespace = source.SyntaxTree.GetRoot().DescendantNodes().OfType<FileScopedNamespaceDeclarationSyntax>().FirstOrDefault()?.Name.ToString();
        var className = source.SyntaxTree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().FirstOrDefault()?.Identifier.ToString();
        Assert.AreEqual(expectedNamespace, @namespace);
        Assert.AreEqual(expectedClassName, className);
    }

    public static GeneratorDriverRunResult RunGeneration(bool globalEnabled, bool oneItemEnabled)
    {
        var additionalTextsLookup = GetOptionsForTexts(oneItemEnabled)
            .ToDictionary(f => (AdditionalText)f.Key, f => f.Value);
        var globalOptions = GetGlobalOptions(globalEnabled);
        var optionsProvider = new Fakes.AnalyzerConfigOptionsProvider(globalOptions, [], additionalTextsLookup);
        var compilation = PodCSharpCompilation.Create([]);
        var generator = new EmbeddedTextsGenerator();
        return compilation.RunGenerators(
            [generator],
            driver => (CSharpGeneratorDriver)driver
                .AddAdditionalTexts(additionalTextsLookup.Select(a => a.Key).ToImmutableArray())
                .WithUpdatedAnalyzerConfigOptions(optionsProvider));
    }

    private static Fakes.AnalyzerConfigOptions GetGlobalOptions(bool globalEnabled) => new()
    {
        ["build_property.rootnamespace"] = "Project",
        ["build_property.projectdir"] = ProjectRoot,
        [$"build_property.{EmbeddedTextsGenerator.EmbedAdditionalTextsConfigProperty}"] = globalEnabled.ToString()
    };

    private static Dictionary<Fakes.AdditionalText, Fakes.AnalyzerConfigOptions> GetOptionsForTexts(bool oneItemEnabled) => new()
    {
        [new($@"{ProjectRoot}//Default.txt", "Test File 1 Content")]
            = [],
        [new($@"{ProjectRoot}//Parameterized Enabled.cs", "Test File 2 Content")]
            = new() { [$"build_metadata.additionalfiles.{EmbeddedTextsGenerator.EmbedTextMetadataProperty}"] = oneItemEnabled.ToString() },
        [new($@"{ProjectRoot}//CustomNamespace.n", "Test File 3 Content")]
            = new() { [$"build_metadata.additionalfiles.{EmbeddedTextsGenerator.EmbedTextNamespaceMetadataProperty}"] = "TestNamespace" },
        [new($@"{ProjectRoot}//CustomClassName.n", "Test File 4 Content")]
            = new() { [$"build_metadata.additionalfiles.{EmbeddedTextsGenerator.EmbedTextClassNameMetadataProperty}"] = "TestClassName" },
        [new($@"{ProjectRoot}//Empty", "")] = [],
        [new($@"{ProjectRoot}//Subdirectory//2 Another &  Subdirectory/Empty.ini", "")] = [],
    };
}