using Microsoft.EntityFrameworkCore;

namespace ManutencaoEmCampo.Web.Data;

internal static class DemoSqliteSchema
{
    public static async Task InitializeAsync(WorkDbContext db)
    {
        await db.Database.EnsureCreatedAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();

        // Existing demo databases predate sessions and do not have a migrations history.
        var activeColumn = await db.Database.SqlQueryRaw<int>(
            """SELECT count(*) AS "Value" FROM pragma_table_info('AspNetUsers') WHERE name = 'IsActive'""")
            .SingleAsync();
        if (activeColumn == 0)
            await db.Database.ExecuteSqlRawAsync(
                """ALTER TABLE "AspNetUsers" ADD COLUMN "IsActive" INTEGER NOT NULL DEFAULT 1""");

        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "Sessions" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_Sessions" PRIMARY KEY,
                "UserId" TEXT NOT NULL,
                "ExpiresAt" TEXT NOT NULL,
                CONSTRAINT "FK_Sessions_AspNetUsers_UserId"
                    FOREIGN KEY ("UserId") REFERENCES "AspNetUsers" ("Id") ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS "IX_Sessions_UserId" ON "Sessions" ("UserId");
            CREATE INDEX IF NOT EXISTS "IX_Sessions_ExpiresAt" ON "Sessions" ("ExpiresAt");
            """);
        await transaction.CommitAsync();
    }
}
