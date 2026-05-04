using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations.Design;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;

namespace EntityFrameworkCore.ExtensibleMigrations;

/// <summary>
/// Wraps the default <see cref="ICSharpMigrationOperationGenerator"/> to interleave custom
/// handler-emitted operations around core EF operations, ordered by handler-declared phase.
/// Op order within a phase follows the order operations appear in the input list — which
/// the differ wrapper sorts by handler <c>Order</c>.
/// </summary>
internal sealed class ExtensibleCSharpMigrationOperationGenerator(
    ICSharpMigrationOperationGenerator defaultGenerator,
    IServiceProvider serviceProvider
) : ICSharpMigrationOperationGenerator
{
    private readonly IReadOnlyList<ICSharpMigrationOperationHandler> _handlers = serviceProvider
        .GetServices<ICSharpMigrationOperationHandler>()
        .ToList();

    public void Generate(
        string builderName,
        IReadOnlyList<MigrationOperation> operations,
        IndentedStringBuilder builder
    )
    {
        var beforeCore =
            new List<(MigrationOperation Op, ICSharpMigrationOperationHandler Handler)>();
        var afterCore =
            new List<(MigrationOperation Op, ICSharpMigrationOperationHandler Handler)>();
        var coreOps = new List<MigrationOperation>();

        foreach (var op in operations)
        {
            var handler = _handlers.FirstOrDefault(h => h.CanHandle(op));
            if (handler is null)
            {
                coreOps.Add(op);
                continue;
            }

            switch (handler.Phase(op))
            {
                case OperationPhase.BeforeCore:
                    beforeCore.Add((op, handler));
                    break;
                case OperationPhase.AfterCore:
                    afterCore.Add((op, handler));
                    break;
            }
        }

        foreach (var (op, h) in beforeCore)
            h.Generate(op, builder);
        if (beforeCore.Count > 0 && (coreOps.Count > 0 || afterCore.Count > 0))
            builder.AppendLine();

        defaultGenerator.Generate(builderName, coreOps, builder);
        if (coreOps.Count > 0 && afterCore.Count > 0)
            builder.AppendLine();

        foreach (var (op, h) in afterCore)
            h.Generate(op, builder);
    }
}
