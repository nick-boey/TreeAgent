using Microsoft.EntityFrameworkCore;
using TreeAgent.Web.Features.Agents.Data;
using TreeAgent.Web.Features.PullRequests.Data.Entities;

namespace TreeAgent.Web.Features.PullRequests.Data;

public class TreeAgentDbContext(DbContextOptions<TreeAgentDbContext> options) : DbContext(options)
{
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<Feature> Features => Set<Feature>();
    public DbSet<Agent> Agents => Set<Agent>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<SystemPromptTemplate> SystemPromptTemplates => Set<SystemPromptTemplate>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Project>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired();
            entity.Property(e => e.LocalPath).IsRequired();
            entity.Property(e => e.DefaultBranch).HasDefaultValue("main");
        });

        modelBuilder.Entity<Feature>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).IsRequired();
            entity.Property(e => e.Status).HasConversion<string>();

            entity.HasOne(e => e.Project)
                .WithMany(p => p.Features)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Parent)
                .WithMany(p => p.Children)
                .HasForeignKey(e => e.ParentId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Agent>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Status).HasConversion<string>();

            entity.HasOne(e => e.Feature)
                .WithMany(f => f.Agents)
                .HasForeignKey(e => e.FeatureId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Message>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Role).IsRequired();
            entity.Property(e => e.Content).IsRequired();

            entity.HasOne(e => e.Agent)
                .WithMany(a => a.Messages)
                .HasForeignKey(e => e.AgentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SystemPromptTemplate>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired();
            entity.Property(e => e.Content).IsRequired();

            entity.HasOne(e => e.Project)
                .WithMany(p => p.PromptTemplates)
                .HasForeignKey(e => e.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Project>(entity =>
        {
            entity.HasOne(e => e.DefaultPromptTemplate)
                .WithMany()
                .HasForeignKey(e => e.DefaultPromptTemplateId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}
