using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using PodNet.Analyzers.Testing.CSharp;
using PodNet.Analyzers.Testing.Diffing;
using System.Diagnostics;
using System.Reflection;
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
            
            public partial class Default_txt
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
    [DataRow(1000, null, 21)]
    [DataRow(1000, 10000, 1000)]
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
        var commentCodeLines = sourceLines.SkipWhile(s => s != "/// <code>").SkipWhile(s => s != "/// <![CDATA[").Skip(1).TakeWhile(s => s != "/// ]]>").ToList();

        if (fileContentLines > 0)
            Assert.IsTrue(commentCodeLines[0] == "/// Line [1]");
        Assert.AreEqual(expectedLines, commentCodeLines.Count);
        if (limitLineNumber is not null && fileContentLines > limitLineNumber)
        {
            Assert.IsTrue(commentCodeLines[^2] == $"/// Line [{limitLineNumber}]");
            Assert.IsTrue(commentCodeLines[^1] == $"/// [{fileContentLines - limitLineNumber} more lines ({fileContentLines} total)]");
        }
    }

    [TestMethod]
    public void ClassIsNotStaticByDefault()
    {
        var result = RunGeneration(new FakeText($@"{ProjectRoot}//File", ""));
        var source = result.Results.Single().GeneratedSources.Single();

        var @class = source.SyntaxTree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().Single();

        Assert.IsTrue(@class.Modifiers.All(m => m.Kind() is not SyntaxKind.StaticKeyword));
    }

    [TestMethod]
    public void ClassCanBeMadeStatic()
    {
        var result = RunGeneration(new FakeText($@"{ProjectRoot}//File", "", ($"build_metadata.additionalfiles.{EmbeddedTextsGenerator.EmbedTextIsStaticClassMetadataProperty}", "true")));

        var source = result.Results.Single().GeneratedSources.Single();
        var @class = source.SyntaxTree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().Single();

        Assert.IsTrue(@class.Modifiers.Any(m => m.Kind() is SyntaxKind.StaticKeyword));
    }

    [TestMethod]
    public void DirectoryAsClassGeneratesCorrectOutput()
    {
        var directoryAsClass = ($"build_metadata.additionalfiles.{EmbeddedTextsGenerator.EmbedTextDirectoryAsClassMetadataProperty}", "true");
        var result = RunGeneration(
            new FakeText($"{ProjectRoot}/Namespace1/Namespace2/ClassName/Property1", "File1 Contents", directoryAsClass), 
            new FakeText($"{ProjectRoot}/Namespace1/Namespace2/ClassName/Property2", "File2 Contents", directoryAsClass));

        if (result.Results.Single().GeneratedSources is not [var one, var two])
            throw new AssertFailedException("Expected two sources generated.");

        var nsOne = one.SyntaxTree.GetRoot().DescendantNodes().OfType<FileScopedNamespaceDeclarationSyntax>().Single();
        var nsTwo = two.SyntaxTree.GetRoot().DescendantNodes().OfType<FileScopedNamespaceDeclarationSyntax>().Single();
        Assert.AreEqual(nsOne.Name.ToString(), nsTwo.Name.ToString());
        Assert.AreEqual("Project.Namespace1.Namespace2", nsOne.Name.ToString());

        var classOne = one.SyntaxTree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().Single();
        var classTwo = two.SyntaxTree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>().Single();
        Assert.AreEqual(classOne.Identifier.ToString(), classTwo.Identifier.ToString());
        Assert.AreEqual("ClassName", classOne.Identifier.ToString());

        Assert.AreEqual("Property1", classOne.DescendantNodes().OfType<PropertyDeclarationSyntax>().Single().Identifier.ToString());
    }

    [TestMethod]
    public void LargeFileEmbeddingStaysPerfomant()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("PodNet.EmbeddedTexts.Tests.LargeData.json") ?? throw new InvalidOperationException("The large data file was not found or the stream couldn't be opened");
        using var reader = new StreamReader(stream);
        var largeData = reader.ReadToEnd();
        var stopwatch = Stopwatch.StartNew();
        var result = RunGeneration(new FakeText($@"{ProjectRoot}//LargeData.json", largeData));
        stopwatch.Stop();
        Assert.IsTrue(stopwatch.ElapsedMilliseconds < 800);
        if (result.GeneratedTrees is [var tree])
        {
            var properties = tree.GetRoot().DescendantNodes().OfType<PropertyDeclarationSyntax>().ToList();
            Assert.AreEqual(1, properties.Count);
            var propertyBodyLiteral = properties.Single().ExpressionBody?.Expression as LiteralExpressionSyntax;
            Assert.IsNotNull(propertyBodyLiteral);
            var diff = TextDiff.InlineDiff(propertyBodyLiteral.Token.ValueText, largeData, false);
            Assert.IsNull(diff, $"The expected property value body was different to the actual contents of the file.\r\n{diff}");
        }
        else
            Assert.Fail($"Expected one tree to be generated, but got {result.GeneratedTrees.Length}");
    }

    [TestMethod]
    public void RelativeFilesOutsideProjectDirThrowWhenNamespaceIsNotDefined()
    {
        var result = RunGeneration(new FakeText($@"//home//Users//source//OtherProject//TestData.json", "Test content"));
        Assert.AreEqual(0, result.GeneratedTrees.Length);
        Assert.AreEqual(1, result.Diagnostics.Length);
        Assert.IsTrue(result.Diagnostics.Single().Descriptor == EmbeddedTextsGenerator.InvalidRelativePathDescriptor);
    }

    [TestMethod]
    public void RelativeFilesOutsideProjectDirWorkWhenNamespaceIsDefined()
    {
        var result = RunGeneration(new FakeText($@"//home//Users//source//OtherProject//TestData.json", "Test content", ($"build_metadata.additionalfiles.{EmbeddedTextsGenerator.EmbedTextNamespaceMetadataProperty}", "TestNamespace")));
        Assert.AreEqual(1, result.GeneratedTrees.Length);
        Assert.AreEqual(0, result.Diagnostics.Length);

        var result2 = RunGeneration(new FakeText($@"//home//Users//source//OtherProject//TestData.json", "Test content", ($"build_metadata.additionalfiles.{EmbeddedTextsGenerator.EmbedTextDirectoryAsClassMetadataProperty}", "true")));
        Assert.AreEqual(1, result2.GeneratedTrees.Length);
        Assert.AreEqual(0, result2.Diagnostics.Length);
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
