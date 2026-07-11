using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Photino.Okf_Todo.Data;
using Photino.Okf_Todo.Services;

namespace Okf_Todo.Tests;

public sealed class TaskLifecycleServiceTests
{
    [Fact]
    public async Task SchemaMaintenance_DropsStaleWaitingForTypesArtifacts()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        var loggerFactory = LoggerFactory.Create(_ => { });

        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={databasePath};Pooling=False")
                .Options;

            await using (var dbContext = new AppDbContext(options))
            {
                await dbContext.Database.EnsureCreatedAsync();
                await dbContext.Database.ExecuteSqlRawAsync("ALTER TABLE \"TaskWaitingFors\" ADD COLUMN \"WaitingForTypeId\" INTEGER NULL");
                await dbContext.Database.ExecuteSqlRawAsync("CREATE INDEX \"IX_TaskWaitingFors_WaitingForTypeId\" ON \"TaskWaitingFors\" (\"WaitingForTypeId\")");
                await dbContext.Database.ExecuteSqlRawAsync("""
                    CREATE TABLE "WaitingForTypes" (
                        "Id" INTEGER NOT NULL CONSTRAINT "PK_WaitingForTypes" PRIMARY KEY AUTOINCREMENT,
                        "Code" TEXT NOT NULL,
                        "Name" TEXT NOT NULL
                    )
                    """);
            }

            SqliteSchemaMaintenance.CleanupCurrentDatabase(
                databasePath,
                loggerFactory.CreateLogger(nameof(SqliteSchemaMaintenance)));

            await using (var dbContext = new AppDbContext(options))
            {
                var tableCount = await dbContext.Database
                    .SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM sqlite_master WHERE type = 'table' AND name = 'WaitingForTypes'")
                    .SingleAsync();
                var waitingForTypeColumnCount = await dbContext.Database
                    .SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM pragma_table_info('TaskWaitingFors') WHERE name = 'WaitingForTypeId'")
                    .SingleAsync();

                Assert.Equal(0, tableCount);
                Assert.Equal(0, waitingForTypeColumnCount);
            }
        }
        finally
        {
            loggerFactory.Dispose();
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task LookupSeeding_InsertsValuesOnlyWhenTablesAreEmpty()
    {
        await using var database = await TestDatabase.CreateAsync();

        var taskTypeCount = await database.DbContext.TaskTypes.CountAsync();
        var statusCount = await database.DbContext.TaskStatuses.CountAsync();
        var tagCount = await database.DbContext.TaskTags.CountAsync();
        var selectedTaskType = await database.DbContext.TaskTypes.SingleAsync(type => type.IsSelected);
        var selectedTaskPriority = await database.DbContext.TaskPriorities.SingleAsync(priority => priority.IsSelected);

        Assert.True(taskTypeCount > 0);
        Assert.True(statusCount > 0);
        Assert.True(tagCount > 0);
        Assert.Equal("REQUEST", selectedTaskType.Code);
        Assert.Equal("NORMAL", selectedTaskPriority.Code);

        var activeStatus = await database.DbContext.TaskStatuses.SingleAsync(status => status.Code == TaskStatusCodes.Active);
        activeStatus.Name = "Renamed Active";
        await database.DbContext.SaveChangesAsync();

        await database.SeedAsync();

        Assert.Equal(taskTypeCount, await database.DbContext.TaskTypes.CountAsync());
        Assert.Equal(statusCount, await database.DbContext.TaskStatuses.CountAsync());
        Assert.Equal(tagCount, await database.DbContext.TaskTags.CountAsync());

        var unchangedStatus = await database.DbContext.TaskStatuses.SingleAsync(status => status.Code == TaskStatusCodes.Active);
        Assert.Equal("Renamed Active", unchangedStatus.Name);
    }

    [Fact]
    public async Task SelectableLookups_ClearPreviousSelectedRowWhenAnotherRowIsSelected()
    {
        await using var database = await TestDatabase.CreateAsync();

        var errorType = await database.DbContext.TaskTypes.SingleAsync(type => type.Code == "ERROR");
        errorType.IsSelected = true;
        await database.DbContext.SaveChangesAsync();

        database.DbContext.ChangeTracker.Clear();

        var selectedTaskTypes = await database.DbContext.TaskTypes
            .Where(type => type.IsSelected)
            .Select(type => type.Code)
            .ToListAsync();

        Assert.Equal(["ERROR"], selectedTaskTypes);

        var urgentPriority = await database.DbContext.TaskPriorities.SingleAsync(priority => priority.Code == "URGENT");
        urgentPriority.IsSelected = true;
        await database.DbContext.SaveChangesAsync();

        database.DbContext.ChangeTracker.Clear();

        var selectedTaskPriorities = await database.DbContext.TaskPriorities
            .Where(priority => priority.IsSelected)
            .Select(priority => priority.Code)
            .ToListAsync();

        Assert.Equal(["URGENT"], selectedTaskPriorities);

        var emailSource = await database.DbContext.TaskSources.SingleAsync(source => source.Code == "EMAIL");
        emailSource.IsSelected = true;
        await database.DbContext.SaveChangesAsync();

        var teamsSource = await database.DbContext.TaskSources.SingleAsync(source => source.Code == "TEAMS");
        teamsSource.IsSelected = true;
        await database.DbContext.SaveChangesAsync();

        database.DbContext.ChangeTracker.Clear();

        var selectedTaskSources = await database.DbContext.TaskSources
            .Where(source => source.IsSelected)
            .Select(source => source.Code)
            .ToListAsync();

        Assert.Equal(["TEAMS"], selectedTaskSources);
    }

    [Fact]
    public async Task CreateTask_CreatesActiveTaskAndTaskCreatedLog()
    {
        await using var database = await TestDatabase.CreateAsync();

        var task = await database.Lifecycle.CreateTaskAsync(new TaskCreateRequest(
            Title: "Fix failed deployment",
            TaskTypeCode: "ERROR",
            BodyFormatCode: "HTML",
            TaskPriorityCode: "NORMAL"));

        var savedTask = await database.LoadTaskAsync(task.Id);

        Assert.Equal(TaskStatusCodes.Active, savedTask.TaskStatus?.Code);
        Assert.NotNull(savedTask.ActivatedAt);
        Assert.NotEqual(default, savedTask.CreatedAt);
        Assert.NotEqual(default, savedTask.UpdatedAt);
        AssertHasLog(savedTask, TaskLogTypeCodes.TaskCreated, "Task created");
    }

    [Fact]
    public async Task StartTask_LeavesActiveTaskActive()
    {
        await using var database = await TestDatabase.CreateAsync();
        var task = await database.CreateTaskAsync();

        await database.Lifecycle.StartTaskAsync(task.Id);

        var savedTask = await database.LoadTaskAsync(task.Id);

        Assert.Equal(TaskStatusCodes.Active, savedTask.TaskStatus?.Code);
        Assert.NotNull(savedTask.ActivatedAt);
    }

    [Fact]
    public async Task UndoStartTask_IsRejectedBecauseTasksAreActiveByDefault()
    {
        await using var database = await TestDatabase.CreateAsync();
        var task = await database.CreateTaskAsync();

        var exception = await Assert.ThrowsAsync<ValidationException>(() =>
            database.Lifecycle.UndoStartTaskAsync(task.Id));

        Assert.Equal("taskStatus", exception.Field);
    }

    [Fact]
    public async Task AddWaitingFor_CreatesActiveWaitTargetKeepsTaskActiveAndLogs()
    {
        await using var database = await TestDatabase.CreateAsync();
        var task = await database.CreateTaskAsync();

        await database.Lifecycle.AddWaitingForAsync(
            task.Id,
            new TaskWaitingForRequest("INC123456"));

        var savedTask = await database.LoadTaskAsync(task.Id);
        var activeTargets = await database.DbContext.TaskWaitingFors
            .Where(waitingFor => waitingFor.TaskId == task.Id && waitingFor.ResolvedAt == null)
            .ToListAsync();

        Assert.Equal(TaskStatusCodes.Active, savedTask.TaskStatus?.Code);
        Assert.NotNull(savedTask.WaitingSince);
        Assert.Single(activeTargets);
        AssertHasLog(savedTask, TaskLogTypeCodes.WaitingForChanged, "Waiting for changed to INC123456");
    }

    [Fact]
    public async Task AddWaitingFor_RejectsTaskThatIsNotActive()
    {
        await using var database = await TestDatabase.CreateAsync();
        var task = await database.CreateTaskAsync();
        await database.Lifecycle.CompleteTaskAsync(task.Id);

        var exception = await Assert.ThrowsAsync<ValidationException>(() => database.Lifecycle.AddWaitingForAsync(
            task.Id,
            new TaskWaitingForRequest("INC123456")));

        Assert.Equal("taskStatus", exception.Field);
    }

    [Fact]
    public async Task ClearWaitingFor_ResolvesWaitTargetSetsActiveClearsWaitingSinceAndLogs()
    {
        await using var database = await TestDatabase.CreateAsync();
        var task = await database.CreateWaitingTaskAsync();

        await database.Lifecycle.ClearWaitingForAsync(task.Id);

        var savedTask = await database.LoadTaskAsync(task.Id);
        var target = await database.DbContext.TaskWaitingFors.SingleAsync(waitingFor => waitingFor.TaskId == task.Id);

        Assert.Equal(TaskStatusCodes.Active, savedTask.TaskStatus?.Code);
        Assert.Null(savedTask.WaitingSince);
        Assert.NotNull(target.ResolvedAt);
        AssertHasLog(savedTask, TaskLogTypeCodes.WaitingForCleared, "Waiting for INC123456 was cleared");
    }

    [Fact]
    public async Task CompleteReopenCancel_UpdateTimestampsAndLogs()
    {
        await using var database = await TestDatabase.CreateAsync();
        var task = await database.CreateTaskAsync();

        await database.Lifecycle.CompleteTaskAsync(task.Id);
        var completedTask = await database.LoadTaskAsync(task.Id);
        Assert.Equal(TaskStatusCodes.Completed, completedTask.TaskStatus?.Code);
        Assert.NotNull(completedTask.CompletedAt);
        AssertHasLog(completedTask, TaskLogTypeCodes.TaskCompleted, "Task completed");

        await database.Lifecycle.ReopenTaskAsync(task.Id);
        var reopenedTask = await database.LoadTaskAsync(task.Id);
        Assert.Equal(TaskStatusCodes.Active, reopenedTask.TaskStatus?.Code);
        Assert.Null(reopenedTask.CompletedAt);
        Assert.Null(reopenedTask.CancelledAt);
        AssertHasLog(reopenedTask, TaskLogTypeCodes.TaskReopened, "Task reopened");

        await database.Lifecycle.CancelTaskAsync(task.Id);
        var cancelledTask = await database.LoadTaskAsync(task.Id);
        Assert.Equal(TaskStatusCodes.Cancelled, cancelledTask.TaskStatus?.Code);
        Assert.NotNull(cancelledTask.CancelledAt);
        Assert.Null(cancelledTask.CompletedAt);
        AssertHasLog(cancelledTask, TaskLogTypeCodes.TaskCancelled, "Task cancelled");
    }

    [Fact]
    public async Task AddWaitingFor_UpdatesExistingActiveWaitingTarget()
    {
        await using var database = await TestDatabase.CreateAsync();
        var task = await database.CreateWaitingTaskAsync();

        await database.Lifecycle.AddWaitingForAsync(
            task.Id,
            new TaskWaitingForRequest("Anna"));

        var activeTargets = await database.DbContext.TaskWaitingFors
            .Where(waitingFor => waitingFor.TaskId == task.Id && waitingFor.ResolvedAt == null)
            .ToListAsync();
        var activeTarget = Assert.Single(activeTargets);

        Assert.Equal("Anna", activeTarget.Label);
    }

    [Fact]
    public async Task CompleteTask_WithActiveWaitingTargetClearsWaitingState()
    {
        await using var database = await TestDatabase.CreateAsync();
        var task = await database.CreateWaitingTaskAsync();

        await database.Lifecycle.CompleteTaskAsync(task.Id);

        var savedTask = await database.LoadTaskAsync(task.Id);
        var target = await database.DbContext.TaskWaitingFors.SingleAsync(waitingFor => waitingFor.TaskId == task.Id);

        Assert.Equal(TaskStatusCodes.Completed, savedTask.TaskStatus?.Code);
        Assert.Null(savedTask.WaitingSince);
        Assert.NotNull(target.ResolvedAt);
        AssertHasLog(savedTask, TaskLogTypeCodes.WaitingForCleared, "Waiting for INC123456 was cleared");
        AssertHasLog(savedTask, TaskLogTypeCodes.TaskCompleted, "Task completed");
    }

    [Fact]
    public async Task CancelTask_WithActiveWaitingTargetClearsWaitingStateAndLogs()
    {
        await using var database = await TestDatabase.CreateAsync();
        var task = await database.CreateWaitingTaskAsync();

        await database.Lifecycle.CancelTaskAsync(task.Id);

        var savedTask = await database.LoadTaskAsync(task.Id);
        var target = await database.DbContext.TaskWaitingFors.SingleAsync(waitingFor => waitingFor.TaskId == task.Id);

        Assert.Equal(TaskStatusCodes.Cancelled, savedTask.TaskStatus?.Code);
        Assert.Null(savedTask.WaitingSince);
        Assert.NotNull(target.ResolvedAt);
        AssertHasLog(savedTask, TaskLogTypeCodes.WaitingForCleared, "Waiting for INC123456 was cleared");
        AssertHasLog(savedTask, TaskLogTypeCodes.TaskCancelled, "Task cancelled");
    }

    private static void AssertHasLog(TaskItem task, string code, string message)
    {
        Assert.Contains(task.LogEntries, log => log.TaskLogType?.Code == code && log.Message == message);
    }
}

internal sealed class TestDatabase : IAsyncDisposable
{
    private readonly SqliteConnection connection;
    private readonly ILoggerFactory loggerFactory;

    private TestDatabase(SqliteConnection connection, AppDbContext dbContext, ILoggerFactory loggerFactory)
    {
        this.connection = connection;
        this.loggerFactory = loggerFactory;
        DbContext = dbContext;
        Lifecycle = new TaskLifecycleService(DbContext, loggerFactory.CreateLogger<TaskLifecycleService>());
        Tasks = new TaskService(DbContext, Lifecycle);
        Images = new ImageService(DbContext, loggerFactory.CreateLogger<ImageService>());
    }

    public AppDbContext DbContext { get; }

    public TaskLifecycleService Lifecycle { get; }

    public TaskService Tasks { get; }

    public ImageService Images { get; }

    public static async Task<TestDatabase> CreateAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        var loggerFactory = LoggerFactory.Create(_ => { });
        var dbContext = new AppDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        var database = new TestDatabase(connection, dbContext, loggerFactory);
        await database.SeedAsync();

        return database;
    }

    public async Task SeedAsync()
    {
        var seeder = new LookupSeedService(DbContext, loggerFactory.CreateLogger<LookupSeedService>());
        await seeder.SeedAsync();
    }

    public async Task<TaskItem> CreateTaskAsync()
    {
        return await Lifecycle.CreateTaskAsync(new TaskCreateRequest(
            Title: "Fix failed deployment",
            TaskTypeCode: "ERROR",
            BodyFormatCode: "HTML",
            TaskPriorityCode: "NORMAL"));
    }

    public async Task<TaskItem> CreateWaitingTaskAsync()
    {
        var task = await CreateTaskAsync();
        await Lifecycle.AddWaitingForAsync(
            task.Id,
            new TaskWaitingForRequest("INC123456"));

        return task;
    }

    public async Task<TaskItem> LoadTaskAsync(int id)
    {
        DbContext.ChangeTracker.Clear();

        return await DbContext.TaskItems
            .Include(task => task.TaskStatus)
            .Include(task => task.LogEntries)
                .ThenInclude(log => log.TaskLogType)
            .SingleAsync(task => task.Id == id);
    }

    public async ValueTask DisposeAsync()
    {
        await DbContext.DisposeAsync();
        await connection.DisposeAsync();
        loggerFactory.Dispose();
    }
}
