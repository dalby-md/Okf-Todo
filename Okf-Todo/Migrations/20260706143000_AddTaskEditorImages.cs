using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Photino.Okf_Todo.Data;

#nullable disable

namespace Photino.Okf_Todo.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260706143000_AddTaskEditorImages")]
    public partial class AddTaskEditorImages : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Images_IssueId",
                table: "Images");

            migrationBuilder.CreateTable(
                name: "Images_New",
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
                    table.CheckConstraint(
                        "CK_Images_OneOwner",
                        "(IssueId IS NOT NULL AND TaskId IS NULL) OR (IssueId IS NULL AND TaskId IS NOT NULL)");
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

            migrationBuilder.Sql(
                """
                INSERT INTO Images_New (Id, IssueId, TaskId, Filename, MimeType, Width, Height, ImageData, CreatedUtc)
                SELECT Id, IssueId, NULL, Filename, MimeType, Width, Height, ImageData, CreatedUtc
                FROM Images
                """);

            migrationBuilder.DropTable(name: "Images");
            migrationBuilder.RenameTable(name: "Images_New", newName: "Images");
            migrationBuilder.CreateIndex("IX_Images_IssueId", "Images", "IssueId");
            migrationBuilder.CreateIndex("IX_Images_TaskId", "Images", "TaskId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Images_IssueId",
                table: "Images");
            migrationBuilder.DropIndex(
                name: "IX_Images_TaskId",
                table: "Images");

            migrationBuilder.CreateTable(
                name: "Images_Old",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    IssueId = table.Column<int>(type: "INTEGER", nullable: false),
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
                    table.ForeignKey(
                        name: "FK_Images_Issues_IssueId",
                        column: x => x.IssueId,
                        principalTable: "Issues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql(
                """
                INSERT INTO Images_Old (Id, IssueId, Filename, MimeType, Width, Height, ImageData, CreatedUtc)
                SELECT Id, IssueId, Filename, MimeType, Width, Height, ImageData, CreatedUtc
                FROM Images
                WHERE IssueId IS NOT NULL
                """);

            migrationBuilder.DropTable(name: "Images");
            migrationBuilder.RenameTable(name: "Images_Old", newName: "Images");
            migrationBuilder.CreateIndex("IX_Images_IssueId", "Images", "IssueId");
        }
    }
}
