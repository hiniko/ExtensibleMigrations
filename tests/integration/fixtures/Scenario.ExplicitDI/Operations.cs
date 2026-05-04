using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Scenario.ExplicitDI;

public sealed class TaggedMarkerOperation : MigrationOperation
{
    public string EntityName { get; init; } = "";
    public string Marker { get; init; } = "";
}
