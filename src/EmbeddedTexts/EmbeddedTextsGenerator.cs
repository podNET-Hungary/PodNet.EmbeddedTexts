using Microsoft.CodeAnalysis;
using PodNet.Analyzers.CodeAnalysis;
using System.Text;

namespace PodNet.EmbeddedTexts;

[Generator(LanguageNames.CSharp)]
public sealed class EmbeddedTextsGenerator : IIncrementalGenerator
{
    public const string EmbedAdditionalTextsConfigProperty = "PodNetAutoEmbedAdditionalTexts";
    public const string EmbedTextMetadataProperty = "PodNet_EmbedText";
    public const string EmbedTextNamespaceMetadataProperty = "PodNet_EmbedTextNamespace";
    public const string EmbedTextClassNameMetadataProperty = "PodNet_EmbedTextClassName";
    public const string EmbedTextIsConstMetadataProperty = "PodNet_EmbedTextIsConst";
    public const string EmbedTextIdentifierMetadataProperty = "PodNet_EmbedTextIdentifier";
    public const string EmbedTextCommentContentLinesMetadataProperty = "PodNet_EmbedTextCommentContentLines";
    public const string EmbedTextIsStaticClassMetadataProperty = "PodNet_EmbedTextIsStaticClass";
    public const string EmbedTextDirectoryAsClassMetadataProperty = "PodNet_EmbedTextDirectoryAsClass";

    public record EmbeddedTextItemOptions(
        string? RootNamespace,
        string? ProjectDirectory,
        string? ItemNamespace,
        string? ItemClassName,
        bool IsConst,
        string? Identifier,
        uint? CommentContentLines,
        bool IsStaticClass,
        bool DirectoryAsClass,
        bool Enabled,
        AdditionalText Text);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var allTexts = context.AdditionalTextsProvider.Combine(context.AnalyzerConfigOptionsProvider)
            .Select((item, _) =>
            {
                var (text, options) = item;
                var itemOptions = options.GetOptions(text);
                var globalEnabled = string.Equals(options.GlobalOptions.GetBuildProperty(EmbedAdditionalTextsConfigProperty) ?? "true", "true", StringComparison.OrdinalIgnoreCase);
                var itemSwitch = itemOptions.GetAdditionalTextMetadata(EmbedTextMetadataProperty);
                var itemEnabled = string.Equals(itemSwitch, "true", StringComparison.OrdinalIgnoreCase);
                var itemDisabled = string.Equals(itemSwitch, "false", StringComparison.OrdinalIgnoreCase);
                return new EmbeddedTextItemOptions(
                    RootNamespace: options.GlobalOptions.GetRootNamespace(),
                    ProjectDirectory: options.GlobalOptions.GetProjectDirectory(),
                    ItemNamespace: itemOptions.GetAdditionalTextMetadata(EmbedTextNamespaceMetadataProperty),
                    ItemClassName: itemOptions.GetAdditionalTextMetadata(EmbedTextClassNameMetadataProperty),
                    IsConst: string.Equals(itemOptions.GetAdditionalTextMetadata(EmbedTextIsConstMetadataProperty), "true", StringComparison.OrdinalIgnoreCase),
                    Identifier: itemOptions.GetAdditionalTextMetadata(EmbedTextIdentifierMetadataProperty),
                    CommentContentLines: uint.TryParse(itemOptions.GetAdditionalTextMetadata(EmbedTextCommentContentLinesMetadataProperty), out var commentLines) ? commentLines : 20,
                    IsStaticClass: string.Equals(itemOptions.GetAdditionalTextMetadata(EmbedTextIsStaticClassMetadataProperty), "true", StringComparison.OrdinalIgnoreCase),
                    DirectoryAsClass: string.Equals(itemOptions.GetAdditionalTextMetadata(EmbedTextDirectoryAsClassMetadataProperty), "true", StringComparison.OrdinalIgnoreCase),
                    Enabled: (globalEnabled || itemEnabled) && !itemDisabled,
                    Text: text);
            });

        var enabledTexts = allTexts.Where(e => e.Enabled);

        context.RegisterSourceOutput(enabledTexts, static (context, item) =>
        {
            var text = item.Text.GetText(context.CancellationToken)?.ToString();
            if (text == null)
                return;
            var commentLines = (int)item.CommentContentLines.GetValueOrDefault();
            var lines = commentLines > 0 ? text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None) : [];
            if (item.ProjectDirectory is not { Length: > 0 })
                throw new InvalidOperationException("No project directory.");
            if (item.Text.Path is not { Length: > 0 })
                throw new InvalidOperationException("Path not found for file.");

            string fullDirectory = Path.GetDirectoryName(item.Text.Path);
            var relativeFolderPath = PathProcessing.GetRelativePath(item.ProjectDirectory, fullDirectory);
            var relativeFilePath = PathProcessing.GetRelativePath(item.ProjectDirectory, item.Text.Path);

            var directoryParts = fullDirectory.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (directoryParts.Length == 0)
                throw new InvalidOperationException("Couldn't determine directory.");
            var directory = directoryParts[^1];

            if (item.DirectoryAsClass)
                relativeFolderPath = relativeFolderPath[0..^directory.Length];

            var @namespace = TextProcessing.GetNamespace(item.ItemNamespace is { Length: > 0 }
                ? item.ItemNamespace
                : $"{item.RootNamespace}.{relativeFolderPath}");

            var className = TextProcessing.GetClassName(item.ItemClassName is { Length: > 0 }
                ? item.ItemClassName
                : item.DirectoryAsClass
                    ? directory
                    : Path.GetFileName(item.Text.Path));

            var isConst = item.IsConst is true;
            var modifier = isConst ? "const" : "static";
            var classModifiers = item.IsStaticClass ? "static partial" : "partial";

            var identifierName = TextProcessing.GetClassName(item.Identifier is { Length: > 0 }
                ? item.Identifier
                : item.DirectoryAsClass
                    ? Path.GetFileName(item.Text.Path)
                    : "Content");

            var maxSubsequentQuotes = 0;
            var previousQuotes = 0;
            for (var i = 0; i < text.Length; i++)
            {
                if (text[i] == '\"')
                    maxSubsequentQuotes = Math.Max(maxSubsequentQuotes, ++previousQuotes);
                else
                    previousQuotes = 0;
            }
            var separator = new string('\"', Math.Max(maxSubsequentQuotes + 1, 3));

            var capacity = text.Length + 500 + lines.Take(commentLines).Select(l => l.Length + 6).DefaultIfEmpty(0).Sum();
            var sourceBuilder = new StringBuilder(capacity);

            sourceBuilder.AppendLine($$"""
            // <auto-generated />

            namespace {{@namespace}};

            public {{classModifiers}} class {{className}}
            {
                /// <summary>
                /// Gets the contents of the file at '{{relativeFilePath}}'.
            """);
            if (commentLines > 0)
            {
                sourceBuilder.AppendLine($$"""
                /// <code>
                /// <![CDATA[
            """);

                foreach (var line in commentLines > 0 ? lines.Take(commentLines) : lines)
                {
                    sourceBuilder.Append("/// ");
                    sourceBuilder.AppendLine(line.ToString().Replace("]]>", "]]]]><![CDATA[>"));
                }
                if (lines.Length > commentLines)
                {
                    sourceBuilder.AppendLine($"/// [{lines.Length - commentLines} more lines ({lines.Length} total)] ");
                }

                sourceBuilder.AppendLine($$"""
                /// ]]>
                /// </code>
            """);
            }
            sourceBuilder.AppendLine($$"""
                /// </summary>
                public {{modifier}} string {{identifierName}} {{(isConst ? "=" : "=>")}} {{separator}}
            """);
            sourceBuilder.AppendLine(text);
            sourceBuilder.AppendLine($$"""
            {{separator}};
            }
            """);

            context.AddSource($"{@namespace}/{className}/{identifierName}.g.cs", sourceBuilder.ToString());
        });
    }
}
