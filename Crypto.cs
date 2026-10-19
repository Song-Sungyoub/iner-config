using System.Security.Cryptography;
using System.Text;

namespace InerConfig;
public static class Crypto
{
    // CE1: PBKDF2-SHA256(600000), salt(16), nonce(12), tag(16), ciphertext.
    private static readonly byte[] Aad = Encoding.ASCII.GetBytes("CE1");
    public static string Transform(string input, string password, bool decrypt)
    {
        if (string.IsNullOrEmpty(password)) throw new ArgumentException("비밀번호를 입력하세요.");
        byte[] data;
        if (decrypt)
        {
            if (!input.StartsWith("CE1:")) throw new FormatException("CE1 형식의 암호문만 지원합니다.");
            data = Convert.FromBase64String(input[4..]);
            if (data.Length < 44) throw new FormatException("암호문이 너무 짧습니다.");
        }
        else
        {
            data = new byte[44 + Encoding.UTF8.GetByteCount(input)];
            RandomNumberGenerator.Fill(data.AsSpan(0, 28));
        }
        var key = Rfc2898DeriveBytes.Pbkdf2(password, data.AsSpan(0, 16), 600_000, HashAlgorithmName.SHA256, 32);
        byte[] plain = decrypt ? new byte[data.Length - 44] : Encoding.UTF8.GetBytes(input);
        try
        {
            using var aes = new AesGcm(key, 16);
            if (decrypt)
            {
                aes.Decrypt(data.AsSpan(16, 12), data.AsSpan(44), data.AsSpan(28, 16), plain, Aad);
                return new UTF8Encoding(false, true).GetString(plain);
            }
            aes.Encrypt(data.AsSpan(16, 12), plain, data.AsSpan(44), data.AsSpan(28, 16), Aad);
            return "CE1:" + Convert.ToBase64String(data);
        }
        finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(plain); }
    }
}
