using Microsoft.EntityFrameworkCore;
using BackupSynchronizer.Models;

namespace BackupSynchronizer.Data;

public class FileHistoryContext : DbContext
{
    public FileHistoryContext(DbContextOptions<FileHistoryContext> options) : base(options)
    {
    }

    public DbSet<FileHistory> FileHistories { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FileHistory>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.FilePath).IsRequired().HasMaxLength(500);
            entity.Property(e => e.OriginalPath).IsRequired().HasMaxLength(500);
            entity.Property(e => e.FileHash).IsRequired().HasMaxLength(64);
            entity.Property(e => e.ChangeType).IsRequired().HasMaxLength(20);
            entity.Property(e => e.HistoryPath).HasMaxLength(500);
            entity.Property(e => e.Reason).HasMaxLength(100);
            
            // Create indexes for better performance
            entity.HasIndex(e => e.FilePath);
            entity.HasIndex(e => e.OriginalPath);
            entity.HasIndex(e => e.Timestamp);
            entity.HasIndex(e => e.ChangeType);
            entity.HasIndex(e => e.IsDeleted);
        });
    }
}
