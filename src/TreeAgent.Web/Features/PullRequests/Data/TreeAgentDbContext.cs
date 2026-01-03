using Microsoft.EntityFrameworkCore;
using TreeAgent.Web.Features.PullRequests.Data.Entities;

namespace TreeAgent.Web.Features.PullRequests.Data;

public class TreeAgentDbContext(DbContextOptions<TreeAgentDbContext> options) : DbContext(options)
{
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<PullRequest> PullRequests => Set<PullRequest>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Project>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired();
            entity.Property(e => e.LocalPath).IsRequired();
            entity.Property(e => e.DefaultBranch).HasDefaultValue("main");
        });

        modelBuilder.Entity<PullRequest>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).IsRequired();
            entity.Property(e => e.Status).HasConversion<string>();

            entity.HasOne(e => e.Project)
                .WithMany(p => p.PullRequests)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Parent)
                .WithMany(p => p.Children)
                .HasForeignKey(e => e.ParentId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
