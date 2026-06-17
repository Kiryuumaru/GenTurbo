namespace Application.EmbeddedConfig.Utilities;

internal static class EmbeddedConfigDecryptor
{
    private static readonly byte[] PayloadEncryptionSalt =
    [
        0x4A, 0x97, 0xB2, 0x3E, 0xC1, 0x58, 0xD6, 0x7F,
        0xA3, 0x0E, 0x91, 0x42, 0xF5, 0x8B, 0x64, 0xD0
    ];

    private const int PayloadEncryptionIterations = 10000;
    private const int IvLength = 16;
    private const int KeyLength = 32;

    internal static string Decrypt(string encryptedBase64, string appName, string version, string appTag)
    {
        if (string.IsNullOrEmpty(encryptedBase64))
        {
            return string.Empty;
        }

        var encryptedBytes = Convert.FromBase64String(encryptedBase64);

        if (encryptedBytes.Length <= IvLength)
        {
            throw new InvalidOperationException("Encrypted embedded config payload is too short.");
        }

        var iv = encryptedBytes.AsSpan(0, IvLength).ToArray();
        var ciphertext = encryptedBytes.AsSpan(IvLength).ToArray();

        var password = System.Text.Encoding.UTF8.GetBytes(appName + "|" + version + "|" + appTag);

        var keyBytes = System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2(
            password, PayloadEncryptionSalt, PayloadEncryptionIterations, System.Security.Cryptography.HashAlgorithmName.SHA1, KeyLength);

        using var aes = System.Security.Cryptography.Aes.Create();
        aes.Key = keyBytes;
        aes.IV = iv;
        aes.Mode = System.Security.Cryptography.CipherMode.CBC;
        aes.Padding = System.Security.Cryptography.PaddingMode.PKCS7;

        using var decryptor = aes.CreateDecryptor();
        var plaintextBytes = decryptor.TransformFinalBlock(ciphertext, 0, ciphertext.Length);

        return System.Text.Encoding.UTF8.GetString(plaintextBytes);
    }
}
