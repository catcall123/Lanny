using Lanny.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using System.Text.Json;

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
            var stringListComparer = new ValueComparer<List<string>?>(
                (left, right) => left == null
                    ? right == null
                    : right != null && left.SequenceEqual(right),
                values => values == null
                    ? 0
                    : string.Join('|', values).GetHashCode(),
                values => values == null ? null : values.ToList());
            var dictionaryComparer = new ValueComparer<Dictionary<string, string>?>(
                (left, right) => DictionariesEqual(left, right),
                values => GetDictionaryHashCode(values),
                values => values == null ? null : new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase));

            e.HasKey(d => d.MacAddress);
            e.Property(d => d.MacAddress).HasMaxLength(17);
            e.Property(d => d.OpenPorts)
             .HasConversion(
                 v => string.Join(',', v),
                 v => string.IsNullOrEmpty(v)
                     ? new List<int>()
                     : v.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList())
             .Metadata.SetValueComparer(openPortsComparer);
            e.Property(d => d.HttpHeaders)
                .HasConversion(
                    v => v == null ? null : JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                    v => string.IsNullOrWhiteSpace(v)
                        ? null
                        : new Dictionary<string, string>(JsonSerializer.Deserialize<Dictionary<string, string>>(v)!, StringComparer.OrdinalIgnoreCase))
                .Metadata.SetValueComparer(dictionaryComparer);
            e.Property(d => d.TlsSubjectAlternativeNames)
                .HasConversion(
                    v => v == null ? null : JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                    v => string.IsNullOrWhiteSpace(v)
                        ? null
                        : JsonSerializer.Deserialize<List<string>>(v)!)
                .Metadata.SetValueComparer(stringListComparer);
        });
    }

    private static bool DictionariesEqual(Dictionary<string, string>? left, Dictionary<string, string>? right)
    {
        if (left == null || right == null)
            return left == right;

        if (left.Count != right.Count)
            return false;

        foreach (var (key, value) in left)
        {
            if (!right.TryGetValue(key, out var otherValue) || !string.Equals(value, otherValue, StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    private static int GetDictionaryHashCode(Dictionary<string, string>? values)
    {
        if (values == null)
            return 0;

        var hash = new HashCode();
        foreach (var (key, value) in values.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
        {
            hash.Add(key, StringComparer.OrdinalIgnoreCase);
            hash.Add(value, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }
}
