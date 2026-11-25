using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using Microsoft.Win32;

namespace MultiRdp
{
    public sealed class Profile
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public string Address { get; set; }
        public string UserName { get; set; }
        public string ProtectedPassword { get; set; }
        public bool SmartCards { get; set; }
        public bool WebAuthn { get; set; }
        public bool Drives { get; set; }
        public bool Clipboard { get; set; }
        public bool Printers { get; set; }
        public bool ScaleRemoteDesktop { get; set; }

        public static string ProtectPassword(string password)
        {
            if (string.IsNullOrEmpty(password))
                return null;

            var bytes = Encoding.Unicode.GetBytes(password + "\0");
            try
            {
                return Convert.ToBase64String(ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser));
            }
            finally
            {
                Array.Clear(bytes, 0, bytes.Length);
            }
        }

        public string ReadPassword()
        {
            if (string.IsNullOrEmpty(ProtectedPassword))
                return "";

            var bytes = ProtectedData.Unprotect(Convert.FromBase64String(ProtectedPassword),
                null, DataProtectionScope.CurrentUser);
            try
            {
                return Encoding.Unicode.GetString(bytes).TrimEnd('\0');
            }
            finally
            {
                Array.Clear(bytes, 0, bytes.Length);
            }
        }
    }

    public enum ProfileSort { Manual, Name, Address, UserName, Status }

    public sealed class SavedWindowLayout
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public int[] ColumnWidths { get; set; }
    }

    [XmlRoot("MultiRdp")]
    public sealed class ProfileStore
    {
        private static readonly XmlSerializer Serializer = new XmlSerializer(typeof(ProfileStore));

        public static readonly string Folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MultiRdp");
        private static readonly string FilePath = Path.Combine(Folder, "profiles.xml");

        public ProfileStore()
        {
            Profiles = new List<Profile>();
            PinnedProfiles = new List<Guid>();
        }

        public bool StartHidden { get; set; }
        public ProfileSort SortColumn { get; set; }
        public bool SortDescending { get; set; }
        public SavedWindowLayout WindowLayout { get; set; }
        public List<Profile> Profiles { get; set; }
        public List<Guid> PinnedProfiles { get; set; }

        public static ProfileStore Load()
        {
            try
            {
                using (var stream = File.OpenRead(FilePath))
                using (var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit }))
                    return (ProfileStore)Serializer.Deserialize(reader);
            }
            catch (FileNotFoundException)
            {
                return new ProfileStore();
            }
            catch (DirectoryNotFoundException)
            {
                return new ProfileStore();
            }
        }

        public void Save()
        {
            Directory.CreateDirectory(Folder);
            var temporary = Path.Combine(Folder, Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var stream = File.Open(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    Serializer.Serialize(stream, this);
                    stream.Flush(true);
                }

                if (File.Exists(FilePath))
                    File.Replace(temporary, FilePath, null);
                else
                    File.Move(temporary, FilePath);
            }
            finally
            {
                File.Delete(temporary);
            }
        }
    }

    internal static class Startup
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string Name = "MultiRdp";

        public static bool Enabled
        {
            get
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKey))
                    return key != null && key.GetValue(Name) != null;
            }
            set
            {
                using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (value)
                        key.SetValue(Name, "\"" + System.Windows.Forms.Application.ExecutablePath + "\"",
                            RegistryValueKind.String);
                    else
                        key.DeleteValue(Name, false);
                }
            }
        }
    }
}
