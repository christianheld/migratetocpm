using MigrateToCpm;

using Spectre.Console;
using Spectre.Console.Cli;

var app = new CommandApp<MigrateToCpmCommand>();
app.Configure(c =>
{
    c.SetExceptionHandler((ex, _) =>
    {
        AnsiConsole.WriteException(ex);
    });
});

await app.RunAsync(args).ConfigureAwait(false);
