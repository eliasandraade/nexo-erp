using System.Text.RegularExpressions;
using FluentAssertions;

namespace Nexo.UnitTests.Users;

/// <summary>
/// The rule "a company keeps at least one active Diretoria" lives in NexoDbContext.SaveChanges, so it holds for every write
/// that goes through EF change tracking, a future one included. The writes below go around SaveChanges: none of them touches
/// who is a director today, and this test keeps it that way. Deleting a user or a tenant does not exist either.
/// </summary>
public class UserWriteSurfaceTests
{
    private const RegexOptions Options = RegexOptions.Singleline;
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(5);

    [Fact]
    public void Nothing_removes_users_or_tenants()
    {
        Offenders(@"\.(Users|Tenants)\s*\.\s*Remove(Range)?\s*\(|Set<(User|Tenant)>\(\)\s*\.\s*Remove(Range)?\s*\(").Should().BeEmpty();
    }

    [Fact]
    public void No_bulk_operation_deletes_users_or_writes_their_role_or_status()
    {
        var bulk = new Regex(@"(\.Users\b|Set<User>\(\))[^;]*?\.Execute(?<kind>Update|Delete)(Async)?\s*\([^;]*;", Options, Limit);
        var offenders = new List<string>();
        foreach (var (file, text) in SourceFiles())
        {
            foreach (Match match in bulk.Matches(text))
            {
                var touchesRoleOrStatus = Regex.IsMatch(match.Value, @"SetProperty\(\s*\w+\s*=>\s*\w+\.(Role|Status)\b", RegexOptions.None, Limit);
                if (match.Groups["kind"].Value == "Delete" || touchesRoleOrStatus) offenders.Add(file);
            }
        }

        offenders.Should().BeEmpty();
    }

    [Fact]
    public void No_raw_sql_writes_the_users_table()
    {
        Offenders(@"\b(UPDATE|DELETE\s+FROM|TRUNCATE(\s+TABLE)?)\s+(nexo\.)?""?users""?(\s|;|$)").Should().BeEmpty();
    }

    private static List<string> Offenders(string pattern)
    {
        var regex = new Regex(pattern, Options, Limit);
        return SourceFiles().Where(f => regex.IsMatch(f.Text)).Select(f => f.File).ToList();
    }

    /// <summary>Everything that runs in production. Migrations are out: they run once and are reviewed on their own.</summary>
    private static IEnumerable<(string File, string Text)> SourceFiles()
    {
        var src = Path.Combine(RepoRoot(), "nexo-backend", "src");
        char separator = Path.DirectorySeparatorChar;
        return Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{separator}bin{separator}") && !f.Contains($"{separator}obj{separator}") && !f.Contains($"{separator}Migrations{separator}"))
            .Select(f => (f, File.ReadAllText(f)));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "nexo-backend"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
