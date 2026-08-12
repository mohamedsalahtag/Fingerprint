using System.DirectoryServices;
using System.Runtime.Versioning;

namespace ZKDeviceManager.Web.Services;

/// <summary>
/// Reads user accounts from Active Directory (under the configured OU) so they can be imported as
/// employees. Uses the app's own Windows identity to bind — no credentials stored. Config:
///   Auth:LdapPath  e.g. "LDAP://192.168.2.19/OU=Users,DC=sharbatly,DC=com"
/// </summary>
[SupportedOSPlatform("windows")]
public class AdDirectory
{
    private readonly IConfiguration _cfg;
    private readonly ILogger<AdDirectory> _log;

    public AdDirectory(IConfiguration cfg, ILogger<AdDirectory> log) { _cfg = cfg; _log = log; }

    private string Server => _cfg["Auth:LdapServer"] ?? "192.168.2.19";
    private string? _baseDn;

    /// <summary>The domain base DN. Uses config "Auth:LdapSearchBase" if set, else auto-discovers the real
    /// defaultNamingContext from the directory (so a wrong/guessed OU path can't break it).</summary>
    private string BaseDn()
    {
        if (!string.IsNullOrEmpty(_baseDn)) return _baseDn;
        var configured = _cfg["Auth:LdapSearchBase"];
        if (!string.IsNullOrWhiteSpace(configured)) return _baseDn = configured;
        using var root = new DirectoryEntry($"LDAP://{Server}/RootDSE");
        _baseDn = root.Properties["defaultNamingContext"].Value?.ToString() ?? "";
        return _baseDn;
    }

    public record AdUser(string Name, string Login, string? EmployeeId, string? Department, string? Title, string? Mail);

    /// <summary>Search enabled AD users by name / login / employee id. Throws on directory errors.</summary>
    public List<AdUser> Search(string? term, int max = 2000)
    {
        using var root = new DirectoryEntry($"LDAP://{Server}/{BaseDn()}");
        using var searcher = new DirectorySearcher(root)
        {
            PageSize = 500,
            SizeLimit = max,
            SearchScope = SearchScope.Subtree,
            ReferralChasing = ReferralChasingOption.None
        };
        searcher.PropertiesToLoad.AddRange(new[]
        { "displayName", "cn", "sAMAccountName", "employeeNumber", "employeeID", "department", "title", "mail" });

        // Person accounts that are not disabled (userAccountControl bit 2).
        var f = "(&(objectCategory=person)(objectClass=user)(!(userAccountControl:1.2.840.113556.1.4.803:=2))";
        var t = term?.Trim();
        if (!string.IsNullOrEmpty(t))
        {
            var e = Escape(t);
            f += $"(|(displayName=*{e}*)(cn=*{e}*)(sAMAccountName=*{e}*)(employeeNumber=*{e}*)(employeeID=*{e}*))";
        }
        f += ")";
        searcher.Filter = f;

        var list = new List<AdUser>();
        using var results = searcher.FindAll();
        foreach (SearchResult r in results)
        {
            string? P(string k) => r.Properties.Contains(k) && r.Properties[k].Count > 0 ? r.Properties[k][0]?.ToString() : null;
            var name = P("displayName") ?? P("cn") ?? P("sAMAccountName") ?? "";
            list.Add(new AdUser(name, P("sAMAccountName") ?? "",
                P("employeeNumber") ?? P("employeeID"), P("department"), P("title"), P("mail")));
        }
        return list.OrderBy(u => u.Name).ToList();
    }

    private static string Escape(string s) => s
        .Replace("\\", "\\5c").Replace("*", "\\2a").Replace("(", "\\28").Replace(")", "\\29").Replace("\0", "\\00");
}
