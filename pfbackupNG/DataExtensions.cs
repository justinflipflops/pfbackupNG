using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace pfbackupNG
{
    public static class DataSafetyExtensions
    {
        public static string GetSafeString(this string Input, char Replace = '_')
        {
            string _result = Input;
            char[] _invalids = Path.GetInvalidFileNameChars();
            foreach (char _invalid in _invalids)
            {
                _result = _result.Replace(_invalid, Replace);
            }
            return _result;
        }
    }
    public static class DataProtectionExtensions
    {
        public static string pfbackup_Encrypt(this string PlainText, string Key)
        {
            try
            {
                byte[] objInitVectorBytes = Encoding.UTF8.GetBytes($"m_4qh&TMX_zfqq@R");
                byte[] objPlainTextBytes = Encoding.UTF8.GetBytes(PlainText);
                Rfc2898DeriveBytes objPassword = new Rfc2898DeriveBytes(Key, objInitVectorBytes);
                byte[] objKeyBytes = objPassword.GetBytes(256 / 8);
                Aes objSymmetricKey = Aes.Create();
                objSymmetricKey.Mode = CipherMode.CBC;
                ICryptoTransform objEncryptor = objSymmetricKey.CreateEncryptor(objKeyBytes, objInitVectorBytes);
                MemoryStream objMemoryStream = new MemoryStream();
                CryptoStream objCryptoStream = new CryptoStream(objMemoryStream, objEncryptor, CryptoStreamMode.Write);
                objCryptoStream.Write(objPlainTextBytes, 0, objPlainTextBytes.Length);
                objCryptoStream.FlushFinalBlock();
                byte[] objEncrypted = objMemoryStream.ToArray();
                objMemoryStream.Dispose();
                objCryptoStream.Dispose();
                return Convert.ToBase64String(objEncrypted);
            }
            catch (Exception _ex)
            {
                return _ex.Message;
            }
        }
        public static string pfbackup_Decrypt(this string EncryptedText, string Key)
        {
            try
            {
                byte[] objInitVectorBytes = Encoding.ASCII.GetBytes($"m_4qh&TMX_zfqq@R");
                byte[] objDeEncryptedText = Convert.FromBase64String(EncryptedText);
                Rfc2898DeriveBytes objPassword = new Rfc2898DeriveBytes(Key, objInitVectorBytes);
                byte[] objKeyBytes = objPassword.GetBytes(256 / 8);
                Aes objSymmetricKey = Aes.Create();
                objSymmetricKey.Mode = CipherMode.CBC;
                ICryptoTransform objDecryptor = objSymmetricKey.CreateDecryptor(objKeyBytes, objInitVectorBytes);
                MemoryStream objMemoryStream = new MemoryStream(objDeEncryptedText);
                CryptoStream objCryptoStream = new CryptoStream(objMemoryStream, objDecryptor, CryptoStreamMode.Read);
                byte[] objPlainTextBytes = new byte[objDeEncryptedText.Length];
                int objDecryptedByteCount = objCryptoStream.Read(objPlainTextBytes, 0, objPlainTextBytes.Length);
                objMemoryStream.Dispose();
                objCryptoStream.Dispose();
                return Encoding.UTF8.GetString(objPlainTextBytes, 0, objDecryptedByteCount);
            }
            catch { return ""; }
        }
    }
}
