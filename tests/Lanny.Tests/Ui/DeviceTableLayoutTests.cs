using System.Runtime.CompilerServices;

namespace Lanny.Tests.Ui;

public class DeviceTableLayoutTests
{
    [Fact]
    public async Task DeviceTable_LastSeenColumn_UsesReservedNonTruncatingLayout()
    {
        var root = FindRepositoryRoot();
        var index = await File.ReadAllTextAsync(Path.Combine(root, "Lanny", "wwwroot", "index.html"));
        var app = await File.ReadAllTextAsync(Path.Combine(root, "Lanny", "wwwroot", "js", "app.js"));
        var css = await File.ReadAllTextAsync(Path.Combine(root, "Lanny", "wwwroot", "css", "style.css"));

        Assert.Contains("<link rel=\"stylesheet\" href=\"css/style.css?v=", index);
        Assert.Contains("<script src=\"js/app.js?v=", index);
        Assert.Contains("<th class=\"sortable date-column last-seen-column\" data-sort=\"lastSeen\">Last Seen</th>", index);
        Assert.Contains("<td class=\"date-cell last-seen-cell\">${formatDateCompact(d.lastSeen)}</td>", app);
        Assert.Contains("--device-table-min-width:", css);
        Assert.Contains("--date-column-width: 13rem;", css);
        Assert.Contains("min-width: max(100%, var(--device-table-min-width));", css);
        Assert.Contains(".date-column,", css);
        Assert.Contains(".date-cell {", css);
        Assert.Contains("overflow: visible;", css);
        Assert.Contains(".hostname-cell,", css);
        Assert.Contains("text-overflow: ellipsis;", css);
    }

    private static string FindRepositoryRoot([CallerFilePath] string sourcePath = "")
    {
        var directory = new FileInfo(sourcePath).Directory;
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Lanny.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root.");
    }
}
