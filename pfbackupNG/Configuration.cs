using System;
using Newtonsoft.Json;
using System.Globalization;
using System.IO;
using System.Text;
using System.Collections.Generic;
using Newtonsoft.Json.Converters;
using System.Net;
using System.Security;
using System.Security.Cryptography;

namespace pfbackupNG
{
    public class Configuration
    {

        GlobalConfiguration _global = new GlobalConfiguration();
        private string _global_path = String.Empty;
        DeviceConfiguration[] _device = new DeviceConfiguration[0];
        private string _device_path = String.Empty;
        public GlobalConfiguration Global { get { return _global; } set { _global = value; } }
        public DeviceConfiguration[] Devices { get { return _device; } set { _device = value; } }
        public Configuration()
        {
            _global = new GlobalConfiguration();
            _device = new DeviceConfiguration[0];
        }
        public Configuration(string GlobalPath, string DevicePath)
        {
            try
            {
                if (File.Exists(GlobalPath))
                {
                    try
                    {
                        _global = JsonConvert.DeserializeObject<GlobalConfiguration>(File.ReadAllText(GlobalPath));
                        _global_path = GlobalPath;
                    }
                    catch (Exception _ex)
                    {
                        throw new FileLoadException("Failed to load Global configuration, see inner exception for details.", _ex);
                    }
                }
                else
                    throw new FileNotFoundException("Global configuration path does not exist.");

                if (File.Exists(DevicePath))
                {
                    try
                    {
                        _device = JsonConvert.DeserializeObject<DeviceConfiguration[]>(File.ReadAllText(DevicePath));
                        _device_path = DevicePath;
                    }
                    catch (Exception _ex)
                    {
                        throw new FileLoadException("Failed to load Device configuration, see inner exception for details.", _ex);
                    }
                }
                else
                    throw new FileNotFoundException("Device configuration path does not exist.");
            }
            catch (Exception _ex) { throw new Exception("Failed to load configuration, see inner exception for details.", _ex); }
        }

        public void Save(bool SaveGlobal = false, bool SaveDevices = false)
        {
            try
            {
                if (SaveGlobal)
                {
                    if (File.Exists(_global_path))
                    {
                        try
                        {
                            File.WriteAllText(_global_path, JsonConvert.SerializeObject(_global, Formatting.Indented));
                        }
                        catch (Exception _ex)
                        {
                            throw new FileLoadException("Failed to save Global configuration, see inner exception for details.", _ex);
                        }
                    }
                    else
                        throw new FileNotFoundException("Global configuration path does not exist.");
                }
                if (SaveDevices)
                {
                    if (File.Exists(_device_path))
                    {
                        try
                        {
                            File.WriteAllText(_device_path, JsonConvert.SerializeObject(_device, Formatting.Indented));
                        }
                        catch (Exception _ex)
                        {
                            throw new FileLoadException("Failed to save Device configuration, see inner exception for details.", _ex);
                        }
                    }
                    else
                        throw new FileNotFoundException("Device configuration path does not exist.");
                }
            }
            catch (Exception _ex) { throw new Exception("Failed to save configuration, see inner exception for details.", _ex); }
        }

        public string BuildGlobalDefault()
        {
            Global.EncryptionKey = $"ChangeThisToSomethingMoreSecure";
            Global.Azure.ConnectionString = $"AzureBlobStorageConnectionString";
            Global.Azure.Container = $"AzureBlobStorageContainer";
            try { return JsonConvert.SerializeObject(Global, Formatting.Indented); }
            catch { return null; }
        }
        public string BuildDeviceDefault()
        {
            Devices = new DeviceConfiguration[] {
                new DeviceConfiguration() {
                    Name = $"Example 1",
                    Address = $"0.0.0.0",
                    Port = 80,
                    UseSSL = false,
                    PollInterval = new PollInterval(),
                    Credentials = new DeviceConfigurationCredentials("Username 1","Password 1"),
                    Version = DeviceConfiguration.DeviceConfigurationVersion.V233_LATER
                },
                new DeviceConfiguration() {
                    Name = $"Example 2",
                    Address = $"0.0.0.0",
                    Port = 443,
                    UseSSL = true,
                    PollInterval = new PollInterval(),
                    Credentials = new DeviceConfigurationCredentials("Username 2","Password 2"),
                    Version = DeviceConfiguration.DeviceConfigurationVersion.V226_V232P1
                }
            };
            try { return JsonConvert.SerializeObject(Devices, Formatting.Indented); }
            catch { return null; }
        }
    }
    public class GlobalConfiguration
    {
        public string EncryptionKey { get; set; }
        public AzureConfiguration Azure { get; set; }
        public GlobalConfiguration()
        {
            EncryptionKey = String.Empty;
            Azure = new AzureConfiguration();
        }
    }

    public class AzureConfiguration
    {
        public string ConnectionString { get; set; }
        public string Container { get; set; }
        public AzureConfiguration()
        {
            ConnectionString = String.Empty;
            Container = String.Empty;
        }
    }
    public class DeviceConfigurationCredentials
    {
        public string Username { get; set; }
        public string Password { get; set; }
        public bool Encrypted { get; set; }
        public DeviceConfigurationCredentials()
        {
            Username = String.Empty;
            Password = String.Empty;
            Encrypted = false;
        }
        public DeviceConfigurationCredentials(string Username, string Password) : this(Username,Password,false)
        {
            return;
        }
        public DeviceConfigurationCredentials(string Username, string Password, bool Encrypted)
        {
            if (String.IsNullOrWhiteSpace(Username))
                throw new ArgumentNullException("Username cannot be null, empty, or whitespace.");
            if (String.IsNullOrEmpty(Password))
                throw new ArgumentNullException("Password cannot be null or empty.");
            this.Username = Username;
            this.Password = Password;
            this.Encrypted = Encrypted;
        }
    }
    public class DeviceConfiguration
    {
        public enum DeviceConfigurationVersion
        {
            V20X_V225, //0
            V226_V232P1, //1
            V233_LATER //2
        }
        public bool Enabled { get; set; }
        public string Name { get; set; }
        public string Address { get; set; }
        public int Port { get; set; }
        public bool UseSSL { get; set; }
        /* string Username { get; set; }
        public SecureString Password { get; set; }*/
        public DeviceConfigurationCredentials Credentials { get; set; }
        public PollInterval PollInterval { get; set; }
        public DeviceConfigurationVersion Version { get; set; }
        public DeviceConfiguration()
        {
            Enabled = false;
            Name = string.Empty;
            Address = string.Empty;
            PollInterval = new PollInterval();
            Credentials = new DeviceConfigurationCredentials();
        }
        public string GetRequestUrl()
        {
            StringBuilder _builder = new StringBuilder();
            if (UseSSL)
                _builder.Append($"https");
            else
                _builder.Append($"http");
            _builder.Append($"://");
            _builder.Append($"{Address}:{Port}");
            return _builder.ToString();
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
            catch(Exception _ex)
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