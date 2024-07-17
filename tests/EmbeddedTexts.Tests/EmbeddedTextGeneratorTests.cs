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
        var result = RunGeneration(GetGlobalOptions(false), FakeText.Default);
        Assert.AreEqual(1, result.Results.Length);
        Assert.AreEqual(0, result.Results[0].GeneratedSources.Length);
    }

    [TestMethod]
    public void GeneratesForOptInOnly()
    {
        var result = RunGeneration(GetGlobalOptions(false), FakeText.Default, FakeText.Enabled, FakeText.Disabled);

        Assert.IsTrue(result.Results.Single().GeneratedSources.Single().SyntaxTree.ToString().Contains("Enabled Content"));
    }

    [TestMethod]
    public void DoesntGenerateForOptOut()
    {
        var result = RunGeneration(GetGlobalOptions(true), FakeText.Default, FakeText.Enabled, FakeText.Disabled);
        Assert.AreEqual(1, result.Results.Length);
        Assert.AreEqual(2, result.Results[0].GeneratedSources.Length);
    }

    [TestMethod]
    public void GeneratesStructurallyEquivalentResult()
    {
        // Generates a single source as per GeneratesForOptInOnly
        var result = RunGeneration(FakeText.Default);
        var source = result.Results.Single().GeneratedSources.Single();

        var expected = CSharpSyntaxTree.ParseText(""""
            namespace Project;
            
            public static partial class Default_txt
            {
                public static string Content => """
            Default Content
            """;
            }
            """").GetRoot();
        var actual = source.SyntaxTree.GetRoot();
        Assert.IsTrue(SyntaxFactory.AreEquivalent(expected, actual, ignoreChildNode: SyntaxFacts.IsTrivia));
    }

    [DataTestMethod]
    [DataRow($@"{ProjectRoot}//Default.txt", null, null, "Project", "Default_txt")]
    [DataRow($@"{ProjectRoot}//Subdirectory//2 Another &  Subdirectory/File | Weird.ini", null, null, "Project.Subdirectory._2_Another____Subdirectory", "File___Weird_ini")]
    [DataRow($@"{ProjectRoot}//CustomNamespace.txt", "TestCustomNamespace", null, "TestCustomNamespace", "CustomNamespace_txt")]
    [DataRow($@"{ProjectRoot}//CustomClassName.txt", null, "TestCustomClassName", "Project", "TestCustomClassName")]
    [DataRow($@"{ProjectRoot}//CustomNamespaceAndClass.txt", "TestCustomNamespace", "TestCustomClassName", "TestCustomNamespace", "TestCustomClassName")]
    public void GeneratesCorrectNamespacesAndClassNames(string path, string? namespaceProperty, string? classProperty, string expectedNamespace, string expectedClassName)
    {
        var options = new Dictionary<string, string?>();
        if (namespaceProperty != null)
            options[$"build_metadata.additionalfiles.{EmbeddedTextsGenerator.EmbedTextNamespaceMetadataProperty}"] = namespaceProperty;
        if (classProperty != null)
            options[$"build_metadata.additionalfiles.{EmbeddedTextsGenerator.EmbedTextClassNameMetadataProperty}"] = classProperty;
        var result = RunGeneration(new FakeText(path, $"{path} Contents", options));
        var sourceNodes = result.Results.Single().GeneratedSources.Single().SyntaxTree.GetRoot().DescendantNodes();
        var @namespace = sourceNodes.OfType<FileScopedNamespaceDeclarationSyntax>().FirstOrDefault()?.Name.ToString();
        var className = sourceNodes.OfType<ClassDeclarationSyntax>().FirstOrDefault()?.Identifier.ToString();
        Assert.AreEqual(expectedNamespace, @namespace);
        Assert.AreEqual(expectedClassName, className);
    }

    [DataTestMethod]
    [DataRow(null, null, "public static string Content => ")]
    [DataRow(false, null, "public static string Content => ")]
    [DataRow(true, null, "public const string Content = ")]
    [DataRow(null, "CustomIdentifier", "public static string CustomIdentifier => ")]
    [DataRow(true, "CustomIdentifier", "public const string CustomIdentifier = ")]
    public void GeneratesConstantsAndCustomIdentifiersOnDemand(bool? constantConfigValue, string? customIdentifier, string expectedDeclaration)
    {
        var options = new Dictionary<string, string?>();
        if (constantConfigValue is not null)
            options[$"build_metadata.additionalfiles.{EmbeddedTextsGenerator.EmbedTextIsConstMetadataProperty}"] = constantConfigValue.ToString();
        if (customIdentifier is not null)
            options[$"build_metadata.additionalfiles.{EmbeddedTextsGenerator.EmbedTextIdentifierMetadataProperty}"] = customIdentifier;
        var result = RunGeneration(new FakeText($@"{ProjectRoot}//File", "Contents", options));
        var source = result.Results.Single().GeneratedSources.Single();
        var actualDeclaration = source.SyntaxTree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().SingleOrDefault()?.DescendantNodes().OfType<MemberDeclarationSyntax>().SingleOrDefault()?.ToString();
        Assert.IsNotNull(actualDeclaration);
        Assert.AreEqual(expectedDeclaration, actualDeclaration[0..expectedDeclaration.Length]);
    }

    [DataTestMethod]
    [DataRow(10, 4, 5)]
    [DataRow(10, 15, 10)]
    [DataRow(10, -15, 10)]
    [DataRow(1000, 50, 51)]
    [DataRow(1000, null, 1000)]
    public void GeneratesCustomCommentLines(int fileContentLines, int? limitLineNumber, int expectedLines)
    {
        if (limitLineNumber is < 0)
            limitLineNumber = null;
        var options = new Dictionary<string, string?>();
        if (limitLineNumber is not null)
            options[$"build_metadata.additionalfiles.{EmbeddedTextsGenerator.EmbedTextCommentContentLinesMetadataProperty}"] = limitLineNumber.ToString();
        var lines = Enumerable.Range(1, fileContentLines).Select(n => $"Line [{n}]").ToList();
        var result = RunGeneration(new FakeText($@"{ProjectRoot}//File", string.Join("\n", lines), options));

        var source = result.Results.Single().GeneratedSources.Single();
        var sourceLines = source.SyntaxTree.ToString().Split(["\r\n", "\r", "\n"], StringSplitOptions.TrimEntries);
        var commentCodeLines = sourceLines.SkipWhile(s => s != "/// <code>").Skip(1).TakeWhile(s => s != "/// </code>").ToList();

        if (fileContentLines > 0)
            Assert.IsTrue(commentCodeLines[0] == "/// Line [1]");
        Assert.AreEqual(expectedLines, commentCodeLines.Count);
        if (limitLineNumber is not null && fileContentLines > limitLineNumber)
        {
            Assert.IsTrue(commentCodeLines[^2] == $"/// Line [{limitLineNumber}]");
            Assert.IsTrue(commentCodeLines[^1] == $"/// [{fileContentLines - limitLineNumber} more lines ({fileContentLines} total)]");
        }
    }

    private static IIncrementalGenerator[] Generators { get; } = [new EmbeddedTextsGenerator()];
    private static CSharpCompilation Compilation { get; } = PodCSharpCompilation.Create([]);

    private static GeneratorDriverRunResult RunGeneration(Fakes.AnalyzerConfigOptions globalOptions, params FakeText[] additionalTexts)
    {
        var additionalTextsDictionary = additionalTexts.Select(e => (Text: (AdditionalText)new Fakes.AdditionalText(e.Path, e.Contents), e.Options)).ToDictionary(e => e.Text, e => new Fakes.AnalyzerConfigOptions(e.Options ?? []));
        return Compilation.RunGenerators(
            Generators,
            driver => (CSharpGeneratorDriver)driver
                .AddAdditionalTexts([.. additionalTextsDictionary.Keys])
                .WithUpdatedAnalyzerConfigOptions(new Fakes.AnalyzerConfigOptionsProvider(globalOptions, [], additionalTextsDictionary)));
    }

    private static GeneratorDriverRunResult RunGeneration(params FakeText[] additionalTexts)
        => RunGeneration(NoOptions, additionalTexts);

    private static Fakes.AnalyzerConfigOptions NoOptions { get; } = new()
    {
        ["build_property.rootnamespace"] = "Project",
        ["build_property.projectdir"] = ProjectRoot,
    };

    private static Fakes.AnalyzerConfigOptions GetGlobalOptions(Dictionary<string, string?> additionalValues)
    {
        var options = new Fakes.AnalyzerConfigOptions(NoOptions.Values);
        foreach (var value in additionalValues)
            options.Add(value);
        return options;
    }

    private static Fakes.AnalyzerConfigOptions GetGlobalOptions(bool enabled)
        => GetGlobalOptions(additionalValues: new()
        {
            [$"build_property.{EmbeddedTextsGenerator.EmbedAdditionalTextsConfigProperty}"] = enabled.ToString()
        });

    private record class FakeText(string Path, string Contents, Dictionary<string, string?>? Options = null)
    {
        public FakeText(string Path, string Contents, params (string Key, string? Value)[] Options) : this(Path, Contents, Options.ToDictionary(e => e.Key, e => e.Value)) { }

        public static FakeText Default { get; } = new($@"{ProjectRoot}//Default.txt", "Default Content");
        public static FakeText Enabled { get; } = new($@"{ProjectRoot}//Enabled.txt", "Enabled Content", ($"build_metadata.additionalfiles.{EmbeddedTextsGenerator.EmbedTextMetadataProperty}", "true"));
        public static FakeText Disabled { get; } = new($@"{ProjectRoot}//Disabled.txt", "Disabled Content", ($"build_metadata.additionalfiles.{EmbeddedTextsGenerator.EmbedTextMetadataProperty}", "false"));

        public static FakeText[] GetDefaultItems() => [Default, Enabled, Disabled];
    }
}
