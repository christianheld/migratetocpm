﻿using System.Xml;
using System.Xml.Linq;
using Spectre.Console;
using Spectre.Console.Cli;

namespace MigrateToCpm;

public class MigrateToCpmCommand : AsyncCommand<MigrateToCpmSettings>
{
    private static readonly XmlWriterSettings _xmlWriterSettings = new()
    {
        OmitXmlDeclaration = true,
        Async = true,
        Indent = true,
        IndentChars = "  "
    };

    public override async Task<int> ExecuteAsync(CommandContext context, MigrateToCpmSettings settings)
    {
        var directoryInfo = new DirectoryInfo(settings.ProjectDirectory);

        var projectFiles = AnsiConsole.Status()
            .Start("Loading .csproj files.", ctx =>
            {
                return GetCsProjFiles(directoryInfo);
            });

        var packages = await AnsiConsole.Progress()
            .HideCompleted(true)
            .StartAsync(async ctx =>
            {
                var packageVersions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                var task = ctx.AddTask("Collect packages", maxValue: projectFiles.Count - 1);

                foreach (var projectFile in projectFiles)
                {
                    task.Description = projectFile.Name;
                    await UpdateCsProjFileAsync(packageVersions, projectFile);
                    task.Increment(1);
                }

                return packageVersions;
            });

        var table = new Table();
        table.AddColumn("Package");
        table.AddColumn("Version");

        foreach (var (package, version) in packages.OrderBy(x => x.Key))
        {
            table.AddRow(package, Markup.Escape(version));
        }

        AnsiConsole.Write(table);

        await CreateDirectoryPackagesFileAsync(directoryInfo, packages);

        return 0;
    }

    private static async Task CreateDirectoryPackagesFileAsync(DirectoryInfo directoryInfo, Dictionary<string, string> packages)
    {
        var packagesProps = new XDocument();
        packagesProps.Add(
            new XElement(
                "Project",
                new XElement(
                    "PropertyGroup",
                    new XElement("ManagePackageVersionsCentrally", true),
                    new XElement("CentralPackageTransitivePinningEnabled", true)),
                new XElement(
                    "ItemGroup",
                    packages
                        .OrderBy(kvp => kvp.Key)
                        .Select(kvp => new XElement(
                            "PackageVersion",
                            new XAttribute("Include", kvp.Key),
                            new XAttribute("Version", kvp.Value))))));

        var fileName = Path.Combine(directoryInfo.FullName, "Directory.Packages.props");
        using (var fs = File.Create(fileName))
        {
            using var writer = XmlWriter.Create(fs, _xmlWriterSettings);
            await packagesProps.SaveAsync(writer, CancellationToken.None);
        }
    }

    private static List<FileInfo> GetCsProjFiles(DirectoryInfo directoryInfo)
    {
        return directoryInfo.EnumerateFiles(
                            "*.csproj",
                            new EnumerationOptions
                            {
                                RecurseSubdirectories = true,
                                IgnoreInaccessible = true,
                            })
                            .ToList();
    }

    private static async Task UpdateCsProjFileAsync(Dictionary<string, string> packageVersions, FileInfo projectFile)
    {
        XDocument document;
        using (var fs = File.OpenRead(projectFile.FullName))
        {
            document = await XDocument.LoadAsync(fs, LoadOptions.None, CancellationToken.None);
        }

        var nodesToRemove = new List<XElement>();

        var packageReferences = document.Root!.Descendants("PackageReference");
        foreach (var packageReference in packageReferences)
        {
            var version = packageReference.Attribute("Version")!.Value;
            var packageAttribute = packageReference.Attribute("Include");

            if (packageAttribute is null)
            {
                packageAttribute = packageReference.Attribute("Update")!;
                AnsiConsole.MarkupLine($"[yellow]{projectFile.Name}: Remove `Update` reference to {packageAttribute.Value}[/]");
                nodesToRemove.Add(packageReference);
            }

            var package = packageAttribute!.Value;

            if (packageVersions.TryGetValue(package, out var existingVersion))
            {
                try
                {
                    version = GetMaxVersion(version, existingVersion);
                }
                catch (FormatException ex)
                {
                    AnsiConsole.MarkupLineInterpolated($"[yellow]Could not parse version in `{package}`: {ex.Message}[/]");
                }
            }

            packageVersions[package] = version;

            packageReference.Attribute("Version")!.Remove();
        }

        nodesToRemove.ForEach(node => node.Remove());

        using (var fs = File.Create(projectFile.FullName))
        {
            using var writer = XmlWriter.Create(fs, _xmlWriterSettings);
            await document.SaveAsync(writer, CancellationToken.None);
            await writer.FlushAsync();
        }
    }

    private static string GetMaxVersion(string left, string right)
    {
        var leftVersion = Version.Parse(left.Split("-", 2)[0]);
        var rightVersion = Version.Parse(right.Split("-", 2)[0]);

        return leftVersion > rightVersion
            ? left
            : right;
    }
}
