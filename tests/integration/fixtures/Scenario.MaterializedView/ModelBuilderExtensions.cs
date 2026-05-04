using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Scenario.MaterializedView;

/// <summary>
/// Fixture-local helper. The framework itself does not ship this — it's the
/// pattern a consumer would write to combine Tier 1 (keyless + ToView entity
/// for snapshot completeness) with Tier 2 (annotations the framework's
/// materialised-view handler reads). Copy + adapt for your own concept.
/// </summary>
public static class ModelBuilderMaterializedViewExtensions
{
    public const string MatViewNameAnnotationKey = "MatView:Name";
    public const string MatViewQueryAnnotationKey = "MatView:Query";

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
            b.HasAnnotation(MatViewNameAnnotationKey, viewName);
            b.HasAnnotation(MatViewQueryAnnotationKey, query);
            configure?.Invoke(b);
        });
        return modelBuilder;
    }
}
