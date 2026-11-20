using System.Text.RegularExpressions;

namespace RepoBoard.Core.Git;

/// <summary>
/// Masks credentials in git's messages before they reach the user: an error can quote a remote URL with a token in it
/// (<c>https://user:token@host</c>) or a server's unfiltered <c>remote:</c> lines. Those messages end up in the row,
/// the activity log and Windows notifications, which stay in the notification centre and on the lock screen.
/// </summary>
public static partial class GitSecrets
{
    public const string Mask = "***";

    /// <summary>The user info of a URL: <c>scheme://user:password@</c> or <c>scheme://token@</c>.</summary>
    [GeneratedRegex(@"(?<scheme>\b[A-Za-z][A-Za-z0-9+.\-]*://)(?<user>[^/\s:@]*)(?::(?<password>[^/\s@]*))?@", RegexOptions.CultureInvariant)]
    private static partial Regex UrlCredentials();

    /// <summary>Tokens with a known prefix, wherever they appear: GitHub (classic and fine-grained) and GitLab.</summary>
    [GeneratedRegex(@"\b(?:gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,}|glpat-[A-Za-z0-9_\-]{20,})", RegexOptions.CultureInvariant)]
    private static partial Regex KnownTokens();

    public static string Redact(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var masked = UrlCredentials().Replace(text, match =>
        {
            var scheme = match.Groups["scheme"].Value;
            var user = match.Groups["user"].Value;
            if (match.Groups["password"].Success)
            {
                return $"{scheme}{user}:{Mask}@";
            }

            // Without a password over HTTP the user name is the token (https://<token>@github.com); an SSH user name
            // (ssh://git@host) is not a secret.
            return scheme.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? $"{scheme}{Mask}@" : match.Value;
        });
        return KnownTokens().Replace(masked, Mask);
    }
}
