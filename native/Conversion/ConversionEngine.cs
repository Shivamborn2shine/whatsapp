using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Xml.Linq;
using Microsoft.Win32;
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

        public static bool IsMicrosoftWordInstalled()
        {
            try
            {
                return Type.GetTypeFromProgID("Word.Application") != null;
            }
            catch { return false; }
        }

        public static bool IsMicrosoftExcelInstalled()
        {
            try
            {
                return Type.GetTypeFromProgID("Excel.Application") != null;
            }
            catch { return false; }
        }

        public static bool IsMicrosoftPowerPointInstalled()
        {
            try
            {
                return Type.GetTypeFromProgID("PowerPoint.Application") != null;
            }
            catch { return false; }
        }

        public static bool IsLibreOfficeInstalled()
        {
            return !string.IsNullOrEmpty(FindLibreOfficePath());
        }

        public static bool IsOfficeConversionAvailable()
        {
            return IsLibreOfficeInstalled() || IsMicrosoftWordInstalled() || IsMicrosoftExcelInstalled() || IsMicrosoftPowerPointInstalled();
        }

        public static string GetOfficeEngineLabel()
        {
            if (IsLibreOfficeInstalled()) return "LibreOffice Ready";
            bool hasWord = IsMicrosoftWordInstalled();
            bool hasExcel = IsMicrosoftExcelInstalled();
            if (hasWord && hasExcel) return "Microsoft Office Ready";
            if (hasWord) return "Word + Built-in XLSX Ready";
            if (hasExcel) return "Excel + Built-in DOCX Ready";
            return "Images, XLSX & DOCX Ready";
        }

        public ConversionResult ConvertFileToPdf(FileRecord file)
        {
            if (file == null || string.IsNullOrEmpty(file.StoragePath) || !File.Exists(file.StoragePath))
                return Fail("Source file not found on disk.");

            string baseName = Path.GetFileNameWithoutExtension(StorageManager.SanitizeDisplayName(file.OriginalFilename));
            return ConvertPathToPdf(file.StoragePath, baseName, file.FileExtension);
        }

        public ConversionResult ConvertPathToPdf(string sourcePath, string desiredBaseName, string explicitExt = null)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
                return Fail("Source file not found on disk.");

            string ext = (string.IsNullOrWhiteSpace(explicitExt) ? Path.GetExtension(sourcePath) : explicitExt).ToLowerInvariant();
            string baseName = StorageManager.SanitizeDisplayName(string.IsNullOrWhiteSpace(desiredBaseName) ? Path.GetFileNameWithoutExtension(sourcePath) : desiredBaseName);
            string outName = baseName + "_" + DateTime.UtcNow.ToString("HHmmss", CultureInfo.InvariantCulture) + ".pdf";
            if (!Directory.Exists(_paths.ConvertedDir)) Directory.CreateDirectory(_paths.ConvertedDir);
            string outPath = Path.Combine(_paths.ConvertedDir, outName);

            if (ext == ".pdf")
            {
                File.Copy(sourcePath, outPath, true);
                return new ConversionResult
                {
                    Success = true,
                    OutputPath = outPath,
                    OutputFilename = outName,
                    OutputSizeBytes = new FileInfo(outPath).Length,
                    EngineUsed = "passthrough"
                };
            }

            if (ext == ".jpg" || ext == ".jpeg" || ext == ".png" || ext == ".bmp" || ext == ".gif")
            {
                return ConvertImagesToPdf(new List<string> { sourcePath }, outName, "A4", "portrait");
            }

            if (ext == ".docx" || ext == ".doc" || ext == ".rtf" || ext == ".txt" ||
                ext == ".xlsx" || ext == ".xls" || ext == ".csv" ||
                ext == ".pptx" || ext == ".ppt")
            {
                if (IsLibreOfficeInstalled())
                {
                    var lo = ConvertOfficeWithLibreOffice(sourcePath, outName);
                    if (lo.Success) return lo;
                }
                if ((ext == ".docx" || ext == ".doc" || ext == ".rtf" || ext == ".txt") && IsMicrosoftWordInstalled())
                {
                    return ConvertWordDocumentWithCom(sourcePath, outName);
                }
                if (ext == ".xlsx" || ext == ".xls" || ext == ".csv")
                {
                    if (IsMicrosoftExcelInstalled())
                    {
                        var xlRes = ConvertExcelWorkbookWithCom(sourcePath, outName);
                        if (xlRes.Success) return xlRes;
                        _logger.Warn("Conversion", "Excel COM fallback triggered (" + xlRes.ErrorMessage + ")");
                        if (ext == ".xls") return xlRes;
                    }
                    if (ext == ".xlsx" || ext == ".csv")
                    {
                        return ConvertSpreadsheetNativeToPdf(sourcePath, ext, outName);
                    }
                    return Fail("Install Microsoft Excel or free LibreOffice to render legacy " + ext + " to PDF.");
                }
                if ((ext == ".pptx" || ext == ".ppt") && IsMicrosoftPowerPointInstalled())
                {
                    return ConvertPowerPointWithCom(sourcePath, outName);
                }
                return Fail("Install Microsoft Office or free LibreOffice to render " + ext + " to PDF.");
            }

            return Fail("Unsupported format for local PDF conversion: " + ext);
        }

        public bool ConvertDocToDocx(string sourceDocPath, string outDocxPath, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(sourceDocPath) || !File.Exists(sourceDocPath))
            {
                error = "Source .doc file not found.";
                return false;
            }
            if (IsMicrosoftWordInstalled())
            {
                bool ok = false;
                string err = null;
                var t = new Thread(() =>
                {
                    object wordApp = null;
                    object docs = null;
                    object doc = null;
                    try
                    {
                        Type wordType = Type.GetTypeFromProgID("Word.Application");
                        wordApp = Activator.CreateInstance(wordType);
                        wordType.InvokeMember("Visible", BindingFlags.SetProperty, null, wordApp, new object[] { false });
                        wordType.InvokeMember("DisplayAlerts", BindingFlags.SetProperty, null, wordApp, new object[] { 0 });
                        docs = wordType.InvokeMember("Documents", BindingFlags.GetProperty, null, wordApp, null);
                        doc = docs.GetType().InvokeMember("Open", BindingFlags.InvokeMethod, null, docs,
                            new object[] { Path.GetFullPath(sourceDocPath), false, true });
                        // 16 = wdFormatXMLDocument (.docx)
                        doc.GetType().InvokeMember("SaveAs2", BindingFlags.InvokeMethod, null, doc,
                            new object[] { Path.GetFullPath(outDocxPath), 16 });
                        doc.GetType().InvokeMember("Close", BindingFlags.InvokeMethod, null, doc, new object[] { false });
                        ok = File.Exists(outDocxPath);
                    }
                    catch (Exception ex) { err = ex.Message; }
                    finally
                    {
                        try { if (doc != null) Marshal.ReleaseComObject(doc); } catch { }
                        try { if (docs != null) Marshal.ReleaseComObject(docs); } catch { }
                        try { if (wordApp != null) { wordApp.GetType().InvokeMember("Quit", BindingFlags.InvokeMethod, null, wordApp, new object[] { false }); Marshal.ReleaseComObject(wordApp); } } catch { }
                    }
                });
                t.SetApartmentState(ApartmentState.STA);
                t.Start();
                if (!t.Join(35000)) { error = "Word .doc conversion timed out."; return false; }
                error = err;
                return ok;
            }
            error = "Microsoft Word is required to convert legacy .doc to .docx.";
            return false;
        }

        private ConversionResult ConvertWordDocumentWithCom(string sourcePath, string desiredOutName)
        {
            if (!Directory.Exists(_paths.ConvertedDir)) Directory.CreateDirectory(_paths.ConvertedDir);
            string finalPath = Path.Combine(_paths.ConvertedDir, StorageManager.SanitizeDisplayName(desiredOutName));
            string fullSrc = Path.GetFullPath(sourcePath);
            string fullOut = Path.GetFullPath(finalPath);

            ConversionResult result = null;
            var t = new Thread(() =>
            {
                object wordApp = null;
                object docs = null;
                object doc = null;
                try
                {
                    Type wordType = Type.GetTypeFromProgID("Word.Application");
                    if (wordType == null)
                    {
                        result = Fail("Microsoft Word COM automation is unavailable.");
                        return;
                    }
                    wordApp = Activator.CreateInstance(wordType);
                    wordType.InvokeMember("Visible", BindingFlags.SetProperty, null, wordApp, new object[] { false });
                    wordType.InvokeMember("DisplayAlerts", BindingFlags.SetProperty, null, wordApp, new object[] { 0 });

                    docs = wordType.InvokeMember("Documents", BindingFlags.GetProperty, null, wordApp, null);
                    doc = docs.GetType().InvokeMember("Open", BindingFlags.InvokeMethod, null, docs,
                        new object[] { fullSrc, false, true });

                    // 17 = wdExportFormatPDF
                    doc.GetType().InvokeMember("ExportAsFixedFormat", BindingFlags.InvokeMethod, null, doc,
                        new object[] { fullOut, 17 });
                    doc.GetType().InvokeMember("Close", BindingFlags.InvokeMethod, null, doc, new object[] { false });

                    if (!File.Exists(fullOut))
                    {
                        result = Fail("Microsoft Word did not produce a PDF.");
                        return;
                    }
                    _logger.Info("Conversion", "Word COM converted " + Path.GetFileName(fullSrc) + " -> " + Path.GetFileName(fullOut));
                    result = new ConversionResult
                    {
                        Success = true,
                        OutputPath = fullOut,
                        OutputFilename = Path.GetFileName(fullOut),
                        OutputSizeBytes = new FileInfo(fullOut).Length,
                        EngineUsed = "Microsoft Word"
                    };
                }
                catch (Exception ex)
                {
                    string msg = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                    _logger.Error("Conversion", "Word COM error: " + msg);
                    result = Fail("Word PDF conversion failed: " + msg);
                }
                finally
                {
                    try { if (doc != null) Marshal.ReleaseComObject(doc); } catch { }
                    try { if (docs != null) Marshal.ReleaseComObject(docs); } catch { }
                    try
                    {
                        if (wordApp != null)
                        {
                            wordApp.GetType().InvokeMember("Quit", BindingFlags.InvokeMethod, null, wordApp, new object[] { false });
                            Marshal.ReleaseComObject(wordApp);
                        }
                    }
                    catch { }
                }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            if (!t.Join(40000))
            {
                return Fail("Microsoft Word PDF conversion timed out after 40 seconds.");
            }
            return result ?? Fail("Unknown Word conversion error.");
        }

        private ConversionResult ConvertExcelWorkbookWithCom(string sourcePath, string desiredOutName)
        {
            if (!Directory.Exists(_paths.ConvertedDir)) Directory.CreateDirectory(_paths.ConvertedDir);
            string finalPath = Path.Combine(_paths.ConvertedDir, StorageManager.SanitizeDisplayName(desiredOutName));
            string fullSrc = Path.GetFullPath(sourcePath);
            string fullOut = Path.GetFullPath(finalPath);

            ConversionResult result = null;
            var t = new Thread(() =>
            {
                object excelApp = null;
                object workbooks = null;
                object wb = null;
                try
                {
                    Type excelType = Type.GetTypeFromProgID("Excel.Application");
                    if (excelType == null)
                    {
                        result = Fail("Microsoft Excel COM automation is unavailable.");
                        return;
                    }
                    excelApp = Activator.CreateInstance(excelType);
                    excelType.InvokeMember("Visible", BindingFlags.SetProperty, null, excelApp, new object[] { false });
                    excelType.InvokeMember("DisplayAlerts", BindingFlags.SetProperty, null, excelApp, new object[] { false });
                    try { excelType.InvokeMember("ScreenUpdating", BindingFlags.SetProperty, null, excelApp, new object[] { false }); } catch { }
                    try { excelType.InvokeMember("EnableEvents", BindingFlags.SetProperty, null, excelApp, new object[] { false }); } catch { }
                    try { excelType.InvokeMember("AskToUpdateLinks", BindingFlags.SetProperty, null, excelApp, new object[] { false }); } catch { }
                    try { excelType.InvokeMember("AutomationSecurity", BindingFlags.SetProperty, null, excelApp, new object[] { 3 }); } catch { }

                    workbooks = excelType.InvokeMember("Workbooks", BindingFlags.GetProperty, null, excelApp, null);
                    object m = Type.Missing;
                    wb = workbooks.GetType().InvokeMember("Open", BindingFlags.InvokeMethod, null, workbooks,
                        new object[] { fullSrc, 0, true, m, m, m, true });

                    // Fit wide spreadsheets cleanly to 1 page wide per sheet so columns never slice awkwardly when printing
                    try
                    {
                        object sheets = wb.GetType().InvokeMember("Worksheets", BindingFlags.GetProperty, null, wb, null);
                        if (sheets is IEnumerable)
                        {
                            foreach (object ws in (IEnumerable)sheets)
                            {
                                if (ws == null) continue;
                                try
                                {
                                    object ps = ws.GetType().InvokeMember("PageSetup", BindingFlags.GetProperty, null, ws, null);
                                    if (ps != null)
                                    {
                                        ps.GetType().InvokeMember("Zoom", BindingFlags.SetProperty, null, ps, new object[] { false });
                                        ps.GetType().InvokeMember("FitToPagesWide", BindingFlags.SetProperty, null, ps, new object[] { 1 });
                                        ps.GetType().InvokeMember("FitToPagesTall", BindingFlags.SetProperty, null, ps, new object[] { false });
                                        Marshal.ReleaseComObject(ps);
                                    }
                                }
                                catch { }
                                finally { try { Marshal.ReleaseComObject(ws); } catch { } }
                            }
                        }
                        if (sheets != null) try { Marshal.ReleaseComObject(sheets); } catch { }
                    }
                    catch { }

                    // 0 = xlTypePDF, 0 = xlQualityStandard, true = IncludeDocProperties, false = IgnorePrintAreas
                    wb.GetType().InvokeMember("ExportAsFixedFormat", BindingFlags.InvokeMethod, null, wb,
                        new object[] { 0, fullOut, 0, true, false });
                    wb.GetType().InvokeMember("Close", BindingFlags.InvokeMethod, null, wb, new object[] { false });

                    if (!File.Exists(fullOut) || new FileInfo(fullOut).Length == 0)
                    {
                        result = Fail("Microsoft Excel did not produce a PDF.");
                        return;
                    }
                    _logger.Info("Conversion", "Excel COM converted " + Path.GetFileName(fullSrc) + " -> " + Path.GetFileName(fullOut));
                    result = new ConversionResult
                    {
                        Success = true,
                        OutputPath = fullOut,
                        OutputFilename = Path.GetFileName(fullOut),
                        OutputSizeBytes = new FileInfo(fullOut).Length,
                        EngineUsed = "Microsoft Excel"
                    };
                }
                catch (Exception ex)
                {
                    string msg = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                    _logger.Error("Conversion", "Excel COM error: " + msg);
                    result = Fail("Excel PDF conversion failed: " + msg);
                }
                finally
                {
                    try { if (wb != null) { wb.GetType().InvokeMember("Close", BindingFlags.InvokeMethod, null, wb, new object[] { false }); Marshal.ReleaseComObject(wb); } } catch { }
                    try { if (workbooks != null) Marshal.ReleaseComObject(workbooks); } catch { }
                    try
                    {
                        if (excelApp != null)
                        {
                            excelApp.GetType().InvokeMember("Quit", BindingFlags.InvokeMethod, null, excelApp, null);
                            Marshal.ReleaseComObject(excelApp);
                        }
                    }
                    catch { }
                }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.IsBackground = true;
            t.Start();
            if (!t.Join(45000))
            {
                return Fail("Microsoft Excel PDF conversion timed out after 45 seconds.");
            }
            return result ?? Fail("Unknown Excel conversion error.");
        }

        private ConversionResult ConvertPowerPointWithCom(string sourcePath, string desiredOutName)
        {
            if (!Directory.Exists(_paths.ConvertedDir)) Directory.CreateDirectory(_paths.ConvertedDir);
            string finalPath = Path.Combine(_paths.ConvertedDir, StorageManager.SanitizeDisplayName(desiredOutName));
            string fullSrc = Path.GetFullPath(sourcePath);
            string fullOut = Path.GetFullPath(finalPath);

            ConversionResult result = null;
            var t = new Thread(() =>
            {
                object ppApp = null;
                object presentations = null;
                object pres = null;
                try
                {
                    Type ppType = Type.GetTypeFromProgID("PowerPoint.Application");
                    if (ppType == null)
                    {
                        result = Fail("Microsoft PowerPoint COM automation is unavailable.");
                        return;
                    }
                    ppApp = Activator.CreateInstance(ppType);
                    try { ppType.InvokeMember("DisplayAlerts", BindingFlags.SetProperty, null, ppApp, new object[] { 1 }); } catch { }
                    presentations = ppType.InvokeMember("Presentations", BindingFlags.GetProperty, null, ppApp, null);
                    // ReadOnly=-1 (msoTrue), Untitled=0, WithWindow=0 (msoFalse)
                    pres = presentations.GetType().InvokeMember("Open", BindingFlags.InvokeMethod, null, presentations,
                        new object[] { fullSrc, -1, 0, 0 });
                    // 32 = ppSaveAsPDF
                    pres.GetType().InvokeMember("SaveAs", BindingFlags.InvokeMethod, null, pres, new object[] { fullOut, 32 });
                    pres.GetType().InvokeMember("Close", BindingFlags.InvokeMethod, null, pres, null);

                    if (!File.Exists(fullOut) || new FileInfo(fullOut).Length == 0)
                    {
                        result = Fail("Microsoft PowerPoint did not produce a PDF.");
                        return;
                    }
                    _logger.Info("Conversion", "PowerPoint COM converted " + Path.GetFileName(fullSrc) + " -> " + Path.GetFileName(fullOut));
                    result = new ConversionResult
                    {
                        Success = true,
                        OutputPath = fullOut,
                        OutputFilename = Path.GetFileName(fullOut),
                        OutputSizeBytes = new FileInfo(fullOut).Length,
                        EngineUsed = "Microsoft PowerPoint"
                    };
                }
                catch (Exception ex)
                {
                    string msg = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                    _logger.Error("Conversion", "PowerPoint COM error: " + msg);
                    result = Fail("PowerPoint PDF conversion failed: " + msg);
                }
                finally
                {
                    try { if (pres != null) { pres.GetType().InvokeMember("Close", BindingFlags.InvokeMethod, null, pres, null); Marshal.ReleaseComObject(pres); } } catch { }
                    try { if (presentations != null) Marshal.ReleaseComObject(presentations); } catch { }
                    try
                    {
                        if (ppApp != null)
                        {
                            ppApp.GetType().InvokeMember("Quit", BindingFlags.InvokeMethod, null, ppApp, null);
                            Marshal.ReleaseComObject(ppApp);
                        }
                    }
                    catch { }
                }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.IsBackground = true;
            t.Start();
            if (!t.Join(45000))
            {
                return Fail("Microsoft PowerPoint PDF conversion timed out after 45 seconds.");
            }
            return result ?? Fail("Unknown PowerPoint conversion error.");
        }

        private sealed class NativeSheetData
        {
            public string Name;
            public List<List<string>> Rows = new List<List<string>>();
        }

        /// <summary>
        /// Zero-dependency pure .NET 4.8 .xlsx (OpenXML) and .csv/.tsv -> paginated table PDF fallback.
        /// Ensures spreadsheets convert cleanly even on laptops without Microsoft Excel or LibreOffice.
        /// </summary>
        public ConversionResult ConvertSpreadsheetNativeToPdf(string sourcePath, string ext, string desiredOutName)
        {
            try
            {
                List<NativeSheetData> sheets = string.Equals(ext, ".csv", StringComparison.OrdinalIgnoreCase)
                    ? ParseCsvFile(sourcePath)
                    : ParseXlsxFile(sourcePath);

                if (sheets.Count == 0)
                    sheets.Add(new NativeSheetData { Name = Path.GetFileNameWithoutExtension(sourcePath), Rows = new List<List<string>> { new List<string> { "(Empty Spreadsheet)" } } });

                if (!Directory.Exists(_paths.ConvertedDir)) Directory.CreateDirectory(_paths.ConvertedDir);
                string finalPath = Path.Combine(_paths.ConvertedDir, StorageManager.SanitizeDisplayName(desiredOutName));

                int maxColsOverall = 1;
                foreach (var sh in sheets)
                {
                    foreach (var r in sh.Rows)
                    {
                        if (r.Count > maxColsOverall) maxColsOverall = r.Count;
                    }
                }
                bool landscape = maxColsOverall > 4;
                int imgW = landscape ? 2338 : 1654; // ~200 DPI A4
                int imgH = landscape ? 1654 : 2338;
                double pdfW = landscape ? 841.89 : 595.28;
                double pdfH = landscape ? 595.28 : 841.89;

                var pages = new List<ImagePageData>();
                var jpegEnc = GetJpegEncoder();

                foreach (var sh in sheets)
                {
                    if (sh.Rows.Count == 0)
                        sh.Rows.Add(new List<string> { "(Empty Sheet)" });

                    int colCount = 1;
                    foreach (var r in sh.Rows) if (r.Count > colCount) colCount = r.Count;
                    if (colCount > 24) colCount = 24;

                    float margin = 64f;
                    float tableW = imgW - margin * 2f;
                    float[] colWidths = ComputeColumnWidths(sh.Rows, colCount, tableW);

                    var fontTitle = new Font("Segoe UI", 22f, FontStyle.Bold, GraphicsUnit.Pixel);
                    var fontHeader = new Font("Segoe UI", 16f, FontStyle.Bold, GraphicsUnit.Pixel);
                    var fontCell = new Font("Segoe UI", 15f, FontStyle.Regular, GraphicsUnit.Pixel);
                    var fontMeta = new Font("Segoe UI", 13f, FontStyle.Regular, GraphicsUnit.Pixel);

                    try
                    {
                        int rowCursor = 0;
                        int sheetPage = 0;
                        var headerRow = sh.Rows[0];

                        while (rowCursor < sh.Rows.Count)
                        {
                            sheetPage++;
                            using (var bmp = new Bitmap(imgW, imgH, PixelFormat.Format24bppRgb))
                            using (var g = Graphics.FromImage(bmp))
                            {
                                g.Clear(Color.White);
                                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

                                using (var navyBrush = new SolidBrush(Color.FromArgb(11, 37, 69)))
                                using (var mutedBrush = new SolidBrush(Color.FromArgb(100, 116, 139)))
                                using (var hdrBg = new SolidBrush(Color.FromArgb(232, 242, 255)))
                                using (var altBg = new SolidBrush(Color.FromArgb(248, 250, 252)))
                                using (var textBrush = new SolidBrush(Color.FromArgb(15, 23, 42)))
                                using (var borderPen = new Pen(Color.FromArgb(203, 213, 225), 1.5f))
                                using (var titlePen = new Pen(Color.FromArgb(0, 120, 255), 3f))
                                {
                                    g.DrawString(sh.Name ?? "Sheet", fontTitle, navyBrush, margin, 32f);
                                    g.DrawLine(titlePen, margin, 68f, imgW - margin, 68f);

                                    float y = 84f;
                                    float maxBottom = imgH - 56f;

                                    // Draw header row on page 1 and repeat on continuation pages if >1 row
                                    bool drawHdr = (rowCursor == 0) || (sh.Rows.Count > 1);
                                    if (drawHdr)
                                    {
                                        float hh = MeasureRowHeight(g, headerRow, colCount, colWidths, fontHeader);
                                        if (y + hh <= maxBottom)
                                        {
                                            DrawTableRow(g, headerRow, colCount, colWidths, margin, y, hh, fontHeader, hdrBg, navyBrush, borderPen);
                                            y += hh;
                                            if (rowCursor == 0) rowCursor = 1;
                                        }
                                    }

                                    while (rowCursor < sh.Rows.Count)
                                    {
                                        var row = sh.Rows[rowCursor];
                                        float rh = MeasureRowHeight(g, row, colCount, colWidths, fontCell);
                                        if (y + rh > maxBottom && rowCursor > 1) break;
                                        if (y + rh > maxBottom) rh = Math.Max(28f, maxBottom - y);
                                        Brush bg = (rowCursor % 2 == 0) ? altBg : Brushes.White;
                                        DrawTableRow(g, row, colCount, colWidths, margin, y, rh, fontCell, bg, textBrush, borderPen);
                                        y += rh;
                                        rowCursor++;
                                        if (y >= maxBottom - 20f) break;
                                    }

                                    g.DrawString((sh.Name ?? "Sheet") + " • Page " + sheetPage.ToString(CultureInfo.InvariantCulture),
                                        fontMeta, mutedBrush, margin, imgH - 38f);
                                }

                                using (var ms = new MemoryStream())
                                {
                                    if (jpegEnc != null)
                                    {
                                        using (var ep = new EncoderParameters(1))
                                        {
                                            ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 92L);
                                            bmp.Save(ms, jpegEnc, ep);
                                        }
                                    }
                                    else bmp.Save(ms, ImageFormat.Jpeg);

                                    pages.Add(new ImagePageData
                                    {
                                        PixelWidth = bmp.Width,
                                        PixelHeight = bmp.Height,
                                        JpegBytes = ms.ToArray()
                                    });
                                }
                            }
                        }
                    }
                    finally
                    {
                        fontTitle.Dispose();
                        fontHeader.Dispose();
                        fontCell.Dispose();
                        fontMeta.Dispose();
                    }
                }

                WriteMultiPageImagePdf(finalPath, pages, pdfW, pdfH);
                _logger.Info("Conversion", "Built-in Spreadsheet renderer converted " + Path.GetFileName(sourcePath) + " -> " + Path.GetFileName(finalPath));
                return new ConversionResult
                {
                    Success = true,
                    OutputPath = finalPath,
                    OutputFilename = Path.GetFileName(finalPath),
                    OutputSizeBytes = new FileInfo(finalPath).Length,
                    EngineUsed = "Built-in Spreadsheet PDF Renderer"
                };
            }
            catch (Exception ex)
            {
                _logger.Error("Conversion", "Native spreadsheet PDF failed: " + ex.Message);
                return Fail("Spreadsheet PDF conversion failed: " + ex.Message);
            }
        }

        private static float[] ComputeColumnWidths(List<List<string>> rows, int colCount, float totalW)
        {
            float[] weights = new float[colCount];
            for (int c = 0; c < colCount; c++) weights[c] = 8f;
            int limit = Math.Min(rows.Count, 80);
            for (int r = 0; r < limit; r++)
            {
                var row = rows[r];
                for (int c = 0; c < Math.Min(colCount, row.Count); c++)
                {
                    int len = (row[c] ?? "").Trim().Length;
                    float w = Math.Max(6f, Math.Min(42f, len + 3f));
                    if (w > weights[c]) weights[c] = w;
                }
            }
            float sum = 0f;
            for (int c = 0; c < colCount; c++) sum += weights[c];
            float[] widths = new float[colCount];
            for (int c = 0; c < colCount; c++) widths[c] = totalW * (weights[c] / Math.Max(1f, sum));
            return widths;
        }

        private static float MeasureRowHeight(Graphics g, List<string> row, int colCount, float[] colWidths, Font font)
        {
            float maxH = 30f;
            for (int c = 0; c < colCount; c++)
            {
                string val = c < row.Count ? (row[c] ?? "") : "";
                if (string.IsNullOrEmpty(val)) continue;
                float availW = Math.Max(12f, colWidths[c] - 12f);
                SizeF sz = g.MeasureString(val, font, (int)availW);
                float h = sz.Height + 12f;
                if (h > maxH) maxH = h;
            }
            return Math.Min(220f, maxH);
        }

        private static void DrawTableRow(Graphics g, List<string> row, int colCount, float[] colWidths, float x0, float y, float h, Font font, Brush bgBrush, Brush textBrush, Pen borderPen)
        {
            float cx = x0;
            for (int c = 0; c < colCount; c++)
            {
                float cw = colWidths[c];
                g.FillRectangle(bgBrush, cx, y, cw, h);
                g.DrawRectangle(borderPen, cx, y, cw, h);
                string val = c < row.Count ? (row[c] ?? "") : "";
                if (!string.IsNullOrEmpty(val))
                {
                    var rect = new RectangleF(cx + 6f, y + 5f, Math.Max(6f, cw - 12f), Math.Max(10f, h - 8f));
                    g.DrawString(val, font, textBrush, rect);
                }
                cx += cw;
            }
        }

        private static List<NativeSheetData> ParseCsvFile(string path)
        {
            var sh = new NativeSheetData { Name = Path.GetFileNameWithoutExtension(path) };
            string raw = File.ReadAllText(path, Encoding.UTF8);
            var curRow = new List<string>();
            var cell = new StringBuilder();
            bool inQuotes = false;
            for (int i = 0; i < raw.Length; i++)
            {
                char ch = raw[i];
                if (inQuotes)
                {
                    if (ch == '"' && i + 1 < raw.Length && raw[i + 1] == '"') { cell.Append('"'); i++; }
                    else if (ch == '"') inQuotes = false;
                    else cell.Append(ch);
                }
                else
                {
                    if (ch == '"') inQuotes = true;
                    else if (ch == ',') { curRow.Add(cell.ToString()); cell.Length = 0; }
                    else if (ch == '\r' || ch == '\n')
                    {
                        if (ch == '\r' && i + 1 < raw.Length && raw[i + 1] == '\n') i++;
                        curRow.Add(cell.ToString()); cell.Length = 0;
                        sh.Rows.Add(curRow);
                        curRow = new List<string>();
                    }
                    else cell.Append(ch);
                }
            }
            if (cell.Length > 0 || curRow.Count > 0)
            {
                curRow.Add(cell.ToString());
                sh.Rows.Add(curRow);
            }
            var list = new List<NativeSheetData>();
            list.Add(sh);
            return list;
        }

        private static List<NativeSheetData> ParseXlsxFile(string path)
        {
            var result = new List<NativeSheetData>();
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Read))
            {
                var sharedStrings = new List<string>();
                var ssEntry = zip.GetEntry("xl/sharedStrings.xml");
                if (ssEntry != null)
                {
                    using (var s = ssEntry.Open())
                    {
                        var xdoc = XDocument.Load(s);
                        XNamespace ns = xdoc.Root != null ? xdoc.Root.Name.Namespace : "";
                        foreach (var si in xdoc.Descendants(ns + "si"))
                        {
                            var sb = new StringBuilder();
                            foreach (var t in si.Descendants(ns + "t")) sb.Append(t.Value);
                            sharedStrings.Add(sb.ToString());
                        }
                    }
                }

                var sheetTargets = new List<KeyValuePair<string, string>>();
                var relMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var relsEntry = zip.GetEntry("xl/_rels/workbook.xml.rels");
                if (relsEntry != null)
                {
                    using (var s = relsEntry.Open())
                    {
                        var rdoc = XDocument.Load(s);
                        foreach (var rel in rdoc.Descendants())
                        {
                            if (rel.Name.LocalName != "Relationship") continue;
                            var idAttr = rel.Attribute("Id");
                            var tgtAttr = rel.Attribute("Target");
                            if (idAttr != null && tgtAttr != null)
                            {
                                string target = tgtAttr.Value.Replace('\\', '/').TrimStart('/');
                                if (!target.StartsWith("xl/", StringComparison.OrdinalIgnoreCase)) target = "xl/" + target;
                                relMap[idAttr.Value] = target;
                            }
                        }
                    }
                }

                var wbEntry = zip.GetEntry("xl/workbook.xml");
                if (wbEntry != null)
                {
                    using (var s = wbEntry.Open())
                    {
                        var wdoc = XDocument.Load(s);
                        int idx = 1;
                        foreach (var shEl in wdoc.Descendants())
                        {
                            if (shEl.Name.LocalName != "sheet") continue;
                            string name = shEl.Attribute("name") != null ? shEl.Attribute("name").Value : ("Sheet" + idx);
                            XAttribute ridAttr = null;
                            foreach (var a in shEl.Attributes())
                            {
                                if (a.Name.LocalName == "id") { ridAttr = a; break; }
                            }
                            string target = (ridAttr != null && relMap.ContainsKey(ridAttr.Value))
                                ? relMap[ridAttr.Value]
                                : ("xl/worksheets/sheet" + idx.ToString(CultureInfo.InvariantCulture) + ".xml");
                            sheetTargets.Add(new KeyValuePair<string, string>(name, target));
                            idx++;
                        }
                    }
                }

                if (sheetTargets.Count == 0)
                {
                    foreach (var e in zip.Entries)
                    {
                        if (e.FullName.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase) && e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                            sheetTargets.Add(new KeyValuePair<string, string>(Path.GetFileNameWithoutExtension(e.Name), e.FullName));
                    }
                }

                foreach (var pair in sheetTargets)
                {
                    var entry = zip.GetEntry(pair.Value);
                    if (entry == null) continue;
                    var shData = new NativeSheetData { Name = pair.Key };
                    using (var s = entry.Open())
                    {
                        var sdoc = XDocument.Load(s);
                        foreach (var rowEl in sdoc.Descendants())
                        {
                            if (rowEl.Name.LocalName != "row") continue;
                            var cellsMap = new Dictionary<int, string>();
                            int maxCol = -1;
                            int fallbackCol = 0;
                            foreach (var cEl in rowEl.Elements())
                            {
                                if (cEl.Name.LocalName != "c") continue;
                                var rAttr = cEl.Attribute("r");
                                int colIdx = rAttr != null ? ParseExcelColumnIndex(rAttr.Value, fallbackCol) : fallbackCol;
                                fallbackCol = colIdx + 1;
                                string tType = cEl.Attribute("t") != null ? cEl.Attribute("t").Value : "";
                                string val = "";
                                if (tType == "inlineStr")
                                {
                                    var isEl = cEl.Elements().FirstOrDefault(x => x.Name.LocalName == "is");
                                    if (isEl != null)
                                    {
                                        var sb = new StringBuilder();
                                        foreach (var te in isEl.Descendants()) if (te.Name.LocalName == "t") sb.Append(te.Value);
                                        val = sb.ToString();
                                    }
                                }
                                else
                                {
                                    var vEl = cEl.Elements().FirstOrDefault(x => x.Name.LocalName == "v");
                                    string rawV = vEl != null ? vEl.Value : "";
                                    if (tType == "s")
                                    {
                                        int si;
                                        if (int.TryParse(rawV, out si) && si >= 0 && si < sharedStrings.Count)
                                            val = sharedStrings[si];
                                    }
                                    else if (tType == "b")
                                    {
                                        val = rawV == "1" ? "TRUE" : "FALSE";
                                    }
                                    else
                                    {
                                        val = rawV;
                                    }
                                }
                                cellsMap[colIdx] = val;
                                if (colIdx > maxCol) maxCol = colIdx;
                            }
                            if (maxCol >= 0)
                            {
                                var rowList = new List<string>(maxCol + 1);
                                bool anyNonEmpty = false;
                                for (int ci = 0; ci <= maxCol; ci++)
                                {
                                    string cv = cellsMap.ContainsKey(ci) ? cellsMap[ci] : "";
                                    if (!string.IsNullOrWhiteSpace(cv)) anyNonEmpty = true;
                                    rowList.Add(cv);
                                }
                                if (anyNonEmpty) shData.Rows.Add(rowList);
                            }
                        }
                    }
                    result.Add(shData);
                }
            }
            return result;
        }

        private static int ParseExcelColumnIndex(string cellRef, int fallback)
        {
            if (string.IsNullOrWhiteSpace(cellRef)) return fallback;
            int col = 0;
            bool hasLetters = false;
            for (int i = 0; i < cellRef.Length; i++)
            {
                char ch = char.ToUpperInvariant(cellRef[i]);
                if (ch >= 'A' && ch <= 'Z')
                {
                    hasLetters = true;
                    col = col * 26 + (ch - 'A' + 1);
                }
                else break;
            }
            return hasLetters ? (col - 1) : fallback;
        }

        /// <summary>
        /// Converts a PDF file into an editable Microsoft Word (.docx) document using Microsoft Word's
        /// native PDF Reflow engine (preferred) or LibreOffice Writer as fallback.
        /// </summary>
        public ConversionResult ConvertPdfToDocx(string pdfPath, string preferredBaseName)
        {
            if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
                return Fail("Source PDF not found on disk.");

            if (!Directory.Exists(_paths.ConvertedDir)) Directory.CreateDirectory(_paths.ConvertedDir);
            string cleanBase = StorageManager.SanitizeDisplayName(
                string.IsNullOrWhiteSpace(preferredBaseName) ? Path.GetFileNameWithoutExtension(pdfPath) : preferredBaseName
            );
            if (cleanBase.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                cleanBase = cleanBase.Substring(0, cleanBase.Length - 4);
            if (cleanBase.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
                cleanBase = cleanBase.Substring(0, cleanBase.Length - 5);
            if (string.IsNullOrWhiteSpace(cleanBase)) cleanBase = "document";

            string desiredName = cleanBase + ".docx";
            string outPath = Path.Combine(_paths.ConvertedDir, desiredName);
            if (File.Exists(outPath))
            {
                desiredName = cleanBase + "_" + DateTime.UtcNow.ToString("HHmmss", CultureInfo.InvariantCulture) + ".docx";
                outPath = Path.Combine(_paths.ConvertedDir, desiredName);
            }

            string wordFailureReason = null;
            if (IsMicrosoftWordInstalled())
            {
                var wordRes = ConvertPdfToDocxWithWordCom(pdfPath, outPath);
                if (wordRes.Success) return wordRes;
                wordFailureReason = wordRes.ErrorMessage;
                _logger.Warn("Conversion", "Word COM PDF->DOCX declined/failed (" + wordFailureReason + "), checking LibreOffice fallback...");
            }

            string soffice = FindLibreOfficePath();
            if (!string.IsNullOrEmpty(soffice))
            {
                var loRes = ConvertPdfToDocxWithLibreOffice(soffice, pdfPath, outPath);
                if (loRes.Success) return loRes;
                return Fail((wordFailureReason != null ? wordFailureReason + " | " : "") + loRes.ErrorMessage);
            }

            return Fail(wordFailureReason ?? "Microsoft Word 2013+ (PDF Reflow) or LibreOffice is not installed on this PC.");
        }

        private ConversionResult ConvertPdfToDocxWithWordCom(string pdfPath, string outDocxPath)
        {
            string fullSrc = Path.GetFullPath(pdfPath);
            string fullOut = Path.GetFullPath(outDocxPath);
            ConversionResult result = null;

            var t = new Thread(() =>
            {
                object wordApp = null;
                object docs = null;
                object doc = null;
                try
                {
                    // Suppress Microsoft Word's one-time "Word will now convert your PDF" modal dialog
                    string[] verKeys = new[] { @"Software\Microsoft\Office\16.0\Word\Options", @"Software\Microsoft\Office\15.0\Word\Options" };
                    foreach (var vk in verKeys)
                    {
                        try
                        {
                            using (var k = Registry.CurrentUser.CreateSubKey(vk))
                            {
                                if (k != null) k.SetValue("DisableConvertPdfWarning", 1, RegistryValueKind.DWord);
                            }
                        }
                        catch { }
                    }

                    Type wordType = Type.GetTypeFromProgID("Word.Application");
                    if (wordType == null)
                    {
                        result = Fail("Microsoft Word COM automation is unavailable.");
                        return;
                    }
                    wordApp = Activator.CreateInstance(wordType);

                    // 1. Verify genuine Microsoft Word >= 15.0 (Word 2013+); Word 2007/2010 & WPS Office lack PDF Reflow
                    string appName = "";
                    string appVerStr = "";
                    try { appName = Convert.ToString(wordType.InvokeMember("Name", BindingFlags.GetProperty, null, wordApp, null)); } catch { }
                    try { appVerStr = Convert.ToString(wordType.InvokeMember("Version", BindingFlags.GetProperty, null, wordApp, null)); } catch { }

                    double majorVer = 0;
                    double.TryParse(appVerStr, NumberStyles.Any, CultureInfo.InvariantCulture, out majorVer);
                    if (string.IsNullOrEmpty(appName) || appName.IndexOf("Microsoft Word", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        result = Fail("Detected '" + (string.IsNullOrEmpty(appName) ? "non-Microsoft Word" : appName) + "' (lacks Microsoft PDF Reflow).");
                        return;
                    }
                    if (majorVer > 0 && majorVer < 15.0)
                    {
                        result = Fail("Installed Microsoft Word v" + appVerStr + " (Office 2007/2010) does not support PDF Reflow (requires Word 2013+).");
                        return;
                    }

                    wordType.InvokeMember("Visible", BindingFlags.SetProperty, null, wordApp, new object[] { false });
                    wordType.InvokeMember("DisplayAlerts", BindingFlags.SetProperty, null, wordApp, new object[] { 0 });
                    try { wordType.InvokeMember("AutomationSecurity", BindingFlags.SetProperty, null, wordApp, new object[] { 3 }); } catch { }

                    // 2. Locate Word's explicit PDF Reflow converter (ClassName="IFDP" / Ext="pdf") so Word NEVER falls back
                    //    to "Recover Text from Any File" which dumps raw "%PDF-1.4 ... /FlateDecode" binary bytes!
                    object pdfOpenFormat = null;
                    try
                    {
                        object converters = wordType.InvokeMember("FileConverters", BindingFlags.GetProperty, null, wordApp, null);
                        if (converters is IEnumerable)
                        {
                            foreach (object fc in (IEnumerable)converters)
                            {
                                if (fc == null) continue;
                                Type ft = fc.GetType();
                                string cls = "";
                                string exts = "";
                                string fmtName = "";
                                try { cls = Convert.ToString(ft.InvokeMember("ClassName", BindingFlags.GetProperty, null, fc, null)); } catch { }
                                try { exts = Convert.ToString(ft.InvokeMember("Extensions", BindingFlags.GetProperty, null, fc, null)); } catch { }
                                try { fmtName = Convert.ToString(ft.InvokeMember("FormatName", BindingFlags.GetProperty, null, fc, null)); } catch { }
                                if (string.Equals(cls, "IFDP", StringComparison.OrdinalIgnoreCase) ||
                                    (!string.IsNullOrEmpty(exts) && exts.IndexOf("pdf", StringComparison.OrdinalIgnoreCase) >= 0) ||
                                    (!string.IsNullOrEmpty(fmtName) && fmtName.IndexOf("PDF", StringComparison.OrdinalIgnoreCase) >= 0))
                                {
                                    pdfOpenFormat = Convert.ToInt32(ft.InvokeMember("OpenFormat", BindingFlags.GetProperty, null, fc, null));
                                    break;
                                }
                            }
                        }
                    }
                    catch { }

                    if (pdfOpenFormat == null)
                    {
                        result = Fail("Microsoft Word v" + appVerStr + " on this PC is missing its PDF Reflow converter (IFDP).");
                        return;
                    }

                    docs = wordType.InvokeMember("Documents", BindingFlags.GetProperty, null, wordApp, null);
                    object m = Type.Missing;
                    // Force explicit Format=pdfOpenFormat + OpenAndRepair=false + NoEncodingDialog=true
                    doc = docs.GetType().InvokeMember("Open", BindingFlags.InvokeMethod, null, docs,
                        new object[] { fullSrc, false, true, false, m, m, m, m, m, pdfOpenFormat, m, false, false, m, true });

                    // 12 = wdFormatXMLDocument (.docx)
                    doc.GetType().InvokeMember("SaveAs2", BindingFlags.InvokeMethod, null, doc,
                        new object[] { fullOut, 12 });
                    doc.GetType().InvokeMember("Close", BindingFlags.InvokeMethod, null, doc, new object[] { false });

                    if (!File.Exists(fullOut))
                    {
                        result = Fail("Microsoft Word did not produce a .docx file.");
                        return;
                    }

                    // 3. Post-conversion integrity guard: reject & delete if output contains raw "%PDF-1.x ... endobj" text
                    if (DocxEditor.IsCorruptedRawPdfDocx(fullOut))
                    {
                        try { File.Delete(fullOut); } catch { }
                        _logger.Warn("Conversion", "Rejected raw-PDF-text Word output for " + Path.GetFileName(fullSrc));
                        result = Fail("Word v" + appVerStr + " opened the PDF as raw text instead of reflowing it.");
                        return;
                    }

                    _logger.Info("Conversion", "Word COM PDF->DOCX (IFDP fmt=" + pdfOpenFormat + "): " + Path.GetFileName(fullSrc) + " -> " + Path.GetFileName(fullOut));
                    result = new ConversionResult
                    {
                        Success = true,
                        OutputPath = fullOut,
                        OutputFilename = Path.GetFileName(fullOut),
                        OutputSizeBytes = new FileInfo(fullOut).Length,
                        EngineUsed = "Microsoft Word PDF Reflow"
                    };
                }
                catch (Exception ex)
                {
                    string msg = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                    _logger.Error("Conversion", "Word COM PDF->DOCX error: " + msg);
                    result = Fail("PDF to Word conversion failed: " + msg);
                }
                finally
                {
                    try { if (doc != null) Marshal.ReleaseComObject(doc); } catch { }
                    try { if (docs != null) Marshal.ReleaseComObject(docs); } catch { }
                    try
                    {
                        if (wordApp != null)
                        {
                            wordApp.GetType().InvokeMember("Quit", BindingFlags.InvokeMethod, null, wordApp, new object[] { false });
                            Marshal.ReleaseComObject(wordApp);
                        }
                    }
                    catch { }
                }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.IsBackground = true;
            t.Start();
            if (!t.Join(60000))
            {
                return Fail("PDF to Word conversion timed out after 60 seconds (file may be very large or password-protected).");
            }
            return result ?? Fail("Unknown PDF to Word conversion error.");
        }

        private ConversionResult ConvertPdfToDocxWithLibreOffice(string soffice, string pdfPath, string outDocxPath)
        {
            string tempOut = Path.Combine(_paths.TempDir, "lo_pdf2docx_" + Guid.NewGuid().ToString("N"));
            string profileDir = Path.Combine(tempOut, "profile");
            Directory.CreateDirectory(profileDir);
            try
            {
                string profileUri = new Uri(profileDir).AbsoluteUri;
                var psi = new ProcessStartInfo
                {
                    FileName = soffice,
                    Arguments = "-env:UserInstallation=" + profileUri + " --headless --norestore --nolockcheck --nodefault --nofirststartwizard --infilter=\"writer_pdf_import\" --convert-to docx --outdir \"" + tempOut + "\" \"" + pdfPath + "\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using (var proc = Process.Start(psi))
                {
                    if (proc == null) return Fail("Could not launch LibreOffice.");
                    if (!proc.WaitForExit(60000))
                    {
                        try { proc.Kill(); } catch { }
                        return Fail("LibreOffice PDF to Word conversion timed out after 60s.");
                    }
                }
                string[] docxFiles = Directory.GetFiles(tempOut, "*.docx");
                if (docxFiles.Length == 0) return Fail("LibreOffice did not produce a .docx output.");
                File.Copy(docxFiles[0], outDocxPath, true);
                if (DocxEditor.IsCorruptedRawPdfDocx(outDocxPath))
                {
                    try { File.Delete(outDocxPath); } catch { }
                    return Fail("LibreOffice produced raw PDF text instead of editable Word content.");
                }
                return new ConversionResult
                {
                    Success = true,
                    OutputPath = outDocxPath,
                    OutputFilename = Path.GetFileName(outDocxPath),
                    OutputSizeBytes = new FileInfo(outDocxPath).Length,
                    EngineUsed = "LibreOffice Writer"
                };
            }
            catch (Exception ex)
            {
                return Fail("LibreOffice PDF to Word error: " + ex.Message);
            }
            finally
            {
                try { if (Directory.Exists(tempOut)) Directory.Delete(tempOut, true); } catch { }
            }
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
