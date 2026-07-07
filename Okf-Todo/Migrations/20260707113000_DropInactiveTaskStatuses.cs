using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Photino.Okf_Todo.Data;

#nullable disable

namespace Photino.Okf_Todo.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260707113000_DropInactiveTaskStatuses")]
    public partial class DropInactiveTaskStatuses : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE TaskItems
                SET TaskStatusId = (SELECT Id FROM TaskStatuses WHERE Code = 'ACTIVE'),
                    ActivatedAt = COALESCE(ActivatedAt, CreatedAt),
                    UpdatedAt = CURRENT_TIMESTAMP
                WHERE TaskStatusId IN (
                    SELECT Id
                    FROM TaskStatuses
                    WHERE Code IN ('NEW', 'WAITING')
                )
                """);

            migrationBuilder.Sql(
                """
                DELETE FROM TaskStatuses
                WHERE Code IN ('NEW', 'WAITING')
                  AND NOT EXISTS (
                      SELECT 1
                      FROM TaskItems
                      WHERE TaskItems.TaskStatusId = TaskStatuses.Id
                  )
                """);

            migrationBuilder.Sql("UPDATE TaskStatuses SET SortOrder = 10 WHERE Code = 'ACTIVE'");
            migrationBuilder.Sql("UPDATE TaskStatuses SET SortOrder = 20 WHERE Code = 'COMPLETED'");
            migrationBuilder.Sql("UPDATE TaskStatuses SET SortOrder = 30 WHERE Code = 'CANCELLED'");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                INSERT OR IGNORE INTO TaskStatuses (
                    Code,
                    Name,
                    Description,
                    BackgroundColor,
                    ForegroundColor,
                    SortOrder,
                    IsActive,
                    IsSystem,
                    CreatedAt,
                    UpdatedAt
                )
                VALUES (
                    'NEW',
                    'New',
                    NULL,
                    '#6b7280',
                    '#ffffff',
                    10,
                    1,
                    1,
                    CURRENT_TIMESTAMP,
                    CURRENT_TIMESTAMP
                )
                """);

            migrationBuilder.Sql(
                """
                INSERT OR IGNORE INTO TaskStatuses (
                    Code,
                    Name,
                    Description,
                    BackgroundColor,
                    ForegroundColor,
                    SortOrder,
                    IsActive,
                    IsSystem,
                    CreatedAt,
                    UpdatedAt
                )
                VALUES (
                    'WAITING',
                    'Waiting',
                    NULL,
                    '#6b7280',
                    '#ffffff',
                    30,
                    1,
                    1,
                    CURRENT_TIMESTAMP,
                    CURRENT_TIMESTAMP
                )
                """);

            migrationBuilder.Sql("UPDATE TaskStatuses SET SortOrder = 20 WHERE Code = 'ACTIVE'");
            migrationBuilder.Sql("UPDATE TaskStatuses SET SortOrder = 30 WHERE Code = 'WAITING'");
            migrationBuilder.Sql("UPDATE TaskStatuses SET SortOrder = 40 WHERE Code = 'COMPLETED'");
            migrationBuilder.Sql("UPDATE TaskStatuses SET SortOrder = 50 WHERE Code = 'CANCELLED'");
        }
    }
}
