using System.Security.Cryptography;
using System.Text;

namespace TranslateTool.Services;

public static class DataProtectionHelper
{
    public static string Protect(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var bytes = Encoding.UTF8.GetBytes(text);
        var encrypted = ProtectedData.Protect(bytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(encrypted);
    }

    public static string Unprotect(string cipher)
    {
        if (string.IsNullOrEmpty(cipher))
        {
            return string.Empty;
        }

        try
        {
            var bytes = Convert.FromBase64String(cipher);
            var plain = ProtectedData.Unprotect(bytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch
        {
            return string.Empty;
        }
    }
}
