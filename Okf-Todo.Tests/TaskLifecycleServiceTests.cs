using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Photino.Okf_Todo.Data;
using Photino.Okf_Todo.Services;

namespace Okf_Todo.Tests;

public sealed class TaskLifecycleServiceTests
{
    [Fact]
    public async Task LookupSeeding_InsertsValuesOnlyWhenTablesAreEmpty()
    {
        await using var database = await TestDatabase.CreateAsync();

        var taskTypeCount = await database.DbContext.TaskTypes.CountAsync();
        var statusCount = await database.DbContext.TaskStatuses.CountAsync();
        var tagCount = await database.DbContext.TaskTags.CountAsync();

        Assert.True(taskTypeCount > 0);
        Assert.True(statusCount > 0);
        Assert.True(tagCount > 0);

        var newStatus = await database.DbContext.TaskStatuses.SingleAsync(status => status.Code == TaskStatusCodes.New);
        newStatus.Name = "Renamed New";
        await database.DbContext.SaveChangesAsync();

        await database.SeedAsync();

        Assert.Equal(taskTypeCount, await database.DbContext.TaskTypes.CountAsync());
        Assert.Equal(statusCount, await database.DbContext.TaskStatuses.CountAsync());
        Assert.Equal(tagCount, await database.DbContext.TaskTags.CountAsync());

        var unchangedStatus = await database.DbContext.TaskStatuses.SingleAsync(status => status.Code == TaskStatusCodes.New);
        Assert.Equal("Renamed New", unchangedStatus.Name);
    }

    [Fact]
    public async Task CreateTask_CreatesNewTaskAndTaskCreatedLog()
    {
        await using var database = await TestDatabase.CreateAsync();

        var task = await database.Lifecycle.CreateTaskAsync(new TaskCreateRequest(
            Title: "Fix failed deployment",
            TaskTypeCode: "ERROR",
            BodyFormatCode: "HTML",
            TaskPriorityCode: "NORMAL"));

        var savedTask = await database.LoadTaskAsync(task.Id);

        Assert.Equal(TaskStatusCodes.New, savedTask.TaskStatus?.Code);
        Assert.NotEqual(default, savedTask.CreatedAt);
        Assert.NotEqual(default, savedTask.UpdatedAt);
        AssertHasLog(savedTask, TaskLogTypeCodes.TaskCreated, "Task created");
    }

    [Fact]
    public async Task StartTask_ChangesStatusToActiveAndLogsStatusChange()
    {
        await using var database = await TestDatabase.CreateAsync();
        var task = await database.CreateTaskAsync();

        await database.Lifecycle.StartTaskAsync(task.Id);

        var savedTask = await database.LoadTaskAsync(task.Id);

        Assert.Equal(TaskStatusCodes.Active, savedTask.TaskStatus?.Code);
        Assert.NotNull(savedTask.ActivatedAt);
        AssertHasLog(savedTask, TaskLogTypeCodes.StatusChanged, "Status changed from New to Active");
    }

    [Fact]
    public async Task AddWaitingFor_CreatesActiveWaitTargetSetsWaitingAndLogs()
    {
        await using var database = await TestDatabase.CreateAsync();
        var task = await database.CreateTaskAsync();
        await database.Lifecycle.StartTaskAsync(task.Id);

        await database.Lifecycle.AddWaitingForAsync(
            task.Id,
            new TaskWaitingForRequest("INC123456"));

        var savedTask = await database.LoadTaskAsync(task.Id);
        var activeTargets = await database.DbContext.TaskWaitingFors
            .Where(waitingFor => waitingFor.TaskId == task.Id && waitingFor.ResolvedAt == null)
            .ToListAsync();

        Assert.Equal(TaskStatusCodes.Waiting, savedTask.TaskStatus?.Code);
        Assert.NotNull(savedTask.WaitingSince);
        Assert.Single(activeTargets);
        AssertHasLog(savedTask, TaskLogTypeCodes.WaitingForChanged, "Waiting for changed to INC123456");
        AssertHasLog(savedTask, TaskLogTypeCodes.StatusChanged, "Status changed from Active to Waiting");
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
        AssertHasLog(savedTask, TaskLogTypeCodes.StatusChanged, "Status changed from Waiting to Active");
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
    public async Task AddWaitingFor_RejectsSecondActiveWaitingTarget()
    {
        await using var database = await TestDatabase.CreateAsync();
        var task = await database.CreateWaitingTaskAsync();

        var exception = await Assert.ThrowsAsync<ValidationException>(() => database.Lifecycle.AddWaitingForAsync(
            task.Id,
            new TaskWaitingForRequest("Anna")));

        Assert.Equal("waitingFor", exception.Field);
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
        await Lifecycle.StartTaskAsync(task.Id);
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
