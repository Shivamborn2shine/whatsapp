using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Text;
using ScanDrop.Native.Core;
using ScanDrop.Native.Storage;

namespace ScanDrop.Native.Conversion
{
    public sealed class ConversionResult
    {
        public bool Success;
        public string OutputPath;
        public string OutputFilename;
        public long OutputSizeBytes;
        public string EngineUsed;
        public string ErrorMessage;
    }

    /// <summary>
    /// Local document & image conversion engine (PRD Sec 7):
    /// 1. Native C# GDI+ Image-to-PDF converter (single or multiple JPG/PNG/BMP images -> 1 PDF) with zero dependencies.
    /// 2. Isolated Headless LibreOffice worker for Office documents (DOCX/DOC/XLSX/XLS/PPTX/PPT/TXT -> PDF)
    ///    running with isolated temporary profile, macros disabled, and strict 45-second timeout.
    /// </summary>
    public sealed class ConversionEngine
    {
        private readonly StoragePaths _paths;
        private readonly AppLogger _logger;

        public ConversionEngine(StoragePaths paths, AppLogger logger)
        {
            _paths = paths;
            _logger = logger;
        }

        public static string FindLibreOfficePath()
        {
            string[] candidates = new[]
            {
                @"C:\Program Files\LibreOffice\program\soffice.exe",
                @"C:\Program Files (x86)\LibreOffice\program\soffice.exe",
                @"C:\Program Files\LibreOffice\program\soffice.com"
            };
            foreach (var c in candidates)
            {
                if (File.Exists(c)) return c;
            }
            try
            {
                string pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
                foreach (var dir in pathEnv.Split(';'))
                {
                    if (string.IsNullOrWhiteSpace(dir)) continue;
                    string candidate = Path.Combine(dir.Trim(), "soffice.exe");
                    if (File.Exists(candidate)) return candidate;
                }
            }
            catch { }
            return null;
        }

        public static bool IsLibreOfficeInstalled()
        {
            return !string.IsNullOrEmpty(FindLibreOfficePath());
        }

        public ConversionResult ConvertFileToPdf(FileRecord file)
        {
            if (file == null || string.IsNullOrEmpty(file.StoragePath) || !File.Exists(file.StoragePath))
                return Fail("Source file not found on disk.");

            string ext = (file.FileExtension ?? Path.GetExtension(file.StoragePath)).ToLowerInvariant();
            string baseName = Path.GetFileNameWithoutExtension(StorageManager.SanitizeDisplayName(file.OriginalFilename));
            string outName = baseName + "_" + DateTime.UtcNow.ToString("HHmmss", CultureInfo.InvariantCulture) + ".pdf";
            string outPath = Path.Combine(_paths.ConvertedDir, outName);

            if (ext == ".pdf")
            {
                File.Copy(file.StoragePath, outPath, true);
                return new ConversionResult
                {
                    Success = true,
                    OutputPath = outPath,
                    OutputFilename = outName,
                    OutputSizeBytes = new FileInfo(outPath).Length,
                    EngineUsed = "passthrough"
                };
            }

            if (ext == ".jpg" || ext == ".jpeg" || ext == ".png" || ext == ".bmp")
            {
                return ConvertImagesToPdf(new List<string> { file.StoragePath }, outName, "A4", "portrait");
            }

            if (ext == ".docx" || ext == ".doc" || ext == ".xlsx" || ext == ".xls" || ext == ".pptx" || ext == ".ppt" || ext == ".txt")
            {
                return ConvertOfficeWithLibreOffice(file.StoragePath, outName);
            }

            return Fail("Unsupported format for local PDF conversion: " + ext);
        }

        /// <summary>
        /// Converts one or multiple images (JPG/PNG/BMP) into a single clean PDF 1.4 file using built-in Windows GDI+.
        /// </summary>
        public ConversionResult ConvertImagesToPdf(List<string> imagePaths, string outputFilename, string paperSize = "A4", string orientation = "portrait")
        {
            if (imagePaths == null || imagePaths.Count == 0)
                return Fail("No image files provided.");

            if (!Directory.Exists(_paths.ConvertedDir)) Directory.CreateDirectory(_paths.ConvertedDir);
            string safeOutName = StorageManager.SanitizeDisplayName(outputFilename);
            if (!safeOutName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) safeOutName += ".pdf";
            string outPath = Path.Combine(_paths.ConvertedDir, safeOutName);

            double pageW = 595.28; // A4 pt
            double pageH = 841.89;
            if (string.Equals(paperSize, "Letter", StringComparison.OrdinalIgnoreCase)) { pageW = 612; pageH = 792; }
            else if (string.Equals(paperSize, "Legal", StringComparison.OrdinalIgnoreCase)) { pageW = 612; pageH = 1008; }
            else if (string.Equals(paperSize, "A5", StringComparison.OrdinalIgnoreCase)) { pageW = 419.53; pageH = 595.28; }
            else if (string.Equals(paperSize, "Photo4x6", StringComparison.OrdinalIgnoreCase)) { pageW = 288; pageH = 432; }

            if (string.Equals(orientation, "landscape", StringComparison.OrdinalIgnoreCase))
            {
                double tmp = pageW; pageW = pageH; pageH = tmp;
            }

            try
            {
                var pages = new List<ImagePageData>();
                foreach (var p in imagePaths)
                {
                    if (!File.Exists(p)) return Fail("Image not found: " + p);
                    using (var img = Image.FromFile(p))
                    using (var bmp = new Bitmap(img.Width, img.Height, PixelFormat.Format24bppRgb))
                    {
                        using (var g = Graphics.FromImage(bmp))
                        {
                            g.Clear(Color.White);
                            g.DrawImage(img, 0, 0, img.Width, img.Height);
                        }
                        using (var ms = new MemoryStream())
                        {
                            var jpegCodec = GetJpegEncoder();
                            if (jpegCodec != null)
                            {
                                var encParams = new EncoderParameters(1);
                                encParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 88L);
                                bmp.Save(ms, jpegCodec, encParams);
                            }
                            else
                            {
                                bmp.Save(ms, ImageFormat.Jpeg);
                            }
                            pages.Add(new ImagePageData
                            {
                                PixelWidth = bmp.Width,
                                PixelHeight = bmp.Height,
                                JpegBytes = ms.ToArray()
                            });
                        }
                    }
                }

                WriteMultiPageImagePdf(outPath, pages, pageW, pageH);
                _logger.Info("Conversion", "Converted " + pages.Count + " image(s) -> " + safeOutName);
                return new ConversionResult
                {
                    Success = true,
                    OutputPath = outPath,
                    OutputFilename = safeOutName,
                    OutputSizeBytes = new FileInfo(outPath).Length,
                    EngineUsed = "Native GDI+ PDF"
                };
            }
            catch (Exception ex)
            {
                _logger.Error("Conversion", "Image-to-PDF failed: " + ex.Message);
                return Fail("Image-to-PDF failed: " + ex.Message);
            }
        }

        private ConversionResult ConvertOfficeWithLibreOffice(string sourcePath, string desiredOutName)
        {
            string soffice = FindLibreOfficePath();
            if (string.IsNullOrEmpty(soffice))
            {
                return Fail("LibreOffice is not installed on this PC. Install free LibreOffice (https://www.libreoffice.org) to enable local Office-to-PDF conversion, or use Open in Default App.");
            }

            string workId = "conv_" + Guid.NewGuid().ToString("N");
            string isolatedProfile = Path.Combine(_paths.TempDir, workId + "_profile");
            string workOutDir = Path.Combine(_paths.TempDir, workId + "_out");
            Directory.CreateDirectory(isolatedProfile);
            Directory.CreateDirectory(workOutDir);

            try
            {
                string profileUri = new Uri(isolatedProfile).AbsoluteUri;
                string args = string.Format(
                    CultureInfo.InvariantCulture,
                    "--headless --nologo --nodefault --norestore --nofirststartwizard \"-env:UserInstallation={0}\" --convert-to pdf --outdir \"{1}\" \"{2}\"",
                    profileUri, workOutDir, sourcePath
                );

                var psi = new ProcessStartInfo
                {
                    FileName = soffice,
                    Arguments = args,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = workOutDir
                };

                using (var proc = Process.Start(psi))
                {
                    bool exited = proc.WaitForExit(45000); // Strict 45-second timeout (PRD Sec 7)
                    if (!exited)
                    {
                        try { proc.Kill(); } catch { }
                        return Fail("Document conversion timed out after 45 seconds.");
                    }

                    string[] pdfs = Directory.GetFiles(workOutDir, "*.pdf");
                    if (pdfs.Length == 0)
                    {
                        string stderr = "";
                        try { stderr = proc.StandardError.ReadToEnd(); } catch { }
                        return Fail("LibreOffice did not produce a PDF. " + stderr);
                    }

                    if (!Directory.Exists(_paths.ConvertedDir)) Directory.CreateDirectory(_paths.ConvertedDir);
                    string finalPath = Path.Combine(_paths.ConvertedDir, StorageManager.SanitizeDisplayName(desiredOutName));
                    File.Copy(pdfs[0], finalPath, true);
                    _logger.Info("Conversion", "LibreOffice converted " + Path.GetFileName(sourcePath) + " -> " + Path.GetFileName(finalPath));

                    return new ConversionResult
                    {
                        Success = true,
                        OutputPath = finalPath,
                        OutputFilename = Path.GetFileName(finalPath),
                        OutputSizeBytes = new FileInfo(finalPath).Length,
                        EngineUsed = "LibreOffice Headless"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.Error("Conversion", "LibreOffice worker error: " + ex.Message);
                return Fail("Office conversion error: " + ex.Message);
            }
            finally
            {
                try { if (Directory.Exists(isolatedProfile)) Directory.Delete(isolatedProfile, true); } catch { }
                try { if (Directory.Exists(workOutDir)) Directory.Delete(workOutDir, true); } catch { }
            }
        }

        private sealed class ImagePageData
        {
            public int PixelWidth;
            public int PixelHeight;
            public byte[] JpegBytes;
        }

        private static void WriteMultiPageImagePdf(string outPath, List<ImagePageData> pages, double pageW, double pageH)
        {
            // Minimal valid PDF 1.4 structure:
            // Obj 1: Catalog, Obj 2: Pages, then per page i: [3 + i*3] Page, [4 + i*3] Image XObject, [5 + i*3] Content stream
            var offsets = new List<long>();
            using (var fs = File.Create(outPath))
            {
                WriteAscii(fs, "%PDF-1.4\n%\xE2\xE3\xCF\xD3\n");

                // Object 1: Catalog
                offsets.Add(fs.Position);
                WriteAscii(fs, "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

                // Object 2: Pages
                var kids = new StringBuilder();
                for (int i = 0; i < pages.Count; i++)
                {
                    if (i > 0) kids.Append(' ');
                    kids.Append(3 + i * 3).Append(" 0 R");
                }
                offsets.Add(fs.Position);
                WriteAscii(fs, string.Format(CultureInfo.InvariantCulture,
                    "2 0 obj\n<< /Type /Pages /Kids [{0}] /Count {1} >>\nendobj\n", kids.ToString(), pages.Count));

                double margin = 20.0;
                double boxW = pageW - margin * 2.0;
                double boxH = pageH - margin * 2.0;

                for (int i = 0; i < pages.Count; i++)
                {
                    var p = pages[i];
                    int pageObj = 3 + i * 3;
                    int imgObj = 4 + i * 3;
                    int contentObj = 5 + i * 3;

                    double scale = Math.Min(boxW / p.PixelWidth, boxH / p.PixelHeight);
                    double drawW = p.PixelWidth * scale;
                    double drawH = p.PixelHeight * scale;
                    double x = margin + (boxW - drawW) / 2.0;
                    double y = margin + (boxH - drawH) / 2.0;

                    // Page object
                    offsets.Add(fs.Position);
                    WriteAscii(fs, string.Format(CultureInfo.InvariantCulture,
                        "{0} 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {1:F2} {2:F2}] /Resources << /XObject << /Im0 {3} 0 R >> >> /Contents {4} 0 R >>\nendobj\n",
                        pageObj, pageW, pageH, imgObj, contentObj));

                    // Image XObject
                    offsets.Add(fs.Position);
                    WriteAscii(fs, string.Format(CultureInfo.InvariantCulture,
                        "{0} 0 obj\n<< /Type /XObject /Subtype /Image /Width {1} /Height {2} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {3} >>\nstream\n",
                        imgObj, p.PixelWidth, p.PixelHeight, p.JpegBytes.Length));
                    fs.Write(p.JpegBytes, 0, p.JpegBytes.Length);
                    WriteAscii(fs, "\nendstream\nendobj\n");

                    // Content stream
                    string content = string.Format(CultureInfo.InvariantCulture,
                        "q\n{0:F2} 0 0 {1:F2} {2:F2} {3:F2} cm\n/Im0 Do\nQ\n", drawW, drawH, x, y);
                    byte[] cBytes = Encoding.ASCII.GetBytes(content);
                    offsets.Add(fs.Position);
                    WriteAscii(fs, string.Format(CultureInfo.InvariantCulture,
                        "{0} 0 obj\n<< /Length {1} >>\nstream\n", contentObj, cBytes.Length));
                    fs.Write(cBytes, 0, cBytes.Length);
                    WriteAscii(fs, "\nendstream\nendobj\n");
                }

                long xrefPos = fs.Position;
                WriteAscii(fs, string.Format(CultureInfo.InvariantCulture, "xref\n0 {0}\n0000000000 65535 f \n", offsets.Count + 1));
                foreach (var off in offsets)
                {
                    WriteAscii(fs, string.Format(CultureInfo.InvariantCulture, "{0:D10} 00000 n \n", off));
                }
                WriteAscii(fs, string.Format(CultureInfo.InvariantCulture,
                    "trailer\n<< /Size {0} /Root 1 0 R >>\nstartxref\n{1}\n%%EOF\n", offsets.Count + 1, xrefPos));
            }
        }

        private static void WriteAscii(Stream s, string text)
        {
            byte[] b = Encoding.ASCII.GetBytes(text);
            s.Write(b, 0, b.Length);
        }

        private static ImageCodecInfo GetJpegEncoder()
        {
            foreach (var c in ImageCodecInfo.GetImageEncoders())
            {
                if (c.FormatID == ImageFormat.Jpeg.Guid) return c;
            }
            return null;
        }

        private static ConversionResult Fail(string msg)
        {
            return new ConversionResult { Success = false, ErrorMessage = msg };
        }
    }
}
