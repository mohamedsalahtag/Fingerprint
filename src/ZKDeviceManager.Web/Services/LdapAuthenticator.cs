using System.DirectoryServices;
using System.DirectoryServices.Protocols;
using System.Net;
using System.Runtime.Versioning;

namespace ZKDeviceManager.Web.Services;

/// <summary>
/// Validates a username/password against Active Directory by attempting an LDAP bind to the configured
/// domain controller. No credentials are stored by the app. Config (appsettings "Auth"):
///   Enabled   - master switch; when false the whole app is anonymous (safety hatch to avoid lockout).
///   LdapServer- e.g. "192.168.2.19"
///   LdapPort  - default 389
///   Domain    - UPN suffix appended when the user types a bare name, e.g. "sharbatlyfruit.com".
/// </summary>
[SupportedOSPlatform("windows")]
public class LdapAuthenticator
{
    private readonly IConfiguration _cfg;
    private readonly ILogger<LdapAuthenticator> _log;

    public LdapAuthenticator(IConfiguration cfg, ILogger<LdapAuthenticator> log) { _cfg = cfg; _log = log; }

    public bool Enabled => _cfg.GetValue("Auth:Enabled", false);
    private string Server => _cfg["Auth:LdapServer"] ?? "192.168.2.19";
    private int Port => _cfg.GetValue("Auth:LdapPort", 389);
    private string Domain => _cfg["Auth:Domain"] ?? "sharbatlyfruit.com";
    /// <summary>When set, only members (incl. nested) of this AD group (its cn) may sign in. Empty = anyone in the domain.</summary>
    private string? AllowedGroup => _cfg["Auth:AllowedGroup"];

    /// <summary>Returns true if the credentials bind successfully. On failure, <paramref name="error"/> explains why.</summary>
    public bool Validate(string username, string password, out string error)
    {
        error = "";
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        { error = "Enter your username and password."; return false; }

        // Accept "user", "user@domain" or "DOMAIN\\user"; add the UPN suffix for a bare name.
        var principal = username.Contains('\\') || username.Contains('@') ? username : $"{username}@{Domain}";
        try
        {
            using var conn = new LdapConnection(new LdapDirectoryIdentifier(Server, Port));
            conn.SessionOptions.ProtocolVersion = 3;
            conn.AuthType = AuthType.Negotiate;
            conn.Timeout = TimeSpan.FromSeconds(15);
            conn.Bind(new NetworkCredential(principal, password));

            // Optional: restrict sign-in to members of a specific AD group.
            if (!string.IsNullOrWhiteSpace(AllowedGroup) && !IsInGroup(username, AllowedGroup, out error))
                return false;

            return true;
        }
        catch (LdapException ex)
        {
            _log.LogWarning("LDAP bind failed for {User}: {Msg}", username, ex.Message);
            error = "Invalid username or password.";
            return false;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "LDAP error contacting {Server}", Server);
            error = "Could not reach the directory server. Try again or contact IT.";
            return false;
        }
    }

    private static string Escape(string s) => s
        .Replace("\\", "\\5c").Replace("*", "\\2a").Replace("(", "\\28").Replace(")", "\\29").Replace("\0", "\\00");

    /// <summary>True if the user is a member (including nested groups) of the named AD group.</summary>
    private bool IsInGroup(string username, string groupName, out string error)
    {
        error = "";
        try
        {
            using var rootDse = new DirectoryEntry($"LDAP://{Server}/RootDSE");
            var baseDn = rootDse.Properties["defaultNamingContext"].Value?.ToString();
            using var root = new DirectoryEntry($"LDAP://{Server}/{baseDn}");

            using var groupSearch = new DirectorySearcher(root)
            { Filter = $"(&(objectCategory=group)(cn={Escape(groupName)}))" };
            groupSearch.PropertiesToLoad.Add("distinguishedName");
            var g = groupSearch.FindOne();
            if (g == null) { error = $"The access group \"{groupName}\" was not found in the directory."; return false; }
            var groupDn = g.Properties["distinguishedName"][0]?.ToString();

            var sam = username.Contains('\\') ? username[(username.IndexOf('\\') + 1)..]
                    : username.Contains('@') ? username[..username.IndexOf('@')]
                    : username;

            // 1.2.840.113556.1.4.1941 = "member of (nested)" matching rule.
            using var userSearch = new DirectorySearcher(root)
            { Filter = $"(&(sAMAccountName={Escape(sam)})(memberOf:1.2.840.113556.1.4.1941:={groupDn}))" };
            userSearch.PropertiesToLoad.Add("distinguishedName");
            if (userSearch.FindOne() == null)
            { error = "Your account is not in the group allowed to use this app. Contact IT for access."; return false; }
            return true;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "AD group membership check failed for {User}", username);
            error = "Could not verify your access group. Try again or contact IT.";
            return false;
        }
    }
}
