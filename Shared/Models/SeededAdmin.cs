namespace SPE_website.Shared.Models;

/// <summary>
/// A Team Leader account created — and re-applied — at every startup, so a fresh deployment
/// always has someone who can sign in and hand out roles, and the app can never be locked out
/// of its own admin.
///
/// Configured rather than hard-coded, under the <c>SeededAdmins</c> array. In development that
/// is user secrets (<c>dotnet user-secrets set "SeededAdmins:0:Email" "you@example.com"</c>);
/// in a deployment it is the environment, which the default configuration provider reads with
/// <c>__</c> in place of <c>:</c>:
///
/// <code>
/// SeededAdmins__0__Email=you@example.com
/// SeededAdmins__0__Name=SPE Team Leader
/// </code>
///
/// The addresses do not have to be SPE members — any address works — and the accounts can be
/// changed afterwards from member management like any other.
/// </summary>
public class SeededAdmin
{
    public string Email { get; set; } = string.Empty;

    /// <summary>Display name for the account. Falls back to a generic label if left unset.</summary>
    public string Name { get; set; } = string.Empty;
}
