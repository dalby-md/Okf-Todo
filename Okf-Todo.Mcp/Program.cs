using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using ModelContextProtocol.Server;
using Photino.Okf_Todo.Data;
using Photino.Okf_Todo.Services;

var databasePath = GetDatabasePath(args);
Directory.CreateDirectory(Path.GetDirectoryName(databasePath)
    ?? throw new InvalidOperationException("The database path has no parent directory."));

// Lookup seeding resolves lookup-seed.json relative to the process working directory.
Directory.SetCurrentDirectory(AppContext.BaseDirectory);

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddConsole(options =>
{
    // MCP owns stdout. Application and framework logs must remain on stderr.
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(DatabasePathProvider.CreateConnectionString(databasePath)));
builder.Services.AddScoped<LookupSeedService>();
builder.Services.AddScoped<TaskLifecycleService>();
builder.Services.AddScoped<TaskService>();
builder.Services.AddSingleton<ApplicationCommandService>();
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

using var host = builder.Build();

await using (var scope = host.Services.CreateAsyncScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await dbContext.Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<LookupSeedService>().SeedAsync();
}

host.Services.GetRequiredService<ILoggerFactory>()
    .CreateLogger("OkfTodoMcp")
    .LogInformation("OKF-Todo MCP server is using database {DatabasePath}.", databasePath);

await host.RunAsync();

static string GetDatabasePath(string[] arguments)
{
    const string option = "--database-path";
    var optionIndex = Array.FindIndex(arguments, argument =>
        string.Equals(argument, option, StringComparison.OrdinalIgnoreCase));

    if (optionIndex < 0)
    {
        return DatabasePathProvider.GetDatabasePath();
    }

    if (optionIndex == arguments.Length - 1 || string.IsNullOrWhiteSpace(arguments[optionIndex + 1]))
    {
        throw new ArgumentException($"{option} requires a path.");
    }

    return Path.GetFullPath(arguments[optionIndex + 1]);
}
