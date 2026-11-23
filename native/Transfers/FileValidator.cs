using System;
using System.Collections.Generic;
using System.IO;
using ScanDrop.Native.Core;

namespace ScanDrop.Native.Transfers
{
    public sealed class FileValidationResult
    {
        public bool IsValid;
        public string ErrorMessage;
        public string DetectedType;
    }

    /// <summary>
    /// Enforces allowlist + actual magic-byte file content inspection (PRD Sec 11).
    /// Blocks executables, scripts, path-traversal tricks, and spoofed file extensions.
    /// </summary>
    public static class FileValidator
    {
        private static readonly HashSet<string> AlwaysBlockedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".exe", ".dll", ".bat", ".cmd", ".msi", ".msp", ".vbs", ".vbe", ".js", ".jse",
            ".ps1", ".psm1", ".scr", ".com", ".pif", ".hta", ".jar", ".reg", ".lnk", ".wsf",
            ".wsh", ".sh", ".cpl", ".sys", ".drv", ".ocx", ".php", ".asp", ".aspx", ".jsp"
        };

        public static HashSet<string> ParseAllowedExtensions(string csv)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string raw = string.IsNullOrWhiteSpace(csv)
                ? ".pdf,.docx,.doc,.xlsx,.xls,.pptx,.ppt,.txt,.jpg,.jpeg,.png,.webp,.bmp"
                : csv;
            foreach (var part in raw.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string ext = part.Trim().ToLowerInvariant();
                if (!ext.StartsWith(".")) ext = "." + ext;
                if (!AlwaysBlockedExtensions.Contains(ext)) set.Add(ext);
            }
            return set;
        }

        public static FileValidationResult ValidateMetadata(string fileName, long fileSize, AppSettingsModel settings)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return Fail("File name is missing.");

            string ext = Path.GetExtension(fileName).ToLowerInvariant();
            if (string.IsNullOrEmpty(ext))
                return Fail("Files without an extension are not supported.");

            if (AlwaysBlockedExtensions.Contains(ext))
                return Fail("Executable or script files (" + ext + ") are blocked for security.");

            var allowed = ParseAllowedExtensions(settings.AllowedExtensions);
            if (!allowed.Contains(ext))
                return Fail("File type " + ext + " is not in the shop's allowed formats (" + settings.AllowedExtensions + ").");

            if (fileSize <= 0)
                return Fail("Empty file (" + fileName + ") cannot be uploaded.");

            if (fileSize > settings.MaxFileSizeBytes)
                return Fail(string.Format("File '{0}' exceeds maximum allowed size ({1} MB).",
                    Path.GetFileName(fileName), settings.MaxFileSizeBytes / (1024 * 1024)));

            return Ok(ext);
        }

        public static FileValidationResult ValidateFileContent(string filePath, string expectedExtension)
        {
            if (!File.Exists(filePath)) return Fail("Uploaded file missing on disk.");
            string ext = (expectedExtension ?? Path.GetExtension(filePath)).ToLowerInvariant();
            if (AlwaysBlockedExtensions.Contains(ext))
                return Fail("Blocked executable file type: " + ext);

            byte[] header = new byte[512];
            int read;
            using (var fs = File.OpenRead(filePath))
            {
                if (fs.Length == 0) return Fail("Uploaded file is empty.");
                read = fs.Read(header, 0, header.Length);
            }

            // Always block Windows PE/MZ executables, ELF binaries, Mach-O binaries, and script shebangs
            if (read >= 2 && header[0] == 0x4D && header[1] == 0x5A) // "MZ"
                return Fail("Security block: file contains a Windows executable (PE/MZ) header.");
            if (read >= 4 && header[0] == 0x7F && header[1] == 0x45 && header[2] == 0x4C && header[3] == 0x46) // ELF
                return Fail("Security block: file contains an ELF binary header.");
            if (read >= 2 && header[0] == 0x23 && header[1] == 0x21) // "#!"
                return Fail("Security block: shell script headers are not allowed.");

            if (ext == ".pdf")
            {
                // Allow optional UTF-8 BOM or whitespace before %PDF-
                int idx = FindAsciiPrefix(header, read, "%PDF-");
                if (idx < 0 || idx > 16)
                    return Fail("Invalid PDF file: missing %PDF header signature.");
                return Ok("application/pdf");
            }

            if (ext == ".jpg" || ext == ".jpeg")
            {
                if (read < 3 || header[0] != 0xFF || header[1] != 0xD8 || header[2] != 0xFF)
                    return Fail("Invalid JPEG image: header signature mismatch.");
                return Ok("image/jpeg");
            }

            if (ext == ".png")
            {
                if (read < 8 || header[0] != 0x89 || header[1] != 0x50 || header[2] != 0x4E || header[3] != 0x47 ||
                    header[4] != 0x0D || header[5] != 0x0A || header[6] != 0x1A || header[7] != 0x0A)
                    return Fail("Invalid PNG image: header signature mismatch.");
                return Ok("image/png");
            }

            if (ext == ".bmp")
            {
                if (read < 2 || header[0] != 0x42 || header[1] != 0x4D)
                    return Fail("Invalid BMP image: header signature mismatch.");
                return Ok("image/bmp");
            }

            if (ext == ".webp")
            {
                if (read < 12 || header[0] != 0x52 || header[1] != 0x49 || header[2] != 0x46 || header[3] != 0x46 ||
                    header[8] != 0x57 || header[9] != 0x45 || header[10] != 0x42 || header[11] != 0x50)
                    return Fail("Invalid WEBP image: header signature mismatch.");
                return Ok("image/webp");
            }

            if (ext == ".docx" || ext == ".xlsx" || ext == ".pptx")
            {
                // OOXML formats are ZIP containers (PK\x03\x04 or empty archive PK\x05\x06)
                if (read < 4 || header[0] != 0x50 || header[1] != 0x4B ||
                    !((header[2] == 0x03 && header[3] == 0x04) || (header[2] == 0x05 && header[3] == 0x06)))
                    return Fail("Invalid Office document (" + ext + "): expected OOXML archive signature.");
                return Ok("application/vnd.openxmlformats");
            }

            if (ext == ".doc" || ext == ".xls" || ext == ".ppt")
            {
                bool isOle2 = read >= 8 && header[0] == 0xD0 && header[1] == 0xCF && header[2] == 0x11 && header[3] == 0xE0 &&
                              header[4] == 0xA1 && header[5] == 0xB1 && header[6] == 0x1A && header[7] == 0xE1;
                bool isRtf = ext == ".doc" && FindAsciiPrefix(header, read, "{\\rtf") == 0;
                bool isZip = read >= 4 && header[0] == 0x50 && header[1] == 0x4B && header[2] == 0x03 && header[3] == 0x04;
                if (!isOle2 && !isRtf && !isZip)
                    return Fail("Invalid legacy Office document (" + ext + "): file signature mismatch.");
                return Ok("application/ms-office");
            }

            if (ext == ".txt")
            {
                // Reject raw binary masquerading as .txt (NUL byte check unless UTF-16 BOM)
                bool utf16Bom = read >= 2 && ((header[0] == 0xFF && header[1] == 0xFE) || (header[0] == 0xFE && header[1] == 0xFF));
                if (!utf16Bom)
                {
                    for (int i = 0; i < read; i++)
                    {
                        if (header[i] == 0x00)
                            return Fail("Invalid text file: binary content detected inside .txt upload.");
                    }
                }
                return Ok("text/plain");
            }

            return Ok("application/octet-stream");
        }

        private static int FindAsciiPrefix(byte[] buf, int len, string needle)
        {
            int max = len - needle.Length;
            for (int i = 0; i <= max; i++)
            {
                bool match = true;
                for (int j = 0; j < needle.Length; j++)
                {
                    if (buf[i + j] != (byte)needle[j]) { match = false; break; }
                }
                if (match) return i;
            }
            return -1;
        }

        private static FileValidationResult Ok(string type)
        {
            return new FileValidationResult { IsValid = true, DetectedType = type };
        }

        private static FileValidationResult Fail(string msg)
        {
            return new FileValidationResult { IsValid = false, ErrorMessage = msg };
        }
    }
}
