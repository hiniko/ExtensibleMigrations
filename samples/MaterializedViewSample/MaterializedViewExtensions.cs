using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MaterializedViewSample;

/// <summary>
/// Consumer-side helper. Combines the Tier 1 native EF mechanism (a keyless
/// entity mapped via <c>ToView</c> — the view appears as a typed entry in
/// the model and snapshot) with the Tier 2 management annotations the
/// migration handler reads.
///
/// The framework itself does not ship this; copy + adapt for your own
/// concept. See <c>docs/snapshot-completeness.md</c>.
/// </summary>
public static class MaterializedViewExtensions
{
    public const string ViewNameAnnotation = "MatView:Name";
    public const string ViewQueryAnnotation = "MatView:Query";

    public static ModelBuilder HasMaterializedView<TProjection>(
        this ModelBuilder modelBuilder,
        string viewName,
        string query,
        Action<EntityTypeBuilder<TProjection>>? configure = null
    )
        where TProjection : class
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        ArgumentException.ThrowIfNullOrEmpty(viewName);
        ArgumentException.ThrowIfNullOrEmpty(query);

        modelBuilder.Entity<TProjection>(b =>
        {
            b.HasNoKey();
            b.ToView(viewName);
            b.HasAnnotation(ViewNameAnnotation, viewName);
            b.HasAnnotation(ViewQueryAnnotation, query);
            configure?.Invoke(b);
        });
        return modelBuilder;
    }
}
