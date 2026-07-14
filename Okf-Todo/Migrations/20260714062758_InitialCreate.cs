using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OkfTodo.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BodyFormats",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Code = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    BackgroundColor = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    ForegroundColor = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    IsSelected = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsSystem = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BodyFormats", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Issues",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false),
                    DueUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    BodyHtml = table.Column<string>(type: "TEXT", nullable: false),
                    BodyMarkdown = table.Column<string>(type: "TEXT", nullable: false),
                    EditorMode = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Issues", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TaskLogTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Code = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    BackgroundColor = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    ForegroundColor = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    IsSelected = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsSystem = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskLogTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TaskPriorities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Code = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    BackgroundColor = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    ForegroundColor = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    IsSelected = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsSystem = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskPriorities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TaskRelationTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ReverseName = table.Column<string>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    BackgroundColor = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    ForegroundColor = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    IsSelected = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsSystem = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskRelationTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TaskSources",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Code = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    BackgroundColor = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    ForegroundColor = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    IsSelected = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsSystem = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskSources", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TaskStatuses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Code = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    BackgroundColor = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    ForegroundColor = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    IsSelected = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsSystem = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskStatuses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TaskTags",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Value = table.Column<string>(type: "TEXT", nullable: false, collation: "NOCASE")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskTags", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TaskTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Code = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    BackgroundColor = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    ForegroundColor = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    IsSelected = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsSystem = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TaskItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Body = table.Column<string>(type: "TEXT", nullable: true),
                    BodyFormatId = table.Column<int>(type: "INTEGER", nullable: true),
                    TaskTypeId = table.Column<int>(type: "INTEGER", nullable: false),
                    TaskStatusId = table.Column<int>(type: "INTEGER", nullable: false),
                    TaskPriorityId = table.Column<int>(type: "INTEGER", nullable: true),
                    TaskSourceId = table.Column<int>(type: "INTEGER", nullable: true),
                    SourceReference = table.Column<string>(type: "TEXT", nullable: true),
                    SourceUrl = table.Column<string>(type: "TEXT", nullable: true),
                    Deadline = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ActivatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    WaitingSince = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaskItems_BodyFormats_BodyFormatId",
                        column: x => x.BodyFormatId,
                        principalTable: "BodyFormats",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TaskItems_TaskPriorities_TaskPriorityId",
                        column: x => x.TaskPriorityId,
                        principalTable: "TaskPriorities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TaskItems_TaskSources_TaskSourceId",
                        column: x => x.TaskSourceId,
                        principalTable: "TaskSources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TaskItems_TaskStatuses_TaskStatusId",
                        column: x => x.TaskStatusId,
                        principalTable: "TaskStatuses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TaskItems_TaskTypes_TaskTypeId",
                        column: x => x.TaskTypeId,
                        principalTable: "TaskTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Images",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    IssueId = table.Column<int>(type: "INTEGER", nullable: true),
                    TaskId = table.Column<int>(type: "INTEGER", nullable: true),
                    Filename = table.Column<string>(type: "TEXT", nullable: true),
                    MimeType = table.Column<string>(type: "TEXT", nullable: false),
                    Width = table.Column<int>(type: "INTEGER", nullable: true),
                    Height = table.Column<int>(type: "INTEGER", nullable: true),
                    ImageData = table.Column<byte[]>(type: "BLOB", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Images", x => x.Id);
                    table.CheckConstraint("CK_Images_OneOwner", "(IssueId IS NOT NULL AND TaskId IS NULL) OR (IssueId IS NULL AND TaskId IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_Images_Issues_IssueId",
                        column: x => x.IssueId,
                        principalTable: "Issues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Images_TaskItems_TaskId",
                        column: x => x.TaskId,
                        principalTable: "TaskItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TaskAttachments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TaskId = table.Column<int>(type: "INTEGER", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", nullable: false),
                    ContentType = table.Column<string>(type: "TEXT", nullable: true),
                    FileSize = table.Column<long>(type: "INTEGER", nullable: false),
                    Sha256Hash = table.Column<string>(type: "TEXT", nullable: true),
                    ContentBlob = table.Column<byte[]>(type: "BLOB", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskAttachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaskAttachments_TaskItems_TaskId",
                        column: x => x.TaskId,
                        principalTable: "TaskItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TaskChecklistItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TaskId = table.Column<int>(type: "INTEGER", nullable: false),
                    Text = table.Column<string>(type: "TEXT", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    IsCompleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskChecklistItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaskChecklistItems_TaskItems_TaskId",
                        column: x => x.TaskId,
                        principalTable: "TaskItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TaskComments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TaskId = table.Column<int>(type: "INTEGER", nullable: false),
                    CommentText = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskComments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaskComments_TaskItems_TaskId",
                        column: x => x.TaskId,
                        principalTable: "TaskItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TaskLogEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TaskId = table.Column<int>(type: "INTEGER", nullable: false),
                    TaskLogTypeId = table.Column<int>(type: "INTEGER", nullable: false),
                    Message = table.Column<string>(type: "TEXT", nullable: false),
                    OldValue = table.Column<string>(type: "TEXT", nullable: true),
                    NewValue = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskLogEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaskLogEntries_TaskItems_TaskId",
                        column: x => x.TaskId,
                        principalTable: "TaskItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TaskLogEntries_TaskLogTypes_TaskLogTypeId",
                        column: x => x.TaskLogTypeId,
                        principalTable: "TaskLogTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TaskRelations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SourceTaskId = table.Column<int>(type: "INTEGER", nullable: false),
                    TargetTaskId = table.Column<int>(type: "INTEGER", nullable: false),
                    TaskRelationTypeId = table.Column<int>(type: "INTEGER", nullable: false),
                    Note = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskRelations", x => x.Id);
                    table.CheckConstraint("CK_TaskRelations_SourceTarget_Different", "SourceTaskId <> TargetTaskId");
                    table.ForeignKey(
                        name: "FK_TaskRelations_TaskItems_SourceTaskId",
                        column: x => x.SourceTaskId,
                        principalTable: "TaskItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TaskRelations_TaskItems_TargetTaskId",
                        column: x => x.TargetTaskId,
                        principalTable: "TaskItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TaskRelations_TaskRelationTypes_TaskRelationTypeId",
                        column: x => x.TaskRelationTypeId,
                        principalTable: "TaskRelationTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TaskTaskTags",
                columns: table => new
                {
                    TaskId = table.Column<int>(type: "INTEGER", nullable: false),
                    TaskTagId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskTaskTags", x => new { x.TaskId, x.TaskTagId });
                    table.ForeignKey(
                        name: "FK_TaskTaskTags_TaskItems_TaskId",
                        column: x => x.TaskId,
                        principalTable: "TaskItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TaskTaskTags_TaskTags_TaskTagId",
                        column: x => x.TaskTagId,
                        principalTable: "TaskTags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TaskWaitingFors",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TaskId = table.Column<int>(type: "INTEGER", nullable: false),
                    Label = table.Column<string>(type: "TEXT", nullable: false),
                    WaitingSince = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskWaitingFors", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaskWaitingFors_TaskItems_TaskId",
                        column: x => x.TaskId,
                        principalTable: "TaskItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BodyFormats_Code",
                table: "BodyFormats",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Images_IssueId",
                table: "Images",
                column: "IssueId");

            migrationBuilder.CreateIndex(
                name: "IX_Images_TaskId",
                table: "Images",
                column: "TaskId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskAttachments_TaskId",
                table: "TaskAttachments",
                column: "TaskId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskChecklistItems_TaskId",
                table: "TaskChecklistItems",
                column: "TaskId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskComments_TaskId",
                table: "TaskComments",
                column: "TaskId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskItems_BodyFormatId",
                table: "TaskItems",
                column: "BodyFormatId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskItems_TaskPriorityId",
                table: "TaskItems",
                column: "TaskPriorityId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskItems_TaskSourceId",
                table: "TaskItems",
                column: "TaskSourceId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskItems_TaskStatusId",
                table: "TaskItems",
                column: "TaskStatusId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskItems_TaskTypeId",
                table: "TaskItems",
                column: "TaskTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskLogEntries_TaskId",
                table: "TaskLogEntries",
                column: "TaskId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskLogEntries_TaskLogTypeId",
                table: "TaskLogEntries",
                column: "TaskLogTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskLogTypes_Code",
                table: "TaskLogTypes",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaskPriorities_Code",
                table: "TaskPriorities",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaskRelations_SourceTaskId",
                table: "TaskRelations",
                column: "SourceTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskRelations_TargetTaskId",
                table: "TaskRelations",
                column: "TargetTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskRelations_TaskRelationTypeId",
                table: "TaskRelations",
                column: "TaskRelationTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskRelationTypes_Code",
                table: "TaskRelationTypes",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaskSources_Code",
                table: "TaskSources",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaskStatuses_Code",
                table: "TaskStatuses",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaskTags_Value",
                table: "TaskTags",
                column: "Value",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaskTaskTags_TaskTagId",
                table: "TaskTaskTags",
                column: "TaskTagId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskTypes_Code",
                table: "TaskTypes",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaskWaitingFors_TaskId",
                table: "TaskWaitingFors",
                column: "TaskId",
                unique: true,
                filter: "ResolvedAt IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Images");

            migrationBuilder.DropTable(
                name: "TaskAttachments");

            migrationBuilder.DropTable(
                name: "TaskChecklistItems");

            migrationBuilder.DropTable(
                name: "TaskComments");

            migrationBuilder.DropTable(
                name: "TaskLogEntries");

            migrationBuilder.DropTable(
                name: "TaskRelations");

            migrationBuilder.DropTable(
                name: "TaskTaskTags");

            migrationBuilder.DropTable(
                name: "TaskWaitingFors");

            migrationBuilder.DropTable(
                name: "Issues");

            migrationBuilder.DropTable(
                name: "TaskLogTypes");

            migrationBuilder.DropTable(
                name: "TaskRelationTypes");

            migrationBuilder.DropTable(
                name: "TaskTags");

            migrationBuilder.DropTable(
                name: "TaskItems");

            migrationBuilder.DropTable(
                name: "BodyFormats");

            migrationBuilder.DropTable(
                name: "TaskPriorities");

            migrationBuilder.DropTable(
                name: "TaskSources");

            migrationBuilder.DropTable(
                name: "TaskStatuses");

            migrationBuilder.DropTable(
                name: "TaskTypes");
        }
    }
}
