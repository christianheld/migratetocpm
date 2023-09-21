using Spectre.Console.Cli;

namespace MigrateToCpm;

public class MigrateToCpmSettings : CommandSettings
{
    [CommandArgument(0, "[ProjectDirectory]")]
    public string ProjectDirectory { get; set; } = Environment.CurrentDirectory;
}
