// SettingsService.cs
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace Topiary.Services
{
    public interface ISettingsService
    {
        string GetOpenAIKey();
        void SaveOpenAIKey(string apiKey);
        void ClearOpenAIKey();
        bool HasOpenAIKey { get; }
    }

    public class SettingsService : ISettingsService
    {
        private readonly string _settingsPath;
        private readonly string _encryptionKey;
        
        public SettingsService()
        {
            _settingsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Topiary",
                "settings.dat"
            );
            
            // Generate a unique encryption key based on machine-specific information
            // This isn't perfect security but provides basic protection
            string machineGuid = GetMachineGuid();
            _encryptionKey = machineGuid.Substring(0, 32);
            
            EnsureSettingsDirectory();
        }

        public bool HasOpenAIKey => !string.IsNullOrEmpty(GetOpenAIKey());

        public string GetOpenAIKey()
        {
            if (!File.Exists(_settingsPath))
                return null;

            try
            {
                string encryptedData = File.ReadAllText(_settingsPath);
                return Decrypt(encryptedData);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error reading API key: {ex.Message}");
                return null;
            }
        }

        public void SaveOpenAIKey(string apiKey)
        {
            if (string.IsNullOrEmpty(apiKey))
            {
                ClearOpenAIKey();
                return;
            }

            try
            {
                string encryptedData = Encrypt(apiKey);
                File.WriteAllText(_settingsPath, encryptedData);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving API key: {ex.Message}");
                throw;
            }
        }

        public void ClearOpenAIKey()
        {
            if (File.Exists(_settingsPath))
                File.Delete(_settingsPath);
        }

        private void EnsureSettingsDirectory()
        {
            string directory = Path.GetDirectoryName(_settingsPath);
            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);
        }

        private string Encrypt(string data)
        {
            using (Aes aes = Aes.Create())
            {
                aes.Key = Encoding.UTF8.GetBytes(_encryptionKey);
                aes.GenerateIV();

                using (MemoryStream msEncrypt = new MemoryStream())
                {
                    msEncrypt.Write(aes.IV, 0, aes.IV.Length);

                    using (ICryptoTransform encryptor = aes.CreateEncryptor())
                    using (CryptoStream csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write))
                    using (StreamWriter swEncrypt = new StreamWriter(csEncrypt))
                    {
                        swEncrypt.Write(data);
                    }

                    return Convert.ToBase64String(msEncrypt.ToArray());
                }
            }
        }

        private string Decrypt(string encryptedData)
        {
            byte[] fullCipher = Convert.FromBase64String(encryptedData);

            using (Aes aes = Aes.Create())
            {
                byte[] iv = new byte[16];
                Array.Copy(fullCipher, 0, iv, 0, iv.Length);
                
                byte[] cipher = new byte[fullCipher.Length - iv.Length];
                Array.Copy(fullCipher, iv.Length, cipher, 0, cipher.Length);

                aes.Key = Encoding.UTF8.GetBytes(_encryptionKey);
                aes.IV = iv;

                using (MemoryStream msDecrypt = new MemoryStream(cipher))
                using (ICryptoTransform decryptor = aes.CreateDecryptor())
                using (CryptoStream csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read))
                using (StreamReader srDecrypt = new StreamReader(csDecrypt))
                {
                    return srDecrypt.ReadToEnd();
                }
            }
        }

        private string GetMachineGuid()
        {
            string machineGuid = "";
            try
            {
                machineGuid = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    "SOFTWARE\\Microsoft\\Cryptography")?.GetValue("MachineGuid")?.ToString() ?? "";
            }
            catch
            {
                // Fallback to a default key if registry access fails
                machineGuid = "TopiaryDefaultEncryptionKey123";
            }
            
            // Ensure the key is exactly 32 bytes
            if (machineGuid.Length < 32)
                machineGuid = machineGuid.PadRight(32, '0');
            else if (machineGuid.Length > 32)
                machineGuid = machineGuid.Substring(0, 32);
                
            return machineGuid;
        }
    }
}