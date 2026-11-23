using System;
using System.Collections.Generic;

namespace ScanDrop.Native.Core
{
    public sealed class ShopRecord
    {
        public string ShopId;
        public string ShopName;
        public string Slug;
        public string CreatedAt;
        public string SettingsJson;
    }

    public sealed class FileRecord
    {
        public string FileId;
        public string SubmissionId;
        public string OriginalFilename;
        public string InternalFilename;
        public string ContentType;
        public string FileExtension;
        public long FileSize;
        public string Checksum;
        public string StoragePath;
        public string ConversionStatus; // none, converting, converted, failed
        public string ConvertedPath;
        public string ReceivedAt;
    }

    public sealed class SubmissionRecord
    {
        public string SubmissionId;
        public string ShopId;
        public string ReceiptCode;
        public string CustomerName;
        public string CustomerNote;
        public string Status; // uploading, processing, received, failed, printed, archived, expired
        public string IdempotencyKey;
        public string CreatedAt;
        public string CompletedAt;
        public string ExpiresAt;
        public long TotalSize;
        public List<FileRecord> Files = new List<FileRecord>();
    }

    public sealed class UploadSessionRecord
    {
        public string SessionId;
        public string SubmissionId;
        public string Status; // open, committing, completed, expired, failed
        public string AuthorizedUploadTokenHash;
        public string ClientIp;
        public long BytesReceived;
        public long ExpectedTotalBytes;
        public string CreatedAt;
        public string ExpiresAt;
    }

    public sealed class UploadPartRecord
    {
        public string SessionId;
        public string FileId;
        public int ChunkIndex;
        public long ByteOffset;
        public long ChunkSize;
        public string Checksum;
        public string CompletedAt;
    }

    public sealed class ActivityLogRecord
    {
        public string ActivityId;
        public string ActivityType;
        public string Actor;
        public string RelatedSubmissionId;
        public string Timestamp;
        public string Details;
    }

    public sealed class AppSettingsModel
    {
        public string ShopId = "shop_quickprint_01";
        public string ShopName = "QuickPrint Xerox & Stationery";
        public string ShopSlug = "quickprint";
        public string PairingCode = "SD-PAIR-7890";
        public string StorageRoot = "";
        public int RetentionHours = 24;
        public long MaxFileSizeBytes = 100L * 1024L * 1024L;       // Default 100 MB per PRD Sec 16 (configurable up to 500 MB)
        public long MaxSubmissionBytes = 500L * 1024L * 1024L;     // 500 MB max per submission
        public int ChunkSizeBytes = 4 * 1024 * 1024;               // 4 MiB (< 8 MiB per PRD Sec 8.5)
        public long MinFreeDiskReserveBytes = 500L * 1024L * 1024L; // 500 MB safety buffer
        public bool PauseReceiving = false;
        public string AllowedExtensions = ".pdf,.docx,.doc,.xlsx,.xls,.pptx,.ppt,.txt,.jpg,.jpeg,.png,.webp,.bmp";
        public string Theme = "light"; // light | dark
        public bool FirstRunCompleted = false;
        public string PublicBaseUrl = "https://scandropdone.netlify.app";
    }

    public sealed class StorageSummary
    {
        public string StorageRoot;
        public string InboxDir;
        public long ActiveBytes;
        public int ActiveCount;
        public long CompletedBytes;
        public int CompletedCount;
        public int ExpiredCount;
        public long StagingBytes;
        public int StagingCount;
        public long ConvertedBytes;
        public int ConvertedCount;
        public long OrphanBytes;
        public int OrphanCount;
        public long DatabaseBytes;
        public long TotalUsedBytes;
        public long FreeDriveBytes;
    }

    public sealed class StorageClearResult
    {
        public string Scope;
        public long FreedBytes;
        public int DeletedSubmissions;
        public int RemovedTempItems;
        public StorageSummary Remaining;
    }
}
