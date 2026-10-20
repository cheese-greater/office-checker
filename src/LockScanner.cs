using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Threading;

namespace OfficeLockChecker
{
    internal sealed class LockScanner
    {
        public static readonly string[] OfficeExtensions =
        {
            ".doc", ".docx", ".docm", ".dot", ".dotx", ".dotm", ".rtf", ".odt",
            ".xls", ".xlsx", ".xlsm", ".xlsb", ".xlt", ".xltx", ".xltm", ".csv", ".ods",
            ".ppt", ".pptx", ".pptm", ".pps", ".ppsx", ".ppsm", ".pot", ".potx", ".potm", ".odp"
        };

        private static readonly HashSet<string> OfficeExtensionSet = new HashSet<string>(OfficeExtensions, StringComparer.OrdinalIgnoreCase);

        // Excel and PowerPoint name the lock file ~$ plus the full name; Word replaces up to the first two characters.
        private static readonly HashSet<string> PrependOnlyExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".xls", ".xlsx", ".xlsm", ".xlsb", ".xlt", ".xltx", ".xltm", ".csv", ".ods",
            ".ppt", ".pptx", ".pptm", ".pps", ".ppsx", ".ppsm", ".pot", ".potx", ".potm", ".odp"
        };

        private const int MaximumReplacedCharacters = 2;
        private const int ErrorSharingViolation = 32;
        private const int ErrorLockViolation = 33;
        // FILE_ATTRIBUTE_RECALL_ON_OPEN and FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS mark cloud placeholder files.
        private const FileAttributes RecallOnOpen = (FileAttributes)0x00040000;
        private const FileAttributes RecallOnDataAccess = (FileAttributes)0x00400000;
        private const FileAttributes CloudPlaceholderAttributes = FileAttributes.Offline | RecallOnOpen | RecallOnDataAccess;
        private const FileAttributes HiddenSystemAttributes = FileAttributes.Hidden | FileAttributes.System;

        private readonly CancellationToken cancellationToken;
        private readonly IProgress<string> progress;
        private readonly string currentAccount = WindowsIdentity.GetCurrent().Name;

        public LockScanner(CancellationToken cancellationToken, IProgress<string> progress)
        {
            this.cancellationToken = cancellationToken;
            this.progress = progress;
        }

        public List<CheckResult> Scan(IEnumerable<string> inputPaths, bool includeSubfolders)
        {
            List<CheckResult> results = new List<CheckResult>();
            Dictionary<string, FolderRequest> requests = new Dictionary<string, FolderRequest>(StringComparer.OrdinalIgnoreCase);
            foreach (string inputPath in inputPaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string fullPath = NormalizePath(inputPath);
                if (fullPath == null)
                {
                    results.Add(CheckResult.ForError(inputPath, "Not a valid path."));
                }
                else if (Directory.Exists(fullPath))
                {
                    AddFolders(requests, fullPath, includeSubfolders, results);
                }
                else if (File.Exists(fullPath))
                {
                    GetRequest(requests, Path.GetDirectoryName(fullPath)).RequestedFiles.Add(fullPath);
                }
                else
                {
                    results.Add(CheckResult.ForError(fullPath, "File or folder not found."));
                }
            }
            foreach (FolderRequest request in requests.Values)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (progress != null)
                {
                    progress.Report(request.FolderPath);
                }
                ScanFolder(request, results);
            }
            return results;
        }

        private void AddFolders(Dictionary<string, FolderRequest> requests, string rootFolder, bool includeSubfolders, List<CheckResult> results)
        {
            Stack<string> pendingFolders = new Stack<string>();
            pendingFolders.Push(rootFolder);
            while (pendingFolders.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string folder = pendingFolders.Pop();
                GetRequest(requests, folder).IncludeWholeFolder = true;
                if (!includeSubfolders)
                {
                    continue;
                }
                try
                {
                    foreach (DirectoryInfo subfolder in new DirectoryInfo(folder).EnumerateDirectories())
                    {
                        // Junctions could loop forever, and hidden system folders such as $RECYCLE.BIN hold no documents.
                        bool isReparsePoint = (subfolder.Attributes & FileAttributes.ReparsePoint) != 0;
                        bool isHiddenSystem = (subfolder.Attributes & HiddenSystemAttributes) == HiddenSystemAttributes;
                        if (!isReparsePoint && !isHiddenSystem)
                        {
                            pendingFolders.Push(subfolder.FullName);
                        }
                    }
                }
                catch (UnauthorizedAccessException exception)
                {
                    results.Add(CheckResult.ForError(folder, exception.Message));
                }
                catch (IOException exception)
                {
                    results.Add(CheckResult.ForError(folder, exception.Message));
                }
            }
        }

        private void ScanFolder(FolderRequest request, List<CheckResult> results)
        {
            FileInfo[] files;
            try
            {
                files = new DirectoryInfo(request.FolderPath).GetFiles();
            }
            catch (UnauthorizedAccessException exception)
            {
                results.Add(CheckResult.ForError(request.FolderPath, exception.Message));
                return;
            }
            catch (IOException exception)
            {
                results.Add(CheckResult.ForError(request.FolderPath, exception.Message));
                return;
            }

            List<FileInfo> documents = new List<FileInfo>();
            List<LockFile> lockFiles = new List<LockFile>();
            foreach (FileInfo file in files)
            {
                LockFile lockFile = LockFileDecoder.TryDescribe(file);
                if (lockFile != null)
                {
                    lockFiles.Add(lockFile);
                }
                else
                {
                    documents.Add(file);
                }
            }

            Dictionary<string, OpenState> openStates = new Dictionary<string, OpenState>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, List<LockFile>> locksByDocument = new Dictionary<string, List<LockFile>>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, List<FileInfo>> documentsByLock = new Dictionary<string, List<FileInfo>>(StringComparer.OrdinalIgnoreCase);
            foreach (LockFile lockFile in lockFiles)
            {
                List<FileInfo> candidates = FindCandidateDocuments(lockFile, documents);
                if (candidates.Count > 1)
                {
                    // Several names fit this lock file; the one that is actually open is the one it belongs to.
                    List<FileInfo> openCandidates = candidates.Where(candidate => GetOpenState(candidate, openStates).IsOpen == true).ToList();
                    if (openCandidates.Count > 0)
                    {
                        candidates = openCandidates;
                    }
                }
                documentsByLock[lockFile.FullPath] = candidates;
                foreach (FileInfo candidate in candidates)
                {
                    List<LockFile> documentLocks;
                    if (!locksByDocument.TryGetValue(candidate.FullName, out documentLocks))
                    {
                        documentLocks = new List<LockFile>();
                        locksByDocument[candidate.FullName] = documentLocks;
                    }
                    documentLocks.Add(lockFile);
                }
            }

            List<FileInfo> reportedDocuments = new List<FileInfo>();
            List<LockFile> reportedOrphans = new List<LockFile>();
            if (request.IncludeWholeFolder)
            {
                reportedDocuments.AddRange(documents.Where(document =>
                    OfficeExtensionSet.Contains(document.Extension) || locksByDocument.ContainsKey(document.FullName)));
                reportedOrphans.AddRange(lockFiles.Where(lockFile => documentsByLock[lockFile.FullPath].Count == 0));
            }
            else
            {
                foreach (string requestedPath in request.RequestedFiles)
                {
                    LockFile requestedLock = lockFiles.FirstOrDefault(lockFile =>
                        string.Equals(lockFile.FullPath, requestedPath, StringComparison.OrdinalIgnoreCase));
                    FileInfo requestedDocument = documents.FirstOrDefault(document =>
                        string.Equals(document.FullName, requestedPath, StringComparison.OrdinalIgnoreCase));
                    if (requestedLock != null)
                    {
                        // A dropped lock file stands for the document it locks.
                        List<FileInfo> lockedDocuments = documentsByLock[requestedLock.FullPath];
                        if (lockedDocuments.Count == 0)
                        {
                            reportedOrphans.Add(requestedLock);
                        }
                        reportedDocuments.AddRange(lockedDocuments);
                    }
                    else if (requestedDocument != null)
                    {
                        reportedDocuments.Add(requestedDocument);
                    }
                    else
                    {
                        results.Add(CheckResult.ForError(requestedPath, "File not found."));
                    }
                }
            }

            HashSet<string> reportedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (FileInfo document in reportedDocuments)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (reportedPaths.Add(document.FullName))
                {
                    List<LockFile> documentLocks;
                    locksByDocument.TryGetValue(document.FullName, out documentLocks);
                    results.Add(BuildDocumentResult(document, documentLocks, GetOpenState(document, openStates)));
                }
            }
            foreach (LockFile orphan in reportedOrphans)
            {
                if (reportedPaths.Add(orphan.FullPath))
                {
                    results.Add(BuildOrphanResult(orphan));
                }
            }
        }

        private static List<FileInfo> FindCandidateDocuments(LockFile lockFile, List<FileInfo> documents)
        {
            string targetName = lockFile.TargetName;
            bool isFullName = lockFile.Kind == LockFileKind.LibreOffice || PrependOnlyExtensions.Contains(Path.GetExtension(targetName));
            int maximumReplaced = isFullName ? 0 : MaximumReplacedCharacters;
            return documents.Where(document =>
            {
                int replacedCharacters = document.Name.Length - targetName.Length;
                return replacedCharacters >= 0
                    && replacedCharacters <= maximumReplaced
                    && document.Name.EndsWith(targetName, StringComparison.OrdinalIgnoreCase);
            }).ToList();
        }

        private CheckResult BuildDocumentResult(FileInfo document, List<LockFile> documentLocks, OpenState openState)
        {
            CheckResult result = new CheckResult { ItemPath = document.FullName };
            LockFile newestLock = documentLocks == null ? null : documentLocks.OrderByDescending(lockFile => lockFile.LastWriteTime).First();
            if (newestLock != null)
            {
                ApplyLockFile(result, newestLock);
                result.Status = openState.IsOpen == false ? LockStatus.PossiblyStale : LockStatus.Locked;
                if (openState.IsOpen == null)
                {
                    result.Detail = "Could not confirm the document is open: " + openState.ErrorMessage;
                }
            }
            else if (openState.IsOpen == true)
            {
                result.Status = LockStatus.InUseWithoutLockFile;
            }
            else if (openState.IsOpen == false)
            {
                result.Status = LockStatus.NotLocked;
            }
            else
            {
                result.Status = LockStatus.Error;
                result.Detail = openState.ErrorMessage;
            }
            return result;
        }

        private CheckResult BuildOrphanResult(LockFile lockFile)
        {
            CheckResult result = new CheckResult { Status = LockStatus.OrphanLockFile, ItemPath = lockFile.FullPath };
            ApplyLockFile(result, lockFile);
            return result;
        }

        private void ApplyLockFile(CheckResult result, LockFile lockFile)
        {
            LockFileDecoder.Decode(lockFile);
            result.LockedBy = lockFile.UserName;
            result.LockedSince = lockFile.LastWriteTime;
            result.LockFileName = lockFile.FileName;
            bool isCurrentAccount = string.Equals(lockFile.WindowsAccount, currentAccount, StringComparison.OrdinalIgnoreCase);
            result.WindowsAccount = isCurrentAccount ? lockFile.WindowsAccount + " (you)" : lockFile.WindowsAccount;
        }

        private static OpenState GetOpenState(FileInfo document, Dictionary<string, OpenState> openStates)
        {
            OpenState openState;
            if (openStates.TryGetValue(document.FullName, out openState))
            {
                return openState;
            }
            openState = new OpenState();
            if ((document.Attributes & CloudPlaceholderAttributes) != 0)
            {
                // A cloud-only file cannot be open on this machine, and opening it could start a download.
                openState.IsOpen = false;
            }
            else
            {
                try
                {
                    // Office holds open documents with write access, so denying other writers fails while one is open.
                    using (new FileStream(document.FullName, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                    }
                    openState.IsOpen = false;
                }
                catch (IOException exception)
                {
                    int errorCode = exception.HResult & 0xFFFF;
                    if (errorCode == ErrorSharingViolation || errorCode == ErrorLockViolation)
                    {
                        openState.IsOpen = true;
                    }
                    else
                    {
                        openState.ErrorMessage = exception.Message;
                    }
                }
                catch (UnauthorizedAccessException exception)
                {
                    openState.ErrorMessage = exception.Message;
                }
            }
            openStates[document.FullName] = openState;
            return openState;
        }

        private static FolderRequest GetRequest(Dictionary<string, FolderRequest> requests, string folderPath)
        {
            FolderRequest request;
            if (!requests.TryGetValue(folderPath, out request))
            {
                request = new FolderRequest(folderPath);
                requests[folderPath] = request;
            }
            return request;
        }

        private static string NormalizePath(string inputPath)
        {
            try
            {
                string fullPath = Path.GetFullPath(inputPath);
                string rootPath = Path.GetPathRoot(fullPath);
                return fullPath.Length > rootPath.Length
                    ? fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    : fullPath;
            }
            catch (ArgumentException)
            {
                return null;
            }
            catch (NotSupportedException)
            {
                return null;
            }
            catch (IOException)
            {
                return null;
            }
        }

        private sealed class OpenState
        {
            public bool? IsOpen { get; set; }
            public string ErrorMessage { get; set; }
        }

        private sealed class FolderRequest
        {
            public FolderRequest(string folderPath)
            {
                FolderPath = folderPath;
                RequestedFiles = new List<string>();
            }

            public string FolderPath { get; private set; }
            public bool IncludeWholeFolder { get; set; }
            public List<string> RequestedFiles { get; private set; }
        }
    }
}
