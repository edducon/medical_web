using System.Security.Cryptography;
using System.Text;

namespace RadPlan.Api.Services;

public sealed class FieldEncryptionService
{
    private readonly byte[] _key;

    public FieldEncryptionService(IConfiguration configuration)
    {
        var rawKey = configuration["Encryption:Key"] ?? throw new InvalidOperationException("Encryption:Key is required.");
        _key = Convert.FromBase64String(rawKey);
        if (_key.Length != 32) throw new InvalidOperationException("Encryption:Key must be a 32-byte Base64 value.");
    }

    public string Encrypt(string value)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var plainText = Encoding.UTF8.GetBytes(value);
        var cipherText = new byte[plainText.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(_key, tag.Length);
        aes.Encrypt(nonce, plainText, cipherText, tag);
        return Convert.ToBase64String(nonce.Concat(tag).Concat(cipherText).ToArray());
    }

    public string Decrypt(string encrypted)
    {
        var payload = Convert.FromBase64String(encrypted);
        if (payload.Length < 29) throw new CryptographicException("Encrypted value is invalid.");
        var plainText = new byte[payload.Length - 28];
        using var aes = new AesGcm(_key, 16);
        aes.Decrypt(payload[..12], payload[28..], payload[12..28], plainText);
        return Encoding.UTF8.GetString(plainText);
    }
}
