using System.Security.Cryptography;
using System.Text;

namespace ModernNotepad.Core.Analysis;

internal static class StableFindingId
{
    public static string Create(string category, string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{category}\n{value}"));
        return $"{category}:{Convert.ToHexString(bytes.AsSpan(0, 8)).ToLowerInvariant()}";
    }
}
