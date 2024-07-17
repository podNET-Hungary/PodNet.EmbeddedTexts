# PodNet.EmbeddedTexts [![Nuget](https://img.shields.io/nuget/v/PodNet.EmbeddedTexts)](https://www.nuget.org/packages/PodNet.EmbeddedTexts/)
A simple C# incremental code generator that allows for efficiently embedding text file content into your code.

## Usage
1. Add the [`PodNet.EmbeddedTexts`](https://www.nuget.org/packages/PodNet.EmbeddedTexts/) NuGet package to your .NET project.
2. Add some text files (can be code files, can be markdown, plain text etc.) to your project you want the contents of to be available to you **at compile time**. Mark the files as having a build action of `AdditionalFile`. The most straightforward way to do this is by editing the `.csproj` project file and adding a pattern in an `<ItemGroup>`:
   ```csproj
   <ItemGroup>
     <AdditionalFiles Include="Files/**" />
   </ItemGroup>
   ```
   You can do the above for individual files as well if you so choose. In this case you can even use the Visual Studio Properties Window (<kbd>F4</kbd> by default, while a file is being selected in Solution Explorer) and set the `Build action` property of the file(s) to *"C# analyzer additional file"*. Once you do, Visual Studio will edit your `.csproj` file accordingly.
3. Once you set this up, you'll immediately find a `public static string Content { get; }` property generated on a class in a namespace according to the structure of your project, which returns the string content of the file. So, if your root namespace for the project is `MyProject`, and you have an `AdditionalFiles` element in your project's `Files/MyFile.txt` file with content `file contents`, you'll be able to call:
   ```csharp
   Console.WriteLine(Files.MyFile_txt.Content); // Writes: "file contents"
   ```

## Additional configuration

The generator is **on by default** for every `AdditionalFiles` text file you have in your project, and will include the file contents in your compilation. If this is not your desired use-case, you have a few options.

Additionally, you can configure the name of the generated namespace and class for every piece of content.

> [!NOTE]  
> It's imporant to mention that the below is standard configuration practice for MSBuild items. The MSBuild project files (like .csproj or Directory.build.props) define items in the children of `ItemGroup` elements. The `AdditionalFiles` element can be used by all modern source generators and are not specific to `PodNet.EmbeddedTexts`. One quirk of MSBuild items is that they can be `Include`d individually or by globbing patterns, but the execution and parsing of these files is (mostly) sequential, so if you have any files `Include`d by any patterns, you then have to `Update` them instead of `Include`-ing again.


### Make the generator opt-in

Set the MSBuild property `PodNetAutoEmbedAdditionalTexts` to `false` to disable automatic generation for all `AdditionalFiles`. Then, for each file you wish to embed in the compilation, add a `PodNet_EmbedText="true"` attribute its `AdditionalFiles` item.

```csproj
<Project>
  <!-- Additional properties, items and targets omitted -->
  <PropertyGroup>
    <!-- Setting this to false makes the default behavior opt-in for the generator. -->
    <PodNetAutoEmbedAdditionalTexts>false</PodNetAutoEmbedAdditionalTexts>
  </PropertyGroup>
  <ItemGroup>
    <!-- Because automatic embedding is disabled, these files won't be embedded in the source... -->
    <AdditionalFiles Include="Files/**" />

    <!-- ...unless you opt-in to them being embedded. -->
    <AdditionalFiles Update="Files/Embed/**" PodNet_EmbedText="true" />

    <!-- But you can still opt-out individually. -->
    <AdditionalFiles Update="Files/Embed/.gitkeep" PodNet_EmbedText="false" />
  </ItemGroup>
</Project>
```

This is useful if you have any source generators enabled that work on text files you wish to not include in the compilation.

> [!WARNING]
> Remember that including all text files in the compilation will essentially make your assemblies/executables larger by about the size of the file. The generated `static` class and property won't be loaded into memory until first being referenced, but this can incur a performance hit when embedding larger files.

### Opt-out of generation for files or folders

Whether you have the generator automatically generating or not, you can explicitly opt-out of generation by setting `PodNet_EmbedText="false"`.

```csproj
<Project>
  <!-- Additional properties, items and targets omitted -->
  <ItemGroup>
    <!-- You can opt-out of generation for any pattern or individual file. -->
    <AdditionalFiles Include="Files/NoEmbed/**" PodNet_EmbedText="false" />
  </ItemGroup>
</Project>
```

### Configuring the generated class name and namespace

You can set the `PodNet_EmbedTextNamespace`, `PodNet_EmbedTextClassName`, `PodNet_EmbeddedTextIsConst` and `PodNet_EmbeddedTextIdentifier` properties (attributes) on the items to override the default namespace, class and identifier name, as well as to generate a const instead of a property.

```csproj
<Project>
  <!-- Additional properties, items and targets omitted -->
  <ItemGroup>
    <!-- The defaults would be:
         - Namespace: "MyProject.Files", generated from the directory structure and the project root namespace,
         - ClassName: "My_File_txt", generated from sanitizing the file name,
         - IsConst: unless set to true, the generated member is a property that returns the constant value by expression body,
         - Identifier: defaults to "Content". -->
    <AdditionalFiles Include="Files/My File.txt" 
                     PodNet_EmbedTextNamespace="OtherNamespace" 
                     PodNet_EmbedTextClassName="MyFileTXT"
                     PodNet_EmbedTextIsConst="true"
                     PodNet_EmbedTextIdentifier="Text" />
  </ItemGroup>
</Project>
```

The above results in: `OtherNamespace.MyFileTXT.Content` being the property that holds the file content.

### Advanced parameterization

Don't be shy to use MSBuild properties, well-known metadata and such to configure the generator.

```
<AdditionalFiles Include="@(Compile)" PodNet_EmbedTextNamespace="$(RootNamespace).CompiledFiles" PodNet_EmbedTextClassName="%(Filename)" />
```

The above includes all `.cs` files (and other files that are at that point included in the compilation) into the source itself, in the `MyProject.CompiledFiles` namespace, with the class name being that of the filename without the extension.


## Contributing and Support

This project is intended to be widely usable, but no warranties are provided. If you want to contact us, feel free to do so in the repo's [[Discussions](https://github.com/podNET-Hungary/PodNet.EnumValues/discussions)], at our website at [podnet.hu](https://podnet.hu), or find us anywhere from [LinkedIn](https://www.linkedin.com/company/podnet-hungary/) and [Patreon](https://www.patreon.com/podNETHungary) to [Meetup](https://www.meetup.com/budapest-net-meetup/), [YouTube](https://www.youtube.com/@podNET) or [X](https://twitter.com/podNET_Hungary).

Any kinds of contributions from issues to PRs and open discussions are welcome!

Don't forget to give us a ⭐ if you like this repo (it's free to give kudos!) or share it on socials, but we're not averse to offering you some benefits at our [🍻 Patreon 🍻](https://www.patreon.com/podNETHungary) either, if you're so inclined!