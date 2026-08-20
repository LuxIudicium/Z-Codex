using Microsoft.EntityFrameworkCore;
using System.Data;
using ZCodex.Core;

namespace ZCodex.Data;

public static class AppDbContextFactory
{
    public static AppDbContext Create()
    {
        var dbPath = AppPaths.DbPath;
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        var ctx = new AppDbContext(options);
        ctx.Database.EnsureCreated();
        EnsureColumns(ctx);
        return ctx;
    }

    // (EF table name, column name, SQL column definition with DEFAULT).
    // Append new columns here as the schema evolves — EnsureColumns handles them all.
    private static readonly (string Table, string Column, string Def)[] _pendingColumns =
    [
        ("CharacterBuilds", "EquipmentJson",  "TEXT NOT NULL DEFAULT 'null'"),
        ("CharacterBuilds", "IsFavorite",     "INTEGER NOT NULL DEFAULT 0"),
        ("CharacterBuilds", "Assignment",     "TEXT NOT NULL DEFAULT '(unassigned)'"),
        ("Skills",          "Adrenaline",     "INTEGER NOT NULL DEFAULT 0"),
        ("Skills",          "Sacrifice",      "INTEGER NOT NULL DEFAULT 0"),
        ("Skills",          "Overcast",       "INTEGER NOT NULL DEFAULT 0"),
        ("Skills",          "Upkeep",         "INTEGER NOT NULL DEFAULT 0"),
        ("Skills",          "Campaign",       "TEXT NOT NULL DEFAULT ''"),
        ("Skills",          "Progression",    "TEXT NOT NULL DEFAULT ''"),
        ("Skills",          "IconUrlHd",      "TEXT NOT NULL DEFAULT ''"),
        ("Skills",          "Conditions",     "TEXT NOT NULL DEFAULT ''"),
        ("Skills",          "Mechanics",      "TEXT NOT NULL DEFAULT ''"),
        ("Skills",          "NameFr",         "TEXT NOT NULL DEFAULT ''"),
        ("Skills",          "DescriptionFr",  "TEXT NOT NULL DEFAULT ''"),
        ("Skills",          "AttributeFr",    "TEXT NOT NULL DEFAULT ''"),
        ("Skills",          "TypeFr",         "TEXT NOT NULL DEFAULT ''"),
        ("Skills",          "FrSuspect",      "INTEGER NOT NULL DEFAULT 0"),
    ];

    private static void EnsureColumns(AppDbContext ctx)
    {
        var conn = ctx.Database.GetDbConnection();
        bool wasOpen = conn.State == ConnectionState.Open;
        if (!wasOpen) conn.Open();
        try
        {
            foreach (var (table, column, def) in _pendingColumns)
            {
                // Saute les tables absentes : une DB neuve n'a que les tables du modèle EF
                // (Skills). Les tables héritées (ex. CharacterBuilds, plus créée par le code)
                // peuvent ne pas exister → un ALTER planterait avec "no such table".
                using var tableCheck = conn.CreateCommand();
                tableCheck.CommandText =
                    $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{table}'";
                if ((long)tableCheck.ExecuteScalar()! == 0) continue;

                using var check = conn.CreateCommand();
                check.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name='{column}'";
                long exists = (long)check.ExecuteScalar()!;
                if (exists == 0)
                {
                    using var alter = conn.CreateCommand();
                    alter.CommandText = $"ALTER TABLE \"{table}\" ADD COLUMN {column} {def}";
                    alter.ExecuteNonQuery();
                }
            }
        }
        finally
        {
            if (!wasOpen) conn.Close();
        }
    }
}
