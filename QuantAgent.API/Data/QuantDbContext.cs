using Microsoft.EntityFrameworkCore;
using QuantAgent.API.Models;
using QuantAgent.API.Models.Enums;

namespace QuantAgent.API.Data;

/// <summary>
/// EF Core context for the quantitative betting agent.
/// Maps domain models to PostgreSQL tables using snake_case naming
/// conventions to match typical Postgres tooling expectations.
/// </summary>
public class QuantDbContext : DbContext
{
    public QuantDbContext(DbContextOptions<QuantDbContext> options) : base(options)
    {
    }

    public DbSet<Partido> Partidos => Set<Partido>();

    public DbSet<Prediccion> Predicciones => Set<Prediccion>();

    public DbSet<ReglaAprendida> ReglasAprendidas => Set<ReglaAprendida>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // --- Partido ----------------------------------------------------------
        modelBuilder.Entity<Partido>(entity =>
        {
            entity.ToTable("partidos");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.EquipoLocal).HasColumnName("equipo_local").IsRequired().HasMaxLength(200);
            entity.Property(e => e.EquipoVisitante).HasColumnName("equipo_visitante").IsRequired().HasMaxLength(200);
            entity.Property(e => e.FechaInicio).HasColumnName("fecha_inicio");
            entity.Property(e => e.Estado).HasColumnName("estado").HasConversion<int>();
            entity.Property(e => e.GolesLocal).HasColumnName("goles_local");
            entity.Property(e => e.GolesVisitante).HasColumnName("goles_visitante");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");

            entity.HasIndex(e => e.FechaInicio).HasDatabaseName("ix_partidos_fecha_inicio");
            entity.HasIndex(e => e.Estado).HasDatabaseName("ix_partidos_estado");
        });

        // --- Prediccion -------------------------------------------------------
        modelBuilder.Entity<Prediccion>(entity =>
        {
            entity.ToTable("predicciones");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.PartidoId).HasColumnName("partido_id");
            entity.Property(e => e.Seleccion).HasColumnName("seleccion").IsRequired().HasMaxLength(100);
            entity.Property(e => e.Cuota).HasColumnName("cuota").HasColumnType("numeric(10,3)");
            entity.Property(e => e.Confianza).HasColumnName("confianza");
            entity.Property(e => e.Razonamiento).HasColumnName("razonamiento").IsRequired().HasMaxLength(2000);
            entity.Property(e => e.Estado).HasColumnName("estado").HasConversion<int>();
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            entity.Property(e => e.Mercado).HasColumnName("mercado").HasConversion<int>();
            entity.Property(e => e.CornersOverUnder).HasColumnName("corners_over_under").HasColumnType("numeric(5,1)");
            entity.Property(e => e.TotalGoals).HasColumnName("total_goals").HasColumnType("numeric(5,1)");

            entity.HasOne(e => e.Partido)
                  .WithMany()
                  .HasForeignKey(e => e.PartidoId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.PartidoId).HasDatabaseName("ix_predicciones_partido_id");
            entity.HasIndex(e => e.Estado).HasDatabaseName("ix_predicciones_estado");
        });

        // --- ReglaAprendida ---------------------------------------------------
        modelBuilder.Entity<ReglaAprendida>(entity =>
        {
            entity.ToTable("reglas_aprendidas");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Equipo).HasColumnName("equipo").IsRequired().HasMaxLength(200);
            entity.Property(e => e.Contexto).HasColumnName("contexto").IsRequired().HasMaxLength(500);
            entity.Property(e => e.Regla).HasColumnName("regla").IsRequired().HasMaxLength(2000);
            entity.Property(e => e.Peso).HasColumnName("peso").HasDefaultValue(1);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");

            entity.HasIndex(e => e.Equipo).HasDatabaseName("ix_reglas_aprendidas_equipo");

            entity.Property(e => e.PrediccionId).HasColumnName("prediccion_id");

            entity.HasOne(e => e.Prediccion)
                  .WithMany()
                  .HasForeignKey(e => e.PrediccionId)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(e => e.PrediccionId).HasDatabaseName("ix_reglas_aprendidas_prediccion_id");
        });
    }
}
