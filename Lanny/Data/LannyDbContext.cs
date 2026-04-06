using Lanny.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Lanny.Data;

public class LannyDbContext : DbContext
{
    public LannyDbContext(DbContextOptions<LannyDbContext> options) : base(options) { }

    public DbSet<Device> Devices => Set<Device>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Device>(e =>
        {
            var openPortsComparer = new ValueComparer<List<int>>(
                (left, right) => left == null
                    ? right == null
                    : right != null && left.SequenceEqual(right),
                ports => ports == null
                    ? 0
                    : string.Join(',', ports).GetHashCode(),
                ports => ports == null ? new List<int>() : ports.ToList());

            e.HasKey(d => d.MacAddress);
            e.Property(d => d.MacAddress).HasMaxLength(17);
            e.Property(d => d.OpenPorts)
             .HasConversion(
                 v => string.Join(',', v),
                 v => string.IsNullOrEmpty(v)
                     ? new List<int>()
                     : v.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList())
             .Metadata.SetValueComparer(openPortsComparer);
        });
    }
}
