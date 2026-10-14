using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Meshmakers.Octo.Frontend.ManagementTool.Services;

/// <summary>
///     Generic helper to keep secret values (tokens, passwords, client secrets) out of console output,
///     logs and agent transcripts. Reveals only the length, never any part of the value.
/// </summary>
internal static partial class SecretMasker
{
    // Member-name fragments that mark a value as a credential (compared case-insensitively after
    // dropping '_', '-', '.' and spaces). "token" is handled separately, see IsSecretName.
    private static readonly string[] SecretNameFragments =
    [
        "password", "passwd", "passphrase", "secret", "apikey", "privatekey", "accesskey", "sharedkey",
        "signingkey", "credential", "connectionstring", "authorization", "bearer"
    ];

    // Member names that contain a secret fragment but only describe the secret (flags, timestamps),
    // e.g. the platform's own SecretIsSet / ClientSecretKeyMissing / ClientSecretSetAt attributes.
    private static readonly string[] DescriptiveSuffixes = ["isset", "keymissing", "setat", "policy"];

    /// <summary>
    ///     Returns a placeholder for <paramref name="secret" />: <c>(not set)</c> for null/empty,
    ///     otherwise <c>********</c> plus the length.
    /// </summary>
    public static string Mask(string? secret) =>
        string.IsNullOrEmpty(secret) ? "(not set)" : $"******** ({secret.Length} chars)";

    /// <summary>
    ///     True when a member / key with this name holds a credential, judged by its name
    ///     (<c>Password</c>, <c>clientSecret</c>, <c>apiKey</c>, <c>accessToken</c>, ...).
    /// </summary>
    public static bool IsSecretName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var n = new string(name.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        if (DescriptiveSuffixes.Any(s => n.EndsWith(s, StringComparison.Ordinal)))
        {
            return false;
        }

        // "token" alone is too broad (TokenEndpoint, TokenLifetime): only token / xyzToken / pwd.
        return n is "pwd" or "token" || n.EndsWith("token", StringComparison.Ordinal) ||
               n.EndsWith("pwd", StringComparison.Ordinal) ||
               SecretNameFragments.Any(f => n.Contains(f, StringComparison.Ordinal));
    }

    /// <summary>
    ///     Returns a copy of <paramref name="token" /> in which the value of every credential-named
    ///     member is replaced by <see cref="Mask" />. String values that themselves contain JSON
    ///     (adapter configurations are stored as a JSON string) or <c>key: value</c> / <c>key=value</c>
    ///     lines (pipeline definitions) are masked the same way. Booleans and nulls stay visible, so
    ///     "is a password set" flags keep their meaning.
    /// </summary>
    public static JToken MaskToken(JToken token)
    {
        var copy = token.DeepClone();
        MaskInPlace(copy);
        return copy;
    }

    /// <summary>
    ///     Masks credentials in free text: JSON, YAML or <c>key=value</c> lines. Used for values whose
    ///     structure is not known up front.
    /// </summary>
    public static string MaskText(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text ?? string.Empty;
        }

        var trimmed = text.TrimStart();
        if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
        {
            try
            {
                var parsed = JToken.Parse(text);
                MaskInPlace(parsed);
                return parsed.ToString(text.Contains('\n') ? Formatting.Indented : Formatting.None);
            }
            catch (JsonException)
            {
                // not JSON after all: fall through to the line based masking
            }
        }

        return KeyValueLine().Replace(text, m =>
            IsSecretName(m.Groups["key"].Value) && !IsBlockMarkerOrEmpty(m.Groups["val"].Value)
                ? m.Groups["prefix"].Value + Mask(UnquoteTrim(m.Groups["val"].Value))
                : m.Value);
    }

    private static void MaskInPlace(JToken token)
    {
        switch (token)
        {
            case JProperty property:
                if (IsSecretName(property.Name) && property.Value.Type is not (JTokenType.Null or JTokenType.Boolean))
                {
                    property.Value = new JValue(Mask(property.Value.Type == JTokenType.String
                        ? property.Value.Value<string>()
                        : property.Value.ToString(Formatting.None)));
                }
                else
                {
                    MaskInPlace(property.Value);
                }

                break;
            case JValue { Type: JTokenType.String } value:
                var text = value.Value<string>();
                var masked = MaskText(text);
                if (!ReferenceEquals(text, masked) && text != masked)
                {
                    value.Value = masked;
                }

                break;
            default:
                foreach (var child in token.Children().ToList())
                {
                    MaskInPlace(child);
                }

                break;
        }
    }

    private static bool IsBlockMarkerOrEmpty(string value) => string.IsNullOrWhiteSpace(value) ||
                                                           value.Trim() is "|" or ">" or "|-" or ">-";

    private static string UnquoteTrim(string value) =>
        value.Trim().TrimEnd(',').Trim().Trim('"', '\'');

    // [prefix]key[:=]value on one line; prefix keeps indentation, list dash and quotes of the key.
    [GeneratedRegex("""^(?<prefix>[ \t]*(?:-[ \t]+)?["']?(?<key>[A-Za-z0-9_.\- ]+?)["']?[ \t]*[:=][ \t]*)(?<val>[^\r\n]*?)[ \t]*$""",
        RegexOptions.Multiline)]
    private static partial Regex KeyValueLine();
}
