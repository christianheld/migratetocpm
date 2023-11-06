using MigrateToCpm;

using Spectre.Console;
using Spectre.Console.Cli;

var app = new CommandApp<MigrateToCpmCommand>();
app.Configure(c =>
{
    c.SetExceptionHandler(ex =>
    {
        AnsiConsole.WriteException(ex);
    });
});

await app.RunAsync(args).ConfigureAwait(false);
