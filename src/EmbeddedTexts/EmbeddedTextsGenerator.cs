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
                    CommentContentLines: uint.TryParse(itemOptions.GetAdditionalTextMetadata(EmbedTextCommentContentLinesMetadataProperty), out var commentLines) ? commentLines : null,
                    IsStaticClass: string.Equals(itemOptions.GetAdditionalTextMetadata(EmbedTextIsStaticClassMetadataProperty), "true", StringComparison.OrdinalIgnoreCase),
                    DirectoryAsClass: string.Equals(itemOptions.GetAdditionalTextMetadata(EmbedTextDirectoryAsClassMetadataProperty), "true", StringComparison.OrdinalIgnoreCase),
                    Enabled: (globalEnabled || itemEnabled) && !itemDisabled,
                    Text: text);
            });

        var enabledTexts = allTexts.Where(e => e.Enabled);

        context.RegisterSourceOutput(enabledTexts, static (context, item) =>
        {
            if (item.Text.GetText() is not { Lines: var lines } text)
                return;

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

            var separator = new string('"', 3);
            while (lines.Any(l => l.Text?.ToString().Contains(separator) == true))
                separator += '\"';

            var sourceBuilder = new StringBuilder(text.Length * 2 + lines.Count * 8 + 300);

            sourceBuilder.AppendLine($$"""
            // <auto-generated />

            namespace {{@namespace}};

            public {{classModifiers}} class {{className}}
            {
                /// <summary>
                /// Contents of the file at '{{relativeFilePath}}':
                /// <code>
            """);

            var commentLines = (int)item.CommentContentLines.GetValueOrDefault();

            foreach (var line in commentLines > 0 ? lines.Take(commentLines) : lines)
            {
                sourceBuilder.AppendLine($$"""
                /// {{line.ToString().Replace("<", "&lt;").Replace(">", "&gt;")}}
            """);
            }
            if (commentLines > 0 && lines.Count > commentLines)
            {
                sourceBuilder.AppendLine($"/// [{lines.Count - commentLines} more lines ({lines.Count} total)] ");
            }

            sourceBuilder.AppendLine($$"""
                /// </code>
                /// </summary>
                public {{modifier}} string {{identifierName}} {{(isConst ? "=" : "=>")}} {{separator}}
            {{text}}
            {{separator}};
            }
            """);

            context.AddSource($"{@namespace}/{className}/{identifierName}.g.cs", sourceBuilder.ToString());
        });
    }
}
