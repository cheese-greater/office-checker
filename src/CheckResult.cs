using System;
using System.IO;

namespace OfficeLockChecker
{
    // Ordered by importance: results sort in this order and the status icons are indexed by it.
    internal enum LockStatus
    {
        Locked,
        InUseWithoutLockFile,
        PossiblyStale,
        OrphanLockFile,
        Error,
        NotLocked
    }

    internal sealed class CheckResult
    {
        public LockStatus Status { get; set; }
        public string ItemPath { get; set; }
        public string LockedBy { get; set; }
        public string WindowsAccount { get; set; }
        public DateTime? LockedSince { get; set; }
        public string LockFileName { get; set; }
        public string Detail { get; set; }

        public string ItemName
        {
            get
            {
                string itemName = Path.GetFileName(ItemPath);
                return string.IsNullOrEmpty(itemName) ? ItemPath : itemName;
            }
        }

        public string FolderPath
        {
            get { return Path.GetDirectoryName(ItemPath) ?? ItemPath; }
        }

        public static CheckResult ForError(string itemPath, string errorMessage)
        {
            return new CheckResult { Status = LockStatus.Error, ItemPath = itemPath, Detail = errorMessage };
        }

        public static string GetLabel(LockStatus status)
        {
            switch (status)
            {
                case LockStatus.Locked:
                    return "Locked";
                case LockStatus.InUseWithoutLockFile:
                    return "In use, no lock file";
                case LockStatus.PossiblyStale:
                    return "Possibly stale lock";
                case LockStatus.OrphanLockFile:
                    return "Orphan lock file";
                case LockStatus.Error:
                    return "Error";
                default:
                    return "Not locked";
            }
        }

        public static string GetDescription(LockStatus status)
        {
            switch (status)
            {
                case LockStatus.Locked:
                    return "The document is open and its lock file names who opened it.";
                case LockStatus.InUseWithoutLockFile:
                    return "The document is held open, but there is no lock file to say by whom. "
                        + "It may be open in another program, or the lock file could not be created in this folder.";
                case LockStatus.PossiblyStale:
                    return "A lock file exists but the document is not held open. "
                        + "It is probably left over from a crash, unless the document is co-authored through OneDrive or SharePoint.";
                case LockStatus.OrphanLockFile:
                    return "This lock file does not match any document in its folder. "
                        + "The document may have been renamed, moved or deleted.";
                case LockStatus.Error:
                    return "The item could not be checked.";
                default:
                    return "No lock file was found and the document is not open.";
            }
        }
    }
}
