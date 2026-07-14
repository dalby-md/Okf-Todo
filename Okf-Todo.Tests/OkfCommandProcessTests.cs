using System.Diagnostics;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Photino.Okf_Todo.Data;
using Photino.Okf_Todo.Services;

namespace Okf_Todo.Tests;

public sealed class OkfCommandProcessTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task OkfCommandMode_WritesJsonToStdoutLogsToStderrAndBypassesDesktopMutex()
    {
        var testDirectory = CreateTestDirectory();
        var databasePath = Path.Combine(testDirectory, "okf-command.db");

        try
        {
            using var mutex = new NamedMutexLease("OkfTodoSingleInstance");
            Assert.True(mutex.Acquired);

            var request = JsonSerializer.Serialize(new
            {
                messageId = "process-create",
                type = "task.create",
                payload = new
                {
                    title = "Created through process command",
                    taskTypeCode = "REQUEST",
                    body = "",
                    bodyFormatCode = "HTML",
                    taskPriorityCode = "NORMAL",
                    taskSourceCode = (string?)null,
                    sourceReference = (string?)null,
                    sourceUrl = (string?)null,
                    deadline = (DateTime?)null
                }
            }, JsonOptions);

            var result = await RunCommandAsync(databasePath, request);

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("Executing command from the OKF command adapter", result.StandardError);

            using var response = JsonDocument.Parse(result.StandardOutput);
            Assert.True(response.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal("process-create", response.RootElement.GetProperty("messageId").GetString());
            var taskId = response.RootElement.GetProperty("payload").GetProperty("id").GetInt32();

            await using var connection = new SqliteConnection(
                DatabasePathProvider.CreateConnectionString(databasePath, pooling: false));
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT COUNT(*)
                FROM TaskLogEntries AS log
                INNER JOIN TaskLogTypes AS type ON type.Id = log.TaskLogTypeId
                WHERE log.TaskId = $taskId AND type.Code = $logTypeCode;
                """;
            command.Parameters.AddWithValue("$taskId", taskId);
            command.Parameters.AddWithValue("$logTypeCode", TaskLogTypeCodes.TaskCreated);

            Assert.Equal(1L, Convert.ToInt64(await command.ExecuteScalarAsync()));
        }
        finally
        {
            DeleteTestDirectory(testDirectory);
        }
    }

    [Fact]
    public async Task OkfCommandMode_ReturnsOneForApplicationErrors()
    {
        var testDirectory = CreateTestDirectory();
        var databasePath = Path.Combine(testDirectory, "okf-command-error.db");

        try
        {
            var request = JsonSerializer.Serialize(new
            {
                messageId = "process-invalid",
                type = "task.create",
                payload = new
                {
                    title = "",
                    taskTypeCode = "REQUEST",
                    body = "",
                    bodyFormatCode = "HTML",
                    taskPriorityCode = "NORMAL",
                    taskSourceCode = (string?)null,
                    sourceReference = (string?)null,
                    sourceUrl = (string?)null,
                    deadline = (DateTime?)null
                }
            }, JsonOptions);

            var result = await RunCommandAsync(databasePath, request);

            Assert.Equal(1, result.ExitCode);
            using var response = JsonDocument.Parse(result.StandardOutput);
            Assert.False(response.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal(
                "ValidationFailed",
                response.RootElement.GetProperty("error").GetProperty("code").GetString());
        }
        finally
        {
            DeleteTestDirectory(testDirectory);
        }
    }

    [Fact]
    public async Task OkfCommandMode_ReturnsTwoWhenStandardInputIsEmpty()
    {
        var testDirectory = CreateTestDirectory();
        var databasePath = Path.Combine(testDirectory, "okf-command-empty.db");

        try
        {
            var result = await RunCommandAsync(databasePath, string.Empty);

            Assert.Equal(2, result.ExitCode);
            Assert.True(string.IsNullOrWhiteSpace(result.StandardOutput));
            Assert.Contains("requires one JSON command on standard input", result.StandardError);
        }
        finally
        {
            DeleteTestDirectory(testDirectory);
        }
    }

    private static async Task<CommandProcessResult> RunCommandAsync(string databasePath, string standardInput)
    {
        var applicationPath = Path.Combine(AppContext.BaseDirectory, "Okf-Todo.dll");
        Assert.True(File.Exists(applicationPath), $"Application assembly was not found at {applicationPath}.");

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
        startInfo.ArgumentList.Add(applicationPath);
        startInfo.ArgumentList.Add("--okf-command");
        startInfo.ArgumentList.Add("--okf-database-path");
        startInfo.ArgumentList.Add(databasePath);

        using var process = new Process { StartInfo = startInfo };
        Assert.True(process.Start());

        var standardOutputTask = process.StandardOutput.ReadToEndAsync();
        var standardErrorTask = process.StandardError.ReadToEndAsync();
        await process.StandardInput.WriteAsync(standardInput);
        process.StandardInput.Close();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("The OKF command process did not exit within 30 seconds.");
        }

        return new CommandProcessResult(
            process.ExitCode,
            await standardOutputTask,
            await standardErrorTask);
    }

    private static string CreateTestDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "Okf-Todo.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTestDirectory(string path)
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private sealed class NamedMutexLease : IDisposable
    {
        private readonly ManualResetEventSlim release = new(false);
        private readonly Thread ownerThread;

        public NamedMutexLease(string name)
        {
            using var ready = new ManualResetEventSlim(false);
            ownerThread = new Thread(() =>
            {
                using var mutex = new Mutex(false, name);
                Acquired = mutex.WaitOne(TimeSpan.FromSeconds(5));
                ready.Set();

                if (!Acquired)
                {
                    return;
                }

                release.Wait();
                mutex.ReleaseMutex();
            })
            {
                IsBackground = true,
                Name = "OKF command test mutex owner"
            };
            ownerThread.Start();
            ready.Wait();
        }

        public bool Acquired { get; private set; }

        public void Dispose()
        {
            release.Set();
            ownerThread.Join(TimeSpan.FromSeconds(5));
            release.Dispose();
        }
    }

    private sealed record CommandProcessResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);
}
