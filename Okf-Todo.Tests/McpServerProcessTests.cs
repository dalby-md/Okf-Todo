using System.Diagnostics;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Okf_Todo.Tests;

public sealed class McpServerProcessTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task McpServer_ListsToolsAndCreatesTaskWithTimelineEntry()
    {
        var testDirectory = Path.Combine(Path.GetTempPath(), "Okf-Todo.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testDirectory);
        var databasePath = Path.Combine(testDirectory, "mcp.db");
        Process? process = null;

        try
        {
            var serverPath = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "..",
                "..",
                "..",
                "Okf-Todo.Mcp",
                "bin",
#if DEBUG
                "Debug",
#else
                "Release",
#endif
                "net8.0",
                "Okf-Todo.Mcp.dll"));
            Assert.True(File.Exists(serverPath), $"MCP server assembly was not found at {serverPath}.");

            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                WorkingDirectory = AppContext.BaseDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.ArgumentList.Add(serverPath);
            startInfo.ArgumentList.Add("--database-path");
            startInfo.ArgumentList.Add(databasePath);

            process = new Process { StartInfo = startInfo };
            Assert.True(process.Start());
            var standardErrorTask = process.StandardError.ReadToEndAsync();

            await SendAsync(process, new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "initialize",
                @params = new
                {
                    protocolVersion = "2025-06-18",
                    capabilities = new { },
                    clientInfo = new { name = "Okf-Todo.Tests", version = "1.0" }
                }
            });
            using var initialize = await ReadResponseAsync(process, 1);
            Assert.Equal("2.0", initialize.RootElement.GetProperty("jsonrpc").GetString());

            await SendAsync(process, new
            {
                jsonrpc = "2.0",
                method = "notifications/initialized"
            });
            await SendAsync(process, new
            {
                jsonrpc = "2.0",
                id = 2,
                method = "tools/list"
            });

            using var toolsResponse = await ReadResponseAsync(process, 2);
            var toolNames = toolsResponse.RootElement
                .GetProperty("result")
                .GetProperty("tools")
                .EnumerateArray()
                .Select(tool => tool.GetProperty("name").GetString()!)
                .ToHashSet(StringComparer.Ordinal);
            Assert.Equal(
                new HashSet<string>(StringComparer.Ordinal)
                {
                    "task_list",
                    "task_get",
                    "task_create",
                    "task_update",
                    "task_get_timeline"
                },
                toolNames);

            await SendAsync(process, new
            {
                jsonrpc = "2.0",
                id = 3,
                method = "tools/call",
                @params = new
                {
                    name = "task_create",
                    arguments = new
                    {
                        title = "Created through MCP",
                        taskTypeCode = "REQUEST",
                        taskPriorityCode = "NORMAL",
                        tags = new[] { "mcp" }
                    }
                }
            });
            using var createResponse = await ReadResponseAsync(process, 3);
            var createResult = createResponse.RootElement.GetProperty("result");
            Assert.False(
                createResult.TryGetProperty("isError", out var isError)
                && isError.GetBoolean());
            var taskId = ReadTextContent(createResponse).GetProperty("id").GetInt32();

            await SendAsync(process, new
            {
                jsonrpc = "2.0",
                id = 4,
                method = "tools/call",
                @params = new
                {
                    name = "task_get_timeline",
                    arguments = new { taskId }
                }
            });
            using var timelineResponse = await ReadResponseAsync(process, 4);
            var timeline = ReadTextContent(timelineResponse);
            Assert.Contains(
                timeline.EnumerateArray(),
                entry => entry.GetProperty("logTypeCode").GetString() == "TASK_CREATED");

            process.StandardInput.Close();
            using var exitTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await process.WaitForExitAsync(exitTimeout.Token);
            Assert.Equal(0, process.ExitCode);
            Assert.Contains("OKF-Todo MCP server is using database", await standardErrorTask);
        }
        finally
        {
            if (process is { HasExited: false })
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }

            process?.Dispose();
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(testDirectory))
            {
                Directory.Delete(testDirectory, recursive: true);
            }
        }
    }

    private static async Task SendAsync(Process process, object message)
    {
        await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(message, JsonOptions));
        await process.StandardInput.FlushAsync();
    }

    private static async Task<JsonDocument> ReadResponseAsync(Process process, int id)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (true)
        {
            var line = await process.StandardOutput.ReadLineAsync(timeout.Token);
            if (line is null)
            {
                throw new InvalidOperationException($"MCP server stdout closed before response {id}.");
            }

            var document = JsonDocument.Parse(line);
            if (document.RootElement.TryGetProperty("id", out var responseId)
                && responseId.ValueKind == JsonValueKind.Number
                && responseId.GetInt32() == id)
            {
                return document;
            }

            document.Dispose();
        }
    }

    private static JsonElement ReadTextContent(JsonDocument response)
    {
        var text = response.RootElement
            .GetProperty("result")
            .GetProperty("content")[0]
            .GetProperty("text")
            .GetString();
        Assert.False(string.IsNullOrWhiteSpace(text));
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }
}
