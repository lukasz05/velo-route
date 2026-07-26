using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace VeloRoute.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Route> Routes => Set<Route>();
    public DbSet<Share> Shares => Set<Share>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(builder =>
        {
            builder.HasKey(u => u.Id);
            builder.Property(u => u.CreatedAt).HasDefaultValueSql("now()");
        });

        modelBuilder.Entity<Route>(builder =>
        {
            builder.HasKey(r => r.Id);
            builder.Property(r => r.CreatedAt).HasDefaultValueSql("now()");
            builder.Property(r => r.Tags).HasColumnType("text[]");

            builder.Property(r => r.Geometry)
                .HasConversion(
                    g => JsonSerializer.Serialize(g, (JsonSerializerOptions?)null),
                    s => JsonSerializer.Deserialize<GeoJsonLineString>(s, (JsonSerializerOptions?)null)!)
                .HasColumnType("jsonb");

            builder.HasOne<User>()
                .WithMany()
                .HasForeignKey(r => r.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Share>(builder =>
        {
            builder.HasKey(s => s.Id);
            builder.Property(s => s.CreatedAt).HasDefaultValueSql("now()");
            builder.HasIndex(s => s.RouteId).IsUnique();
            builder.HasIndex(s => s.Token).IsUnique();

            builder.HasOne<Route>()
                .WithMany()
                .HasForeignKey(s => s.RouteId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
