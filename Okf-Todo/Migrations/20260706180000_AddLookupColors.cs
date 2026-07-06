using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Photino.Okf_Todo.Data;

#nullable disable

namespace Photino.Okf_Todo.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260706180000_AddLookupColors")]
    public partial class AddLookupColors : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var tableName in LookupTableNames)
            {
                migrationBuilder.AddColumn<string>(
                    name: "BackgroundColor",
                    table: tableName,
                    type: "TEXT",
                    maxLength: 32,
                    nullable: true);

                migrationBuilder.AddColumn<string>(
                    name: "ForegroundColor",
                    table: tableName,
                    type: "TEXT",
                    maxLength: 32,
                    nullable: true);
            }

            foreach (var tableName in new[] { "TaskTypes", "TaskStatuses", "TaskPriorities" })
            {
                migrationBuilder.Sql($"""
                    UPDATE {tableName}
                    SET BackgroundColor = '#6b7280',
                        ForegroundColor = '#ffffff'
                    WHERE BackgroundColor IS NULL
                       OR ForegroundColor IS NULL
                    """);
            }

            migrationBuilder.Sql("""
                UPDATE TaskTypes
                SET BackgroundColor = '#b42318',
                    ForegroundColor = '#ffffff'
                WHERE Code = 'CRITICAL_ERROR'
                """);

            migrationBuilder.Sql("""
                UPDATE TaskTypes
                SET BackgroundColor = '#facc15',
                    ForegroundColor = '#111827'
                WHERE Code = 'ERROR'
                """);

            migrationBuilder.Sql("""
                UPDATE TaskPriorities
                SET BackgroundColor = '#b42318',
                    ForegroundColor = '#ffffff'
                WHERE Code = 'URGENT'
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var tableName in LookupTableNames)
            {
                migrationBuilder.DropColumn(
                    name: "BackgroundColor",
                    table: tableName);

                migrationBuilder.DropColumn(
                    name: "ForegroundColor",
                    table: tableName);
            }
        }

        private static readonly string[] LookupTableNames =
        [
            "AttachmentKinds",
            "BodyFormats",
            "StakeholderRoles",
            "StakeholderTypes",
            "TaskLogTypes",
            "TaskPriorities",
            "TaskRelationTypes",
            "TaskSources",
            "TaskStatuses",
            "TaskTypes"
        ];
    }
}
