using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using ScanDrop.Native.Core;
using ScanDrop.Native.Storage;

namespace ScanDrop.Native.Transfers
{
    public sealed class SessionFileDeclaration
    {
        public string ClientFileId;
        public string OriginalName;
        public string ContentType;
        public long FileSize;
        public int ChunkCount;
    }

    public sealed class SessionInitResponse
    {
        public bool AlreadyCompleted;
        public SubmissionRecord ExistingSubmission;
        public string SessionId;
        public string SubmissionId;
        public string UploadToken;
        public int ChunkSizeBytes;
        public string ExpiresAt;
        public List<Dictionary<string, object>> Files = new List<Dictionary<string, object>>();
    }

    /// <summary>
    /// Coordinates resumable chunked uploads, streaming assembly without RAM bloat,
    /// magic-byte validation, SHA-256 verification, atomic commits, and 6-char receipt generation.
    /// </summary>
    public sealed class TransferService
    {
        public const int MaxChunkBytes = 8 * 1024 * 1024; // Hard upper cap 8 MiB per PRD Sec 8.5

        private readonly StorageManager _storage;
        private readonly SqliteDatabase _db;
        private readonly UploadSecurityGuard _guard;
        private readonly AppLogger _logger;
        private readonly object _sessionMetaLock = new object();
        private readonly Dictionary<string, Dictionary<string, SessionFileDeclaration>> _declaredFiles =
            new Dictionary<string, Dictionary<string, SessionFileDeclaration>>(StringComparer.OrdinalIgnoreCase);

        public event Action<SubmissionRecord> OnSubmissionReceived;

        public TransferService(StorageManager storage, SqliteDatabase db, UploadSecurityGuard guard, AppLogger logger)
        {
            _storage = storage;
            _db = db;
            _guard = guard;
            _logger = logger;
        }

        public SessionInitResponse CreateUploadSession(
            string clientIp,
            string customerName,
            string customerNote,
            string idempotencyKey,
            List<SessionFileDeclaration> files,
            out int httpStatus,
            out string error)
        {
            httpStatus = 200;
            error = null;

            var settings = _db.LoadSettings(_storage.Paths.DataRootDir);
            if (settings.PauseReceiving)
            {
                httpStatus = 503;
                error = "This shop is temporarily paused and not accepting new uploads right now.";
                return null;
            }

            if (!_guard.CheckRateLimit(clientIp, true, out error))
            {
                httpStatus = 429;
                return null;
            }

            // Duplicate submission protection via idempotencyKey (PRD Sec 4 Step 4 & Sec 19 #13)
            if (!string.IsNullOrWhiteSpace(idempotencyKey))
            {
                var existing = _db.GetSubmissionByIdempotencyKey(idempotencyKey.Trim());
                if (existing != null && existing.Status != "uploading" && existing.Status != "failed")
                {
                    return new SessionInitResponse
                    {
                        AlreadyCompleted = true,
                        ExistingSubmission = existing
                    };
                }
            }

            if (files == null || files.Count == 0)
            {
                httpStatus = 400;
                error = "Please select at least one file to upload.";
                return null;
            }

            if (files.Count > 50)
            {
                httpStatus = 400;
                error = "Maximum 50 files allowed per submission.";
                return null;
            }

            long totalExpected = 0;
            foreach (var f in files)
            {
                var v = FileValidator.ValidateMetadata(f.OriginalName, f.FileSize, settings);
                if (!v.IsValid)
                {
                    httpStatus = 400;
                    error = v.ErrorMessage;
                    return null;
                }
                totalExpected += f.FileSize;
            }

            if (totalExpected > settings.MaxSubmissionBytes)
            {
                httpStatus = 413;
                error = string.Format(CultureInfo.InvariantCulture,
                    "Total upload size ({0:F1} MB) exceeds the shop limit ({1} MB).",
                    totalExpected / (1024.0 * 1024.0), settings.MaxSubmissionBytes / (1024 * 1024));
                return null;
            }

            long availBytes;
            if (!_storage.HasEnoughDiskSpace(totalExpected * 2, settings.MinFreeDiskReserveBytes, out availBytes))
            {
                httpStatus = 507;
                error = "Shop computer is low on disk space and cannot accept this upload right now.";
                _logger.Warn("Storage", "Rejected upload due to insufficient disk space. Free=" + availBytes + ", Incoming=" + totalExpected);
                return null;
            }

            string nowIso = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            string subExpIso = DateTime.UtcNow.AddHours(settings.RetentionHours).ToString("o", CultureInfo.InvariantCulture);
            string sessExpIso = DateTime.UtcNow.AddMinutes(30).ToString("o", CultureInfo.InvariantCulture);

            string submissionId = "sub_" + Guid.NewGuid().ToString("N");
            string sessionId = "ses_" + Guid.NewGuid().ToString("N");
            string receiptCode = GenerateUniqueReceiptCode();
            string rawToken = SecretProtector.GenerateSecureToken(32);
            string tokenHash = SecretProtector.ComputeSha256Hex(rawToken);

            var shop = _db.GetPrimaryShop();
            var subRecord = new SubmissionRecord
            {
                SubmissionId = submissionId,
                ShopId = shop.ShopId,
                ReceiptCode = receiptCode,
                CustomerName = string.IsNullOrWhiteSpace(customerName) ? "Anonymous" : customerName.Trim(),
                CustomerNote = (customerNote ?? "").Trim(),
                Status = "uploading",
                IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim(),
                CreatedAt = nowIso,
                ExpiresAt = subExpIso,
                TotalSize = totalExpected
            };

            var sessRecord = new UploadSessionRecord
            {
                SessionId = sessionId,
                SubmissionId = submissionId,
                Status = "open",
                AuthorizedUploadTokenHash = tokenHash,
                ClientIp = clientIp ?? "",
                BytesReceived = 0,
                ExpectedTotalBytes = totalExpected,
                CreatedAt = nowIso,
                ExpiresAt = sessExpIso
            };

            _db.RunInTransaction(() =>
            {
                _db.InsertSubmission(subRecord);
                _db.InsertUploadSession(sessRecord);
            });

            string sessionTempDir = Path.Combine(_storage.Paths.TempDir, sessionId);
            Directory.CreateDirectory(sessionTempDir);

            var resp = new SessionInitResponse
            {
                SessionId = sessionId,
                SubmissionId = submissionId,
                UploadToken = rawToken,
                ChunkSizeBytes = Math.Min(settings.ChunkSizeBytes, MaxChunkBytes),
                ExpiresAt = sessExpIso
            };

            var declMap = new Dictionary<string, SessionFileDeclaration>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < files.Count; i++)
            {
                var f = files[i];
                string serverFileId = "f_" + Guid.NewGuid().ToString("N").Substring(0, 16);
                var decl = new SessionFileDeclaration
                {
                    ClientFileId = string.IsNullOrWhiteSpace(f.ClientFileId) ? ("c_" + i) : f.ClientFileId,
                    OriginalName = StorageManager.SanitizeDisplayName(f.OriginalName),
                    ContentType = string.IsNullOrWhiteSpace(f.ContentType) ? "application/octet-stream" : f.ContentType,
                    FileSize = f.FileSize,
                    ChunkCount = f.ChunkCount > 0 ? f.ChunkCount : (int)Math.Max(1, (f.FileSize + resp.ChunkSizeBytes - 1) / resp.ChunkSizeBytes)
                };
                declMap[serverFileId] = decl;
                resp.Files.Add(new Dictionary<string, object>
                {
                    { "clientFileId", decl.ClientFileId },
                    { "fileId", serverFileId },
                    { "originalName", decl.OriginalName },
                    { "chunkCount", decl.ChunkCount }
                });
            }

            lock (_sessionMetaLock)
            {
                _declaredFiles[sessionId] = declMap;
            }

            return resp;
        }

        public bool SaveChunk(
            string clientIp,
            string sessionId,
            string uploadToken,
            string fileId,
            int chunkIndex,
            long byteOffset,
            Stream chunkStream,
            long contentLengthHint,
            string expectedChunkSha256,
            out int httpStatus,
            out string error)
        {
            httpStatus = 200;
            error = null;

            if (!_guard.CheckRateLimit(clientIp, false, out error))
            {
                httpStatus = 429;
                return false;
            }

            var session = _guard.ValidateSessionToken(sessionId, uploadToken, out error);
            if (session == null)
            {
                httpStatus = 401;
                return false;
            }

            if (string.IsNullOrWhiteSpace(fileId) || chunkIndex < 0 || byteOffset < 0)
            {
                httpStatus = 400;
                error = "Invalid chunk parameters.";
                return false;
            }

            string cleanFileId = StorageManager.SanitizeDisplayName(fileId);
            string fileTempDir = StoragePaths.EnsureSafeSubPath(
                _storage.Paths.TempDir,
                Path.Combine(_storage.Paths.TempDir, session.SessionId, cleanFileId));
            Directory.CreateDirectory(fileTempDir);

            string partPath = StoragePaths.EnsureSafeSubPath(
                fileTempDir,
                Path.Combine(fileTempDir, string.Format(CultureInfo.InvariantCulture, "part_{0:D6}", chunkIndex)));

            long written = 0;
            string actualSha;
            byte[] buf = new byte[65536];
            try
            {
                using (var fs = File.Create(partPath))
                using (var sha = SHA256.Create())
                {
                    int r;
                    while ((r = chunkStream.Read(buf, 0, buf.Length)) > 0)
                    {
                        written += r;
                        if (written > MaxChunkBytes)
                        {
                            fs.Close();
                            try { File.Delete(partPath); } catch { }
                            httpStatus = 413;
                            error = "Chunk exceeds 8 MiB maximum chunk size limit.";
                            return false;
                        }
                        fs.Write(buf, 0, r);
                        sha.TransformBlock(buf, 0, r, null, 0);
                    }
                    sha.TransformFinalBlock(new byte[0], 0, 0);
                    actualSha = BitConverter.ToString(sha.Hash).Replace("-", "").ToLowerInvariant();
                }
            }
            catch (Exception ex)
            {
                try { if (File.Exists(partPath)) File.Delete(partPath); } catch { }
                httpStatus = 500;
                error = "Failed writing chunk: " + ex.Message;
                return false;
            }

            if (!string.IsNullOrWhiteSpace(expectedChunkSha256) &&
                !string.Equals(actualSha, expectedChunkSha256.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                try { File.Delete(partPath); } catch { }
                httpStatus = 400;
                error = "Chunk checksum mismatch (corrupted transfer). Please retry chunk.";
                return false;
            }

            var settings = _db.LoadSettings(_storage.Paths.DataRootDir);
            _db.UpsertUploadPart(new UploadPartRecord
            {
                SessionId = session.SessionId,
                FileId = cleanFileId,
                ChunkIndex = chunkIndex,
                ByteOffset = byteOffset,
                ChunkSize = written,
                Checksum = actualSha,
                CompletedAt = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)
            });

            long totalReceived = _db.GetSessionTotalPartBytes(session.SessionId);
            if (totalReceived > settings.MaxSubmissionBytes)
            {
                try { File.Delete(partPath); } catch { }
                httpStatus = 413;
                error = "Total submission size exceeded maximum limit.";
                return false;
            }

            _db.UpdateUploadSession(session.SessionId, "open", totalReceived);
            return true;
        }

        public SubmissionRecord CompleteUploadSession(
            string clientIp,
            string sessionId,
            string uploadToken,
            out int httpStatus,
            out string error)
        {
            httpStatus = 200;
            error = null;

            var session = _db.GetUploadSession(sessionId);
            if (session != null && session.Status == "completed")
            {
                var existing = _db.GetSubmissionByReceiptOrId(session.SubmissionId);
                if (existing != null) return existing;
            }

            session = _guard.ValidateSessionToken(sessionId, uploadToken, out error);
            if (session == null)
            {
                httpStatus = 401;
                return null;
            }

            var settings = _db.LoadSettings(_storage.Paths.DataRootDir);
            var submission = _db.GetSubmissionByReceiptOrId(session.SubmissionId);
            if (submission == null)
            {
                httpStatus = 404;
                error = "Submission not found.";
                return null;
            }

            Dictionary<string, SessionFileDeclaration> declMap = null;
            lock (_sessionMetaLock)
            {
                _declaredFiles.TryGetValue(session.SessionId, out declMap);
            }
            if (declMap == null || declMap.Count == 0)
            {
                httpStatus = 400;
                error = "Session file manifest missing (server may have restarted during upload). Please resend.";
                return null;
            }

            _db.UpdateUploadSession(session.SessionId, "committing", session.BytesReceived);

            string sessionTempDir = Path.Combine(_storage.Paths.TempDir, session.SessionId);
            string receivedSubDir = Path.Combine(_storage.Paths.ReceivedDir, submission.SubmissionId);
            Directory.CreateDirectory(receivedSubDir);

            var stagedPaths = new List<string>();
            var finalizedFiles = new List<FileRecord>();
            long grandTotalBytes = 0;
            string nowIso = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);

            try
            {
                byte[] copyBuf = new byte[1024 * 1024]; // 1 MB streaming buffer
                foreach (var kv in declMap)
                {
                    string fileId = kv.Key;
                    var decl = kv.Value;
                    var parts = _db.GetUploadParts(session.SessionId, fileId);
                    if (parts.Count == 0)
                    {
                        throw new InvalidOperationException("No uploaded chunks found for " + decl.OriginalName);
                    }

                    string ext = Path.GetExtension(decl.OriginalName).ToLowerInvariant();
                    string internalName = _storage.GenerateInternalFilename(ext);
                    string assembledTempPath = Path.Combine(sessionTempDir, "assembled_" + internalName);
                    stagedPaths.Add(assembledTempPath);

                    long assembledLen = 0;
                    string fileShaHex;
                    using (var outFs = File.Create(assembledTempPath))
                    using (var sha = SHA256.Create())
                    {
                        foreach (var p in parts)
                        {
                            string partPath = Path.Combine(sessionTempDir, fileId, string.Format(CultureInfo.InvariantCulture, "part_{0:D6}", p.ChunkIndex));
                            if (!File.Exists(partPath))
                                throw new FileNotFoundException("Missing chunk " + p.ChunkIndex + " for " + decl.OriginalName);
                            using (var inFs = File.OpenRead(partPath))
                            {
                                int read;
                                while ((read = inFs.Read(copyBuf, 0, copyBuf.Length)) > 0)
                                {
                                    outFs.Write(copyBuf, 0, read);
                                    sha.TransformBlock(copyBuf, 0, read, null, 0);
                                    assembledLen += read;
                                    if (assembledLen > settings.MaxFileSizeBytes)
                                        throw new InvalidOperationException("File '" + decl.OriginalName + "' exceeded max file size.");
                                }
                            }
                        }
                        sha.TransformFinalBlock(new byte[0], 0, 0);
                        fileShaHex = BitConverter.ToString(sha.Hash).Replace("-", "").ToLowerInvariant();
                    }

                    // Server-side magic-byte & content signature validation (PRD Sec 11 #4)
                    var contentCheck = FileValidator.ValidateFileContent(assembledTempPath, ext);
                    if (!contentCheck.IsValid)
                    {
                        // Move rejected artifact to quarantine briefly then fail cleanly
                        try
                        {
                            string qPath = Path.Combine(_storage.Paths.QuarantineDir, "rejected_" + internalName);
                            File.Move(assembledTempPath, qPath);
                        }
                        catch { }
                        throw new InvalidDataException(contentCheck.ErrorMessage);
                    }

                    string finalPath = StoragePaths.EnsureSafeSubPath(
                        _storage.Paths.ReceivedDir,
                        Path.Combine(receivedSubDir, internalName));

                    File.Move(assembledTempPath, finalPath);
                    grandTotalBytes += assembledLen;

                    finalizedFiles.Add(new FileRecord
                    {
                        FileId = fileId,
                        SubmissionId = submission.SubmissionId,
                        OriginalFilename = decl.OriginalName,
                        InternalFilename = internalName,
                        ContentType = string.IsNullOrWhiteSpace(decl.ContentType) ? contentCheck.DetectedType : decl.ContentType,
                        FileExtension = ext,
                        FileSize = assembledLen,
                        Checksum = fileShaHex,
                        StoragePath = finalPath,
                        ConversionStatus = "none",
                        ReceivedAt = nowIso
                    });
                }

                // Atomic commit in SQLite: only NOW is submission marked "received" (PRD Sec 4 Step 5)
                _db.RunInTransaction(() =>
                {
                    foreach (var fr in finalizedFiles) _db.InsertFile(fr);
                    _db.UpdateSubmissionStatus(submission.SubmissionId, "received", grandTotalBytes, nowIso);
                    _db.UpdateUploadSession(session.SessionId, "completed", grandTotalBytes);
                    _db.AddActivity("SUBMISSION_RECEIVED", "customer", submission.SubmissionId,
                        string.Format(CultureInfo.InvariantCulture, "Received {0} ({1} file(s), {2:F2} MB) from {3}",
                            submission.ReceiptCode, finalizedFiles.Count, grandTotalBytes / (1024.0 * 1024.0), submission.CustomerName));
                });

                submission.Status = "received";
                submission.CompletedAt = nowIso;
                submission.TotalSize = grandTotalBytes;
                submission.Files = finalizedFiles;

                CleanupTempSession(session.SessionId);
                _logger.Info("Transfer", string.Format(CultureInfo.InvariantCulture,
                    "Committed submission {0} ({1} file(s), {2:F2} MB)",
                    submission.ReceiptCode, finalizedFiles.Count, grandTotalBytes / (1024.0 * 1024.0)));

                FireSubmissionReceived(submission);
                return submission;
            }
            catch (InvalidDataException dex)
            {
                RollbackFailedCommit(submission.SubmissionId, session.SessionId, receivedSubDir);
                httpStatus = 400;
                error = dex.Message;
                return null;
            }
            catch (Exception ex)
            {
                RollbackFailedCommit(submission.SubmissionId, session.SessionId, receivedSubDir);
                httpStatus = 500;
                error = ex.Message;
                return null;
            }
        }

        /// <summary>
        /// Backwards-compatible single-shot direct upload (routes through exact same validation,
        /// disk-space checks, safe GUID storage, and SQLite atomic commit).
        /// </summary>
        public SubmissionRecord HandleSingleShotUpload(
            string clientIp,
            string customerName,
            string customerNote,
            string idempotencyKey,
            List<KeyValuePair<string, byte[]>> rawFiles,
            Dictionary<string, string> contentTypes,
            out int httpStatus,
            out string error)
        {
            var decls = new List<SessionFileDeclaration>();
            for (int i = 0; i < rawFiles.Count; i++)
            {
                string fname = rawFiles[i].Key;
                byte[] bytes = rawFiles[i].Value;
                string ct = contentTypes != null && contentTypes.ContainsKey(fname) ? contentTypes[fname] : "application/octet-stream";
                decls.Add(new SessionFileDeclaration
                {
                    ClientFileId = "f" + i,
                    OriginalName = fname,
                    ContentType = ct,
                    FileSize = bytes.Length,
                    ChunkCount = 1
                });
            }

            var init = CreateUploadSession(clientIp, customerName, customerNote, idempotencyKey, decls, out httpStatus, out error);
            if (init == null) return null;
            if (init.AlreadyCompleted) return init.ExistingSubmission;

            for (int i = 0; i < rawFiles.Count; i++)
            {
                string serverFileId = Convert.ToString(init.Files[i]["fileId"]);
                byte[] bytes = rawFiles[i].Value;
                int chunkSize = init.ChunkSizeBytes;
                int offset = 0;
                int cIdx = 0;
                while (offset < bytes.Length)
                {
                    int len = Math.Min(chunkSize, bytes.Length - offset);
                    using (var ms = new MemoryStream(bytes, offset, len, false))
                    {
                        if (!SaveChunk(clientIp, init.SessionId, init.UploadToken, serverFileId, cIdx, offset, ms, len, null, out httpStatus, out error))
                            return null;
                    }
                    offset += len;
                    cIdx++;
                }
            }

            return CompleteUploadSession(clientIp, init.SessionId, init.UploadToken, out httpStatus, out error);
        }

        private void RollbackFailedCommit(string submissionId, string sessionId, string receivedSubDir)
        {
            try { if (Directory.Exists(receivedSubDir)) Directory.Delete(receivedSubDir, true); } catch { }
            try { _db.UpdateUploadSession(sessionId, "failed", 0); } catch { }
            try { _db.UpdateSubmissionStatus(submissionId, "failed"); } catch { }
            CleanupTempSession(sessionId);
        }

        private void CleanupTempSession(string sessionId)
        {
            lock (_sessionMetaLock) { _declaredFiles.Remove(sessionId); }
            string dir = Path.Combine(_storage.Paths.TempDir, sessionId);
            if (Directory.Exists(dir)) try { Directory.Delete(dir, true); } catch { }
        }

        private string GenerateUniqueReceiptCode()
        {
            for (int attempt = 0; attempt < 20; attempt++)
            {
                string code = SecretProtector.GenerateReceiptCode();
                if (!_db.ReceiptCodeExists(code)) return code;
            }
            return Guid.NewGuid().ToString("N").Substring(0, 6).ToUpperInvariant();
        }

        private void FireSubmissionReceived(SubmissionRecord sub)
        {
            var h = OnSubmissionReceived;
            if (h != null) try { h(sub); } catch { }
        }
    }
}
