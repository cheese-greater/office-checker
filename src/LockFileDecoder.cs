using System;
using System.Collections.Generic;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace OfficeLockChecker
{
    internal enum LockFileKind
    {
        MicrosoftOffice,
        LibreOffice
    }

    internal sealed class LockFile
    {
        public LockFileKind Kind { get; set; }
        public string FullPath { get; set; }
        public string FileName { get; set; }
        public DateTime LastWriteTime { get; set; }
        public bool IsDecoded { get; set; }
        public string UserName { get; set; }
        public string WindowsAccount { get; set; }

        // The part of the document's file name that the lock file name preserves.
        public string TargetName
        {
            get
            {
                if (Kind == LockFileKind.LibreOffice)
                {
                    int prefixLength = LockFileDecoder.LibreOfficePrefix.Length;
                    return FileName.Substring(prefixLength, FileName.Length - prefixLength - LockFileDecoder.LibreOfficeSuffix.Length);
                }
                return FileName.Substring(LockFileDecoder.MicrosoftPrefix.Length);
            }
        }
    }

    internal static class LockFileDecoder
    {
        public const string MicrosoftPrefix = "~$";
        public const string LibreOfficePrefix = ".~lock.";
        public const string LibreOfficeSuffix = "#";

        // Byte 0 of an Office owner file holds the user name length and the ANSI name follows it.
        private const int AnsiNameOffset = 1;
        // A 16-bit length and the UTF-16LE name follow at byte 54 in Word's layout and at byte 55 in Excel's.
        private static readonly int[] UnicodeLengthOffsets = { 54, 55 };
        // Lock files are tiny; capping the read keeps a misnamed large file from slowing the scan.
        private const int MaximumBytesToRead = 4096;

        public static LockFile TryDescribe(FileInfo file)
        {
            string fileName = file.Name;
            LockFileKind kind;
            if (fileName.StartsWith(MicrosoftPrefix, StringComparison.Ordinal) && fileName.Length > MicrosoftPrefix.Length)
            {
                kind = LockFileKind.MicrosoftOffice;
            }
            else if (fileName.StartsWith(LibreOfficePrefix, StringComparison.Ordinal)
                && fileName.EndsWith(LibreOfficeSuffix, StringComparison.Ordinal)
                && fileName.Length > LibreOfficePrefix.Length + LibreOfficeSuffix.Length)
            {
                kind = LockFileKind.LibreOffice;
            }
            else
            {
                return null;
            }
            return new LockFile { Kind = kind, FullPath = file.FullName, FileName = fileName, LastWriteTime = file.LastWriteTime };
        }

        public static void Decode(LockFile lockFile)
        {
            if (lockFile.IsDecoded)
            {
                return;
            }
            lockFile.IsDecoded = true;
            lockFile.UserName = ReadUserName(lockFile);
            lockFile.WindowsAccount = GetOwnerAccount(lockFile.FullPath);
        }

        public static string DecodeMicrosoftOffice(byte[] lockBytes)
        {
            foreach (int lengthOffset in UnicodeLengthOffsets)
            {
                int nameOffset = lengthOffset + 2;
                if (lockBytes.Length < nameOffset)
                {
                    continue;
                }
                int unicodeLength = lockBytes[lengthOffset] | (lockBytes[lengthOffset + 1] << 8);
                if (unicodeLength > 0 && nameOffset + unicodeLength * 2 <= lockBytes.Length)
                {
                    string unicodeName = Encoding.Unicode.GetString(lockBytes, nameOffset, unicodeLength * 2);
                    if (IsPlausibleName(unicodeName))
                    {
                        return unicodeName.Trim();
                    }
                }
            }
            int ansiLength = lockBytes.Length > 0 ? lockBytes[0] : 0;
            if (ansiLength > 0 && AnsiNameOffset + ansiLength <= lockBytes.Length)
            {
                string ansiName = Encoding.Default.GetString(lockBytes, AnsiNameOffset, ansiLength);
                if (IsPlausibleName(ansiName))
                {
                    return ansiName.Trim();
                }
            }
            return null;
        }

        // LibreOffice lock files are one text record: full name, system user, host, time and profile URL.
        public static string DecodeLibreOffice(byte[] lockBytes)
        {
            List<string> fields = SplitLibreOfficeFields(Encoding.UTF8.GetString(lockBytes).TrimStart('\uFEFF'));
            string fullName = GetField(fields, 0);
            string systemUser = GetField(fields, 1);
            string host = GetField(fields, 2);
            string account = systemUser.Length > 0 && host.Length > 0 ? systemUser + " on " + host : systemUser + host;
            if (fullName.Length == 0)
            {
                return account.Length > 0 ? account : null;
            }
            return account.Length > 0 ? fullName + " (" + account + ")" : fullName;
        }

        private static string ReadUserName(LockFile lockFile)
        {
            byte[] lockBytes;
            try
            {
                lockBytes = ReadLockBytes(lockFile.FullPath);
            }
            catch (IOException exception)
            {
                return "(could not read lock file: " + exception.Message + ")";
            }
            catch (UnauthorizedAccessException)
            {
                return "(no permission to read lock file)";
            }
            if (lockBytes.Length == 0)
            {
                return "(empty lock file)";
            }
            string userName = lockFile.Kind == LockFileKind.LibreOffice
                ? DecodeLibreOffice(lockBytes)
                : DecodeMicrosoftOffice(lockBytes);
            return userName ?? "(unrecognized lock file contents)";
        }

        private static byte[] ReadLockBytes(string path)
        {
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                byte[] buffer = new byte[(int)Math.Min(stream.Length, MaximumBytesToRead)];
                int totalBytesRead = 0;
                while (totalBytesRead < buffer.Length)
                {
                    int bytesRead = stream.Read(buffer, totalBytesRead, buffer.Length - totalBytesRead);
                    if (bytesRead == 0)
                    {
                        break;
                    }
                    totalBytesRead += bytesRead;
                }
                if (totalBytesRead < buffer.Length)
                {
                    Array.Resize(ref buffer, totalBytesRead);
                }
                return buffer;
            }
        }

        // The account that created the lock file; best effort because shares may hide or fail to map owners.
        private static string GetOwnerAccount(string path)
        {
            try
            {
                FileSecurity security = File.GetAccessControl(path, AccessControlSections.Owner);
                IdentityReference ownerSid = security.GetOwner(typeof(SecurityIdentifier));
                if (ownerSid == null)
                {
                    return null;
                }
                try
                {
                    return ownerSid.Translate(typeof(NTAccount)).Value;
                }
                catch (IdentityNotMappedException)
                {
                    return ownerSid.Value;
                }
            }
            catch (SystemException)
            {
                return null;
            }
        }

        private static bool IsPlausibleName(string name)
        {
            if (name.Trim().Length == 0)
            {
                return false;
            }
            foreach (char character in name)
            {
                if (char.IsControl(character))
                {
                    return false;
                }
            }
            return true;
        }

        private static string GetField(List<string> fields, int index)
        {
            return index < fields.Count ? fields[index].Trim() : string.Empty;
        }

        // An unescaped comma ends a field and a semicolon ends the record; a backslash escapes the next character.
        private static List<string> SplitLibreOfficeFields(string text)
        {
            List<string> fields = new List<string>();
            StringBuilder field = new StringBuilder();
            for (int position = 0; position < text.Length; position++)
            {
                char character = text[position];
                if (character == '\\' && position + 1 < text.Length)
                {
                    position++;
                    field.Append(text[position]);
                }
                else if (character == ',')
                {
                    fields.Add(field.ToString());
                    field.Clear();
                }
                else if (character == ';')
                {
                    break;
                }
                else
                {
                    field.Append(character);
                }
            }
            fields.Add(field.ToString());
            return fields;
        }
    }
}
