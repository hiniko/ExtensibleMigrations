using EntityFrameworkCore.ExtensibleMigrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Scenario.SnapshotOnly;

[CustomMigrationHandler(Order = 100)]
public sealed class MetaOwnerSnapshotHandler : IMigrationsSnapshotHandler
{
    public void GenerateSnapshot(IModel model, IndentedStringBuilder builder)
    {
        foreach (var et in model.GetEntityTypes())
        {
            if (et.FindAnnotation("Meta:Owner")?.Value is not string owner)
                continue;
            builder.AppendLine($"// MetaOwner: {et.Name} -> {owner}");
        }
    }
}
