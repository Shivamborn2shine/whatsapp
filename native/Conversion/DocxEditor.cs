using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace ScanDrop.Native.Conversion
{
    public sealed class DocxTextBlock
    {
        public string PartPath;
        public string PartType; // "header" | "footer" | "body"
        public int ParagraphIndex;
        public string Text;
        public bool InTable;
    }

    public sealed class DocxImageInfo
    {
        public string PartPath;
        public int DrawingIndex;
        public string Name;
        public double WidthCm;
        public double HeightCm;
        public long WidthEmu;
        public long HeightEmu;
    }

    public sealed class DocxInspectResult
    {
        public bool Success;
        public string Error;
        public List<DocxTextBlock> Headers = new List<DocxTextBlock>();
        public List<DocxTextBlock> Footers = new List<DocxTextBlock>();
        public List<DocxTextBlock> BodyBlocks = new List<DocxTextBlock>();
        public List<DocxImageInfo> Images = new List<DocxImageInfo>();
    }

    public sealed class DocxFindReplaceRule
    {
        public string Find;
        public string Replace;
        public bool CaseSensitive;
        public string Scope; // "all" | "headers" | "body"
    }

    public sealed class DocxParagraphEdit
    {
        public string PartPath;
        public int ParagraphIndex;
        public string NewText;
    }

    public sealed class DocxImageResize
    {
        public string PartPath;
        public int DrawingIndex;
        public double ScalePercent; // e.g., 100 = unchanged, 125 = 125%
        public double WidthCm;      // optional explicit width in cm (> 0 overrides ScalePercent)
        public double HeightCm;     // optional explicit height in cm (> 0 overrides ScalePercent)
    }

    public sealed class DocxEditResult
    {
        public bool Success;
        public string Error;
        public string OutputDocxPath;
        public int ReplacementsCount;
        public int ParagraphEditsCount;
        public int ImagesResizedCount;
    }

    /// <summary>
    /// Native .NET 4.8 OpenXML (.docx) inspector and run-preserving editor.
    /// Supports Headers/Footers, Body/Table paragraphs, run-spanning Find & Replace,
    /// direct paragraph editing, and embedded image resizing without corrupting formatting.
    /// </summary>
    public static class DocxEditor
    {
        private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        private static readonly XNamespace WP = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
        private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
        private static readonly XNamespace PIC = "http://schemas.openxmlformats.org/drawingml/2006/picture";
        private static readonly XNamespace WPS = "http://schemas.microsoft.com/office/word/2010/wordprocessingShape";
        private static readonly XNamespace MC = "http://schemas.openxmlformats.org/markup-compatibility/2006";
        private static readonly XNamespace V = "urn:schemas-microsoft-com:vml";
        private static readonly XNamespace XML = "http://www.w3.org/XML/1998/namespace";
        private const double EmuPerCm = 360000.0;
        private const double EmuPerPt = 12700.0;

        public static DocxInspectResult Inspect(string docxPath)
        {
            var res = new DocxInspectResult();
            if (string.IsNullOrWhiteSpace(docxPath) || !File.Exists(docxPath))
            {
                res.Error = "DOCX file not found.";
                return res;
            }

            try
            {
                using (var fs = new FileStream(docxPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var zip = new ZipArchive(fs, ZipArchiveMode.Read))
                {
                    var xmlParts = GetEditableParts(zip);
                    foreach (var entry in xmlParts)
                    {
                        string partPath = entry.FullName;
                        string partType = ClassifyPart(partPath);
                        XDocument doc;
                        using (var s = entry.Open())
                        {
                            doc = XDocument.Load(s, LoadOptions.PreserveWhitespace);
                        }

                        var paragraphs = doc.Descendants(W + "p").ToList();
                        for (int pi = 0; pi < paragraphs.Count; pi++)
                        {
                            var p = paragraphs[pi];
                            string text = ExtractDirectParagraphText(p);
                            if (string.IsNullOrWhiteSpace(text)) continue;

                            bool inTable = p.Ancestors(W + "tc").Any();
                            var block = new DocxTextBlock
                            {
                                PartPath = partPath,
                                PartType = partType,
                                ParagraphIndex = pi,
                                Text = text,
                                InTable = inTable
                            };

                            if (partType == "header") res.Headers.Add(block);
                            else if (partType == "footer") res.Footers.Add(block);
                            else if (res.BodyBlocks.Count < 160) res.BodyBlocks.Add(block);
                        }

                        var drawings = GetDrawings(doc);
                        for (int di = 0; di < drawings.Count; di++)
                        {
                            var dr = drawings[di];
                            var extent = dr.Element(WP + "extent");
                            if (extent == null) continue;
                            long cx = ParseLongAttr(extent, "cx");
                            long cy = ParseLongAttr(extent, "cy");
                            if (cx <= 0 || cy <= 0) continue;

                            var docPr = dr.Element(WP + "docPr");
                            string name = (docPr != null && docPr.Attribute("name") != null)
                                ? docPr.Attribute("name").Value
                                : ("Image " + (di + 1));

                            res.Images.Add(new DocxImageInfo
                            {
                                PartPath = partPath,
                                DrawingIndex = di,
                                Name = name,
                                WidthEmu = cx,
                                HeightEmu = cy,
                                WidthCm = Math.Round(cx / EmuPerCm, 2),
                                HeightCm = Math.Round(cy / EmuPerCm, 2)
                            });
                        }
                    }
                }
                res.Success = true;
                return res;
            }
            catch (Exception ex)
            {
                res.Error = "Could not inspect DOCX: " + ex.Message;
                return res;
            }
        }

        public static DocxEditResult ApplyEdits(
            string sourceDocxPath,
            string outputDocxPath,
            List<DocxFindReplaceRule> findReplaces,
            List<DocxParagraphEdit> paragraphEdits,
            List<DocxImageResize> imageResizes)
        {
            var res = new DocxEditResult { OutputDocxPath = outputDocxPath };
            if (string.IsNullOrWhiteSpace(sourceDocxPath) || !File.Exists(sourceDocxPath))
            {
                res.Error = "Source DOCX file not found.";
                return res;
            }

            try
            {
                string outDir = Path.GetDirectoryName(outputDocxPath);
                if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir)) Directory.CreateDirectory(outDir);
                File.Copy(sourceDocxPath, outputDocxPath, true);

                var rules = (findReplaces ?? new List<DocxFindReplaceRule>())
                    .Where(r => r != null && !string.IsNullOrEmpty(r.Find))
                    .ToList();
                var pEdits = paragraphEdits ?? new List<DocxParagraphEdit>();
                var imgEdits = imageResizes ?? new List<DocxImageResize>();

                using (var zip = ZipFile.Open(outputDocxPath, ZipArchiveMode.Update))
                {
                    var editableNames = GetEditableParts(zip).Select(e => e.FullName).ToList();

                    foreach (var partPath in editableNames)
                    {
                        var entry = zip.GetEntry(partPath);
                        if (entry == null) continue;
                        string partType = ClassifyPart(partPath);

                        XDocument doc;
                        using (var s = entry.Open())
                        {
                            doc = XDocument.Load(s, LoadOptions.PreserveWhitespace);
                        }

                        bool partModified = false;
                        var paragraphs = doc.Descendants(W + "p").ToList();

                        // 1. Apply direct paragraph edits for this part
                        foreach (var pe in pEdits)
                        {
                            if (pe == null || !string.Equals(pe.PartPath, partPath, StringComparison.OrdinalIgnoreCase)) continue;
                            if (pe.ParagraphIndex < 0 || pe.ParagraphIndex >= paragraphs.Count) continue;
                            if (SetDirectParagraphText(paragraphs[pe.ParagraphIndex], pe.NewText ?? ""))
                            {
                                res.ParagraphEditsCount++;
                                partModified = true;
                            }
                        }

                        // 2. Apply run-aware Find & Replace rules matching scope
                        foreach (var rule in rules)
                        {
                            string scope = (rule.Scope ?? "all").Trim().ToLowerInvariant();
                            if (scope == "headers" && partType != "header" && partType != "footer") continue;
                            if (scope == "body" && partType != "body") continue;

                            for (int pi = 0; pi < paragraphs.Count; pi++)
                            {
                                int hits = ReplaceInParagraph(paragraphs[pi], rule.Find, rule.Replace ?? "", rule.CaseSensitive);
                                if (hits > 0)
                                {
                                    res.ReplacementsCount += hits;
                                    partModified = true;
                                }
                            }
                        }

                        // 3. Apply embedded image resizes for this part
                        var drawings = GetDrawings(doc);
                        foreach (var ir in imgEdits)
                        {
                            if (ir == null || !string.Equals(ir.PartPath, partPath, StringComparison.OrdinalIgnoreCase)) continue;
                            if (ir.DrawingIndex < 0 || ir.DrawingIndex >= drawings.Count) continue;
                            if (ResizeDrawing(drawings[ir.DrawingIndex], ir))
                            {
                                res.ImagesResizedCount++;
                                partModified = true;
                            }
                        }

                        if (partModified)
                        {
                            entry.Delete();
                            var newEntry = zip.CreateEntry(partPath, CompressionLevel.Optimal);
                            using (var outStream = newEntry.Open())
                            using (var writer = new XmlTextWriter(outStream, new UTF8Encoding(false)))
                            {
                                doc.Save(writer);
                            }
                        }
                    }
                }

                res.Success = true;
                return res;
            }
            catch (Exception ex)
            {
                res.Error = "DOCX edit failed: " + ex.Message;
                return res;
            }
        }

        private static List<ZipArchiveEntry> GetEditableParts(ZipArchive zip)
        {
            var list = new List<ZipArchiveEntry>();
            foreach (var e in zip.Entries)
            {
                string n = (e.FullName ?? "").ToLowerInvariant();
                if (n == "word/document.xml" ||
                    (n.StartsWith("word/header") && n.EndsWith(".xml")) ||
                    (n.StartsWith("word/footer") && n.EndsWith(".xml")))
                {
                    list.Add(e);
                }
            }
            // Headers first, then footers, then body
            list.Sort((a, b) =>
            {
                int pa = PartOrder(a.FullName);
                int pb = PartOrder(b.FullName);
                if (pa != pb) return pa.CompareTo(pb);
                return string.Compare(a.FullName, b.FullName, StringComparison.OrdinalIgnoreCase);
            });
            return list;
        }

        private static int PartOrder(string name)
        {
            string t = ClassifyPart(name);
            if (t == "header") return 0;
            if (t == "footer") return 1;
            return 2;
        }

        private static string ClassifyPart(string partPath)
        {
            string n = (partPath ?? "").ToLowerInvariant();
            if (n.Contains("header")) return "header";
            if (n.Contains("footer")) return "footer";
            return "body";
        }

        /// <summary>
        /// Gets only <w:t> elements that directly belong to this paragraph (not nested textboxes inside child <w:p>).
        /// </summary>
        private static List<XElement> GetDirectTextNodes(XElement paragraph)
        {
            var nodes = new List<XElement>();
            foreach (var t in paragraph.Descendants(W + "t"))
            {
                var nearestP = t.Ancestors(W + "p").FirstOrDefault();
                if (nearestP == paragraph) nodes.Add(t);
            }
            return nodes;
        }

        private static string ExtractDirectParagraphText(XElement paragraph)
        {
            var nodes = GetDirectTextNodes(paragraph);
            if (nodes.Count == 0) return "";
            var sb = new StringBuilder();
            foreach (var t in nodes) sb.Append(t.Value);
            return sb.ToString();
        }

        private static bool SetDirectParagraphText(XElement paragraph, string newText)
        {
            var nodes = GetDirectTextNodes(paragraph);
            if (nodes.Count == 0)
            {
                var r = new XElement(W + "r", new XElement(W + "t", new XAttribute(XML + "space", "preserve"), newText));
                paragraph.Add(r);
                return true;
            }
            string current = ExtractDirectParagraphText(paragraph);
            if (string.Equals(current, newText, StringComparison.Ordinal)) return false;

            nodes[0].Value = newText;
            EnsurePreserveSpace(nodes[0]);
            for (int i = 1; i < nodes.Count; i++)
            {
                nodes[i].Value = "";
            }
            return true;
        }

        /// <summary>
        /// Span-preserving Find & Replace across split Word <w:t> runs.
        /// Keeps formatting (bold/italic/color) of surrounding runs in the same paragraph intact!
        /// </summary>
        private static int ReplaceInParagraph(XElement paragraph, string find, string replace, bool caseSensitive)
        {
            if (string.IsNullOrEmpty(find)) return 0;
            var nodes = GetDirectTextNodes(paragraph);
            if (nodes.Count == 0) return 0;

            StringComparison cmp = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            int totalHits = 0;

            // Loop until no more matches remain in the concatenated paragraph text
            int searchFrom = 0;
            while (true)
            {
                var sb = new StringBuilder();
                var starts = new int[nodes.Count];
                var lengths = new int[nodes.Count];
                for (int i = 0; i < nodes.Count; i++)
                {
                    starts[i] = sb.Length;
                    string val = nodes[i].Value ?? "";
                    lengths[i] = val.Length;
                    sb.Append(val);
                }

                string combined = sb.ToString();
                if (searchFrom >= combined.Length) break;

                int matchIdx = combined.IndexOf(find, searchFrom, cmp);
                if (matchIdx < 0) break;

                int matchEnd = matchIdx + find.Length;
                int startNode = -1;
                int endNode = -1;
                for (int i = 0; i < nodes.Count; i++)
                {
                    int nStart = starts[i];
                    int nEnd = nStart + lengths[i];
                    if (lengths[i] == 0) continue;
                    if (startNode < 0 && matchIdx < nEnd) startNode = i;
                    if (matchEnd <= nEnd) { endNode = i; break; }
                }
                if (startNode < 0 || endNode < 0) break;

                if (startNode == endNode)
                {
                    int localStart = matchIdx - starts[startNode];
                    string orig = nodes[startNode].Value ?? "";
                    nodes[startNode].Value = orig.Substring(0, localStart) + replace + orig.Substring(localStart + find.Length);
                    EnsurePreserveSpace(nodes[startNode]);
                }
                else
                {
                    // Match spans multiple runs: put replacement at startNode and trim matched prefix/suffix across spanned runs
                    int localStart = matchIdx - starts[startNode];
                    string firstOrig = nodes[startNode].Value ?? "";
                    nodes[startNode].Value = firstOrig.Substring(0, localStart) + replace;
                    EnsurePreserveSpace(nodes[startNode]);

                    for (int m = startNode + 1; m < endNode; m++)
                    {
                        nodes[m].Value = "";
                    }

                    int localEndCut = matchEnd - starts[endNode];
                    string lastOrig = nodes[endNode].Value ?? "";
                    nodes[endNode].Value = localEndCut < lastOrig.Length ? lastOrig.Substring(localEndCut) : "";
                    EnsurePreserveSpace(nodes[endNode]);
                }

                totalHits++;
                searchFrom = matchIdx + replace.Length;
            }

            return totalHits;
        }

        private static void EnsurePreserveSpace(XElement tElem)
        {
            if (tElem == null) return;
            string v = tElem.Value ?? "";
            if (v.Length > 0 && (char.IsWhiteSpace(v[0]) || char.IsWhiteSpace(v[v.Length - 1])))
            {
                tElem.SetAttributeValue(XML + "space", "preserve");
            }
        }

        private static List<XElement> GetDrawings(XDocument doc)
        {
            var list = new List<XElement>();
            foreach (var d in doc.Descendants(W + "drawing"))
            {
                var inline = d.Element(WP + "inline");
                if (inline != null) list.Add(inline);
                var anchor = d.Element(WP + "anchor");
                if (anchor != null) list.Add(anchor);
            }
            return list;
        }

        private static bool ResizeDrawing(XElement drawingContainer, DocxImageResize resize)
        {
            var extent = drawingContainer.Element(WP + "extent");
            if (extent == null) return false;
            long curCx = ParseLongAttr(extent, "cx");
            long curCy = ParseLongAttr(extent, "cy");
            if (curCx <= 0 || curCy <= 0) return false;

            long newCx = curCx;
            long newCy = curCy;

            if (resize.WidthCm > 0.2 && resize.HeightCm > 0.2)
            {
                newCx = (long)Math.Round(resize.WidthCm * EmuPerCm);
                newCy = (long)Math.Round(resize.HeightCm * EmuPerCm);
            }
            else if (resize.WidthCm > 0.2)
            {
                double ratio = (double)curCy / curCx;
                newCx = (long)Math.Round(resize.WidthCm * EmuPerCm);
                newCy = (long)Math.Round(newCx * ratio);
            }
            else if (resize.HeightCm > 0.2)
            {
                double ratio = (double)curCx / curCy;
                newCy = (long)Math.Round(resize.HeightCm * EmuPerCm);
                newCx = (long)Math.Round(newCy * ratio);
            }
            else if (resize.ScalePercent > 5 && resize.ScalePercent <= 400 && Math.Abs(resize.ScalePercent - 100.0) > 0.1)
            {
                double factor = resize.ScalePercent / 100.0;
                newCx = (long)Math.Round(curCx * factor);
                newCy = (long)Math.Round(curCy * factor);
            }
            else
            {
                return false;
            }

            // Clamp to sane printable page bounds (0.5 cm to 28 cm)
            long minEmu = (long)(0.5 * EmuPerCm);
            long maxEmu = (long)(28.0 * EmuPerCm);
            newCx = Math.Max(minEmu, Math.Min(maxEmu, newCx));
            newCy = Math.Max(minEmu, Math.Min(maxEmu, newCy));

            extent.SetAttributeValue("cx", newCx.ToString(CultureInfo.InvariantCulture));
            extent.SetAttributeValue("cy", newCy.ToString(CultureInfo.InvariantCulture));

            // Also update nested DrawingML <a:xfrm><a:ext cx cy/>
            foreach (var aExt in drawingContainer.Descendants(A + "ext"))
            {
                if (aExt.Parent != null && aExt.Parent.Name == A + "xfrm")
                {
                    aExt.SetAttributeValue("cx", newCx.ToString(CultureInfo.InvariantCulture));
                    aExt.SetAttributeValue("cy", newCy.ToString(CultureInfo.InvariantCulture));
                }
            }

            return true;
        }

        private static long ParseLongAttr(XElement elem, string attrName)
        {
            var a = elem.Attribute(attrName);
            if (a == null) return 0;
            long val;
            return long.TryParse(a.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out val) ? val : 0;
        }

        public sealed class ExtractedPdfLine
        {
            public string Text;
            public double FontSize;
            public bool Bold;
        }

        /// <summary>
        /// Detects when an older Word (2007/2010), WPS Office, or Word's "Recover Text from Any File"
        /// filter mistakenly opens a PDF as raw binary text ("%PDF-1.4 ... 1 0 obj ... /FlateDecode")
        /// instead of using PDF Reflow.
        /// </summary>
        public static bool IsCorruptedRawPdfDocx(string docxPath)
        {
            if (string.IsNullOrWhiteSpace(docxPath) || !File.Exists(docxPath)) return true;
            try
            {
                using (var zip = ZipFile.OpenRead(docxPath))
                {
                    var entry = zip.GetEntry("word/document.xml");
                    if (entry == null) return true;
                    string xmlText;
                    using (var sr = new StreamReader(entry.Open(), Encoding.UTF8))
                    {
                        var xdoc = XDocument.Load(sr);
                        var sb = new StringBuilder();
                        foreach (var t in xdoc.Descendants(W + "t"))
                        {
                            sb.Append(t.Value);
                            if (sb.Length > 4000) break;
                        }
                        xmlText = sb.ToString();
                    }
                    if (string.IsNullOrWhiteSpace(xmlText)) return false;
                    string sample = xmlText.TrimStart();
                    bool hasPdfHeader = sample.StartsWith("%PDF-", StringComparison.OrdinalIgnoreCase) ||
                                        sample.IndexOf("%PDF-1.", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                        sample.IndexOf("%PDF-2.", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!hasPdfHeader) return false;
                    bool hasPdfStruct = sample.IndexOf("endobj", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                        sample.IndexOf("/FlateDecode", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                        sample.IndexOf("/Type", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                        sample.IndexOf("/Producer", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                        sample.IndexOf("0 obj", StringComparison.OrdinalIgnoreCase) >= 0;
                    return hasPdfStruct;
                }
            }
            catch
            {
                return true;
            }
        }

        /// <summary>
        /// Pure native .NET 4.8 OpenXML (.docx) generator from client pdf.js extracted lines.
        /// Guarantees clean editable Word output on any Windows laptop even without Word 2013+ or LibreOffice.
        /// </summary>
        public static void BuildDocxFromExtractedPages(string outDocxPath, List<List<ExtractedPdfLine>> pages)
        {
            var body = new XElement(W + "body");
            if (pages != null)
            {
                for (int pi = 0; pi < pages.Count; pi++)
                {
                    if (pi > 0)
                    {
                        body.Add(new XElement(W + "p", new XElement(W + "r", new XElement(W + "br", new XAttribute(W + "type", "page")))));
                    }
                    var lines = pages[pi];
                    if (lines == null) continue;
                    foreach (var line in lines)
                    {
                        if (line == null) continue;
                        string clean = StripInvalidXmlChars(line.Text ?? "");
                        if (string.IsNullOrWhiteSpace(clean)) continue;
                        double pt = line.FontSize > 6 && line.FontSize < 64 ? line.FontSize : 11.0;
                        int halfPt = (int)Math.Round(pt * 2.0);
                        var rPr = new XElement(W + "rPr",
                            new XElement(W + "rFonts", new XAttribute(W + "ascii", "Calibri"), new XAttribute(W + "hAnsi", "Calibri"))
                        );
                        if (line.Bold || pt >= 14.0)
                        {
                            rPr.Add(new XElement(W + "b"));
                            rPr.Add(new XElement(W + "bCs"));
                        }
                        rPr.Add(new XElement(W + "sz", new XAttribute(W + "val", halfPt.ToString(CultureInfo.InvariantCulture))));
                        rPr.Add(new XElement(W + "szCs", new XAttribute(W + "val", halfPt.ToString(CultureInfo.InvariantCulture))));

                        var pPr = new XElement(W + "pPr",
                            new XElement(W + "spacing",
                                new XAttribute(W + "after", pt >= 14.0 ? "120" : "80"),
                                new XAttribute(W + "line", "260"),
                                new XAttribute(W + "lineRule", "auto"))
                        );
                        var tEl = new XElement(W + "t", clean);
                        if (clean.StartsWith(" ") || clean.EndsWith(" ")) tEl.SetAttributeValue(XML + "space", "preserve");

                        body.Add(new XElement(W + "p", pPr, new XElement(W + "r", rPr, tEl)));
                    }
                }
            }
            body.Add(new XElement(W + "sectPr",
                new XElement(W + "pgSz", new XAttribute(W + "w", "11906"), new XAttribute(W + "h", "16838")),
                new XElement(W + "pgMar", new XAttribute(W + "top", "1080"), new XAttribute(W + "right", "1080"), new XAttribute(W + "bottom", "1080"), new XAttribute(W + "left", "1080"))
            ));

            var docXml = new XDocument(
                new XDeclaration("1.0", "UTF-8", "yes"),
                new XElement(W + "document", new XAttribute(XNamespace.Xmlns + "w", W.NamespaceName), body)
            );

            using (var fs = new FileStream(outDocxPath, FileMode.Create, FileAccess.Write))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                WriteUtf8Entry(zip, "[Content_Types].xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                    "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                    "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                    "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>" +
                    "</Types>");
                WriteUtf8Entry(zip, "_rels/.rels",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                    "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/>" +
                    "</Relationships>");
                var docEntry = zip.CreateEntry("word/document.xml", CompressionLevel.Optimal);
                using (var xw = XmlWriter.Create(docEntry.Open(), new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = false }))
                {
                    docXml.Save(xw);
                }
            }
        }

        public sealed class VisualPdfRunLine
        {
            public string Text;
            public double FontSize;
            public bool Bold;
            public bool Italic;
            public string ColorHex;
            public string FontFamily;
            public double LineHeightPt;
        }

        public sealed class VisualPdfTextBox
        {
            public double X;
            public double Y;
            public double W;
            public double H;
            public string Align;
            public List<VisualPdfRunLine> Lines = new List<VisualPdfRunLine>();
        }

        public sealed class VisualPdfPage
        {
            public double WidthPt;
            public double HeightPt;
            public byte[] BgImageBytes;
            public string BgImageExt;
            public List<VisualPdfTextBox> Boxes = new List<VisualPdfTextBox>();
        }

        /// <summary>
        /// Pure native .NET 4.8 Full Visual-Layout OpenXML (.docx) generator.
        /// Embeds each PDF page's text-free graphics/photos/sidebars/icons as a full-page behind-text background
        /// and overlays exact-positioned, transparent, color-matched editable Word Text Boxes (with both DrawingML
        /// wps:wsp for Word 2010-365/LibreOffice AND VML fallback for Word 2007/WPS Office).
        /// </summary>
        public static void BuildVisualDocxFromPages(string outDocxPath, List<VisualPdfPage> pages)
        {
            if (pages == null || pages.Count == 0)
                throw new ArgumentException("At least one visual page is required.");

            var body = new XElement(W + "body");
            var relEntries = new List<string>();
            relEntries.Add("<Relationship Id=\"rIdStyles\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>");
            var mediaFiles = new List<KeyValuePair<string, byte[]>>();
            int nextId = 1;

            double firstW = pages[0].WidthPt > 100 ? pages[0].WidthPt : 595.0;
            double firstH = pages[0].HeightPt > 100 ? pages[0].HeightPt : 842.0;

            for (int pi = 0; pi < pages.Count; pi++)
            {
                var pg = pages[pi];
                double pw = pg.WidthPt > 100 ? pg.WidthPt : firstW;
                double ph = pg.HeightPt > 100 ? pg.HeightPt : firstH;

                if (pi > 0)
                {
                    body.Add(new XElement(W + "p",
                        new XElement(W + "pPr", new XElement(W + "spacing", new XAttribute(W + "before", "0"), new XAttribute(W + "after", "0"), new XAttribute(W + "line", "20"), new XAttribute(W + "lineRule", "exact"))),
                        new XElement(W + "r", new XElement(W + "br", new XAttribute(W + "type", "page")))
                    ));
                }

                var pagePara = new XElement(W + "p",
                    new XElement(W + "pPr",
                        new XElement(W + "spacing",
                            new XAttribute(W + "before", "0"),
                            new XAttribute(W + "after", "0"),
                            new XAttribute(W + "line", "20"),
                            new XAttribute(W + "lineRule", "exact"))
                    )
                );

                // 1. Behind-text full-page graphics/photo/sidebar background layer
                if (pg.BgImageBytes != null && pg.BgImageBytes.Length > 0)
                {
                    string imgExt = string.Equals(pg.BgImageExt, "jpg", StringComparison.OrdinalIgnoreCase) ? "jpg" : "png";
                    string relId = "rIdBg" + (pi + 1).ToString(CultureInfo.InvariantCulture);
                    string mediaTarget = "media/page_bg_" + (pi + 1).ToString(CultureInfo.InvariantCulture) + "." + imgExt;
                    relEntries.Add("<Relationship Id=\"" + relId + "\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/image\" Target=\"" + mediaTarget + "\"/>");
                    mediaFiles.Add(new KeyValuePair<string, byte[]>("word/" + mediaTarget, pg.BgImageBytes));

                    long cx = (long)Math.Round(pw * EmuPerPt);
                    long cy = (long)Math.Round(ph * EmuPerPt);
                    int docPrId = nextId++;
                    int cNvPrId = nextId++;

                    var bgDrawing = new XElement(W + "drawing",
                        new XElement(WP + "anchor",
                            new XAttribute("distT", "0"), new XAttribute("distB", "0"),
                            new XAttribute("distL", "0"), new XAttribute("distR", "0"),
                            new XAttribute("simplePos", "0"),
                            new XAttribute("relativeHeight", "100"),
                            new XAttribute("behindDoc", "1"),
                            new XAttribute("locked", "0"),
                            new XAttribute("layoutInCell", "1"),
                            new XAttribute("allowOverlap", "1"),
                            new XElement(WP + "simplePos", new XAttribute("x", "0"), new XAttribute("y", "0")),
                            new XElement(WP + "positionH", new XAttribute("relativeFrom", "page"),
                                new XElement(WP + "posOffset", "0")),
                            new XElement(WP + "positionV", new XAttribute("relativeFrom", "page"),
                                new XElement(WP + "posOffset", "0")),
                            new XElement(WP + "extent", new XAttribute("cx", cx.ToString(CultureInfo.InvariantCulture)), new XAttribute("cy", cy.ToString(CultureInfo.InvariantCulture))),
                            new XElement(WP + "effectExtent", new XAttribute("l", "0"), new XAttribute("t", "0"), new XAttribute("r", "0"), new XAttribute("b", "0")),
                            new XElement(WP + "wrapNone"),
                            new XElement(WP + "docPr", new XAttribute("id", docPrId.ToString(CultureInfo.InvariantCulture)), new XAttribute("name", "PageBg" + (pi + 1).ToString(CultureInfo.InvariantCulture))),
                            new XElement(WP + "cNvGraphicFramePr",
                                new XElement(A + "graphicFrameLocks", new XAttribute("noChangeAspect", "1"))),
                            new XElement(A + "graphic",
                                new XElement(A + "graphicData", new XAttribute("uri", "http://schemas.openxmlformats.org/drawingml/2006/picture"),
                                    new XElement(PIC + "pic",
                                        new XElement(PIC + "nvPicPr",
                                            new XElement(PIC + "cNvPr", new XAttribute("id", cNvPrId.ToString(CultureInfo.InvariantCulture)), new XAttribute("name", mediaTarget)),
                                            new XElement(PIC + "cNvPicPr")),
                                        new XElement(PIC + "blipFill",
                                            new XElement(A + "blip", new XAttribute(R + "embed", relId)),
                                            new XElement(A + "stretch", new XElement(A + "fillRect"))),
                                        new XElement(PIC + "spPr",
                                            new XElement(A + "xfrm",
                                                new XElement(A + "off", new XAttribute("x", "0"), new XAttribute("y", "0")),
                                                new XElement(A + "ext", new XAttribute("cx", cx.ToString(CultureInfo.InvariantCulture)), new XAttribute("cy", cy.ToString(CultureInfo.InvariantCulture)))),
                                            new XElement(A + "prstGeom", new XAttribute("prst", "rect"), new XElement(A + "avLst"))))))
                        )
                    );
                    pagePara.Add(new XElement(W + "r", bgDrawing));
                }

                // 2. Exact-positioned editable floating text boxes
                if (pg.Boxes != null)
                {
                    for (int bi = 0; bi < pg.Boxes.Count; bi++)
                    {
                        var box = pg.Boxes[bi];
                        if (box == null || box.Lines == null || box.Lines.Count == 0) continue;

                        double bx = Math.Max(0, box.X);
                        double by = Math.Max(0, box.Y);
                        double bw = Math.Max(16, Math.Min(pw - bx, box.W));
                        double bh = Math.Max(12, Math.Min(ph - by, box.H));

                        long xEmu = (long)Math.Round(bx * EmuPerPt);
                        long yEmu = (long)Math.Round(by * EmuPerPt);
                        long wEmu = (long)Math.Round(bw * EmuPerPt);
                        long hEmu = (long)Math.Round(bh * EmuPerPt);
                        int tbDocPrId = nextId++;

                        var txbxChoiceContent = BuildTextBoxContentElement(box);
                        var txbxFallbackContent = BuildTextBoxContentElement(box);

                        // Modern DrawingML text box (Word 2010, 2013, 2016, 2019, 2021, 365, LibreOffice)
                        var choiceDrawing = new XElement(W + "drawing",
                            new XElement(WP + "anchor",
                                new XAttribute("distT", "0"), new XAttribute("distB", "0"),
                                new XAttribute("distL", "0"), new XAttribute("distR", "0"),
                                new XAttribute("simplePos", "0"),
                                new XAttribute("relativeHeight", (251658240 + bi).ToString(CultureInfo.InvariantCulture)),
                                new XAttribute("behindDoc", "0"),
                                new XAttribute("locked", "0"),
                                new XAttribute("layoutInCell", "1"),
                                new XAttribute("allowOverlap", "1"),
                                new XElement(WP + "simplePos", new XAttribute("x", "0"), new XAttribute("y", "0")),
                                new XElement(WP + "positionH", new XAttribute("relativeFrom", "page"),
                                    new XElement(WP + "posOffset", xEmu.ToString(CultureInfo.InvariantCulture))),
                                new XElement(WP + "positionV", new XAttribute("relativeFrom", "page"),
                                    new XElement(WP + "posOffset", yEmu.ToString(CultureInfo.InvariantCulture))),
                                new XElement(WP + "extent",
                                    new XAttribute("cx", wEmu.ToString(CultureInfo.InvariantCulture)),
                                    new XAttribute("cy", hEmu.ToString(CultureInfo.InvariantCulture))),
                                new XElement(WP + "effectExtent", new XAttribute("l", "0"), new XAttribute("t", "0"), new XAttribute("r", "0"), new XAttribute("b", "0")),
                                new XElement(WP + "wrapNone"),
                                new XElement(WP + "docPr",
                                    new XAttribute("id", tbDocPrId.ToString(CultureInfo.InvariantCulture)),
                                    new XAttribute("name", "TextBox_" + (pi + 1).ToString(CultureInfo.InvariantCulture) + "_" + (bi + 1).ToString(CultureInfo.InvariantCulture))),
                                new XElement(WP + "cNvGraphicFramePr"),
                                new XElement(A + "graphic",
                                    new XElement(A + "graphicData",
                                        new XAttribute("uri", "http://schemas.microsoft.com/office/word/2010/wordprocessingShape"),
                                        new XElement(WPS + "wsp",
                                            new XElement(WPS + "cNvSpPr", new XAttribute("txBox", "1")),
                                            new XElement(WPS + "spPr",
                                                new XElement(A + "xfrm",
                                                    new XElement(A + "off", new XAttribute("x", "0"), new XAttribute("y", "0")),
                                                    new XElement(A + "ext",
                                                        new XAttribute("cx", wEmu.ToString(CultureInfo.InvariantCulture)),
                                                        new XAttribute("cy", hEmu.ToString(CultureInfo.InvariantCulture)))),
                                                new XElement(A + "prstGeom", new XAttribute("prst", "rect"), new XElement(A + "avLst")),
                                                new XElement(A + "noFill"),
                                                new XElement(A + "ln", new XElement(A + "noFill"))),
                                            new XElement(WPS + "txbx", txbxChoiceContent),
                                            new XElement(WPS + "bodyPr",
                                                new XAttribute("rot", "0"),
                                                new XAttribute("vert", "horz"),
                                                new XAttribute("wrap", "square"),
                                                new XAttribute("lIns", "0"),
                                                new XAttribute("tIns", "0"),
                                                new XAttribute("rIns", "0"),
                                                new XAttribute("bIns", "0"),
                                                new XAttribute("anchor", "t"),
                                                new XAttribute("anchorCtr", "0"),
                                                new XElement(A + "spAutoFit")))))
                            )
                        );

                        // VML fallback text box (Word 2007 & legacy WPS Office)
                        string vmlStyle = string.Format(CultureInfo.InvariantCulture,
                            "position:absolute;margin-left:{0:0.##}pt;margin-top:{1:0.##}pt;width:{2:0.##}pt;height:{3:0.##}pt;z-index:{4};mso-position-horizontal-relative:page;mso-position-vertical-relative:page",
                            bx, by, bw, bh, 100 + bi);
                        var fallbackPict = new XElement(W + "pict",
                            new XElement(V + "rect",
                                new XAttribute("id", "_x0000_s" + (1025 + tbDocPrId).ToString(CultureInfo.InvariantCulture)),
                                new XAttribute("style", vmlStyle),
                                new XAttribute("filled", "f"),
                                new XAttribute("stroked", "f"),
                                new XElement(V + "textbox",
                                    new XAttribute("style", "mso-fit-shape-to-text:t"),
                                    new XAttribute("inset", "0,0,0,0"),
                                    txbxFallbackContent)
                            )
                        );

                        var altContent = new XElement(MC + "AlternateContent",
                            new XElement(MC + "Choice", new XAttribute("Requires", "wps"), choiceDrawing),
                            new XElement(MC + "Fallback", fallbackPict)
                        );

                        pagePara.Add(new XElement(W + "r", altContent));
                    }
                }

                body.Add(pagePara);
            }

            long pgWTwips = (long)Math.Round(firstW * 20.0);
            long pgHTwips = (long)Math.Round(firstH * 20.0);
            var pgSzEl = new XElement(W + "pgSz",
                new XAttribute(W + "w", pgWTwips.ToString(CultureInfo.InvariantCulture)),
                new XAttribute(W + "h", pgHTwips.ToString(CultureInfo.InvariantCulture)));
            if (firstW > firstH) pgSzEl.SetAttributeValue(W + "orient", "landscape");

            body.Add(new XElement(W + "sectPr",
                pgSzEl,
                new XElement(W + "pgMar",
                    new XAttribute(W + "top", "360"),
                    new XAttribute(W + "right", "360"),
                    new XAttribute(W + "bottom", "360"),
                    new XAttribute(W + "left", "360"),
                    new XAttribute(W + "header", "360"),
                    new XAttribute(W + "footer", "360"),
                    new XAttribute(W + "gutter", "0"))
            ));

            var docXml = new XDocument(
                new XDeclaration("1.0", "UTF-8", "yes"),
                new XElement(W + "document",
                    new XAttribute(XNamespace.Xmlns + "w", W.NamespaceName),
                    new XAttribute(XNamespace.Xmlns + "r", R.NamespaceName),
                    new XAttribute(XNamespace.Xmlns + "wp", WP.NamespaceName),
                    new XAttribute(XNamespace.Xmlns + "a", A.NamespaceName),
                    new XAttribute(XNamespace.Xmlns + "pic", PIC.NamespaceName),
                    new XAttribute(XNamespace.Xmlns + "wps", WPS.NamespaceName),
                    new XAttribute(XNamespace.Xmlns + "mc", MC.NamespaceName),
                    new XAttribute(XNamespace.Xmlns + "v", V.NamespaceName),
                    new XAttribute(MC + "Ignorable", "wps"),
                    body
                )
            );

            using (var fs = new FileStream(outDocxPath, FileMode.Create, FileAccess.Write))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                WriteUtf8Entry(zip, "[Content_Types].xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                    "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                    "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                    "<Default Extension=\"png\" ContentType=\"image/png\"/>" +
                    "<Default Extension=\"jpg\" ContentType=\"image/jpeg\"/>" +
                    "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>" +
                    "<Override PartName=\"/word/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml\"/>" +
                    "</Types>");
                WriteUtf8Entry(zip, "_rels/.rels",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                    "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/>" +
                    "</Relationships>");
                WriteUtf8Entry(zip, "word/_rels/document.xml.rels",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                    string.Join("", relEntries.ToArray()) +
                    "</Relationships>");
                WriteUtf8Entry(zip, "word/styles.xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<w:styles xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">" +
                    "<w:docDefaults><w:rPrDefault><w:rPr><w:rFonts w:ascii=\"Calibri\" w:hAnsi=\"Calibri\" w:cs=\"Calibri\"/><w:sz w:val=\"22\"/><w:szCs w:val=\"22\"/></w:rPr></w:rPrDefault>" +
                    "<w:pPrDefault><w:pPr><w:spacing w:before=\"0\" w:after=\"0\" w:line=\"240\" w:lineRule=\"auto\"/></w:pPr></w:pPrDefault></w:docDefaults>" +
                    "<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/></w:style>" +
                    "</w:styles>");

                foreach (var mf in mediaFiles)
                {
                    var me = zip.CreateEntry(mf.Key, CompressionLevel.NoCompression);
                    using (var ms = me.Open())
                        ms.Write(mf.Value, 0, mf.Value.Length);
                }

                var docEntry = zip.CreateEntry("word/document.xml", CompressionLevel.Optimal);
                using (var xw = XmlWriter.Create(docEntry.Open(), new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = false }))
                {
                    docXml.Save(xw);
                }
            }
        }

        private static XElement BuildTextBoxContentElement(VisualPdfTextBox box)
        {
            var content = new XElement(W + "txbxContent");
            string align = (box.Align ?? "left").Trim().ToLowerInvariant();
            for (int i = 0; i < box.Lines.Count; i++)
            {
                var line = box.Lines[i];
                if (line == null) continue;
                string clean = StripInvalidXmlChars(line.Text ?? "");
                double pt = line.FontSize >= 5.5 && line.FontSize <= 72 ? line.FontSize : 10.5;
                int halfPt = (int)Math.Round(pt * 2.0);
                string fontFam = string.IsNullOrWhiteSpace(line.FontFamily) ? "Calibri" : line.FontFamily.Trim();
                string colorHex = SanitizeHexColor(line.ColorHex);

                var spacingEl = new XElement(W + "spacing",
                    new XAttribute(W + "before", "0"),
                    new XAttribute(W + "after", "0"));
                if (line.LineHeightPt >= pt * 0.9 && line.LineHeightPt <= pt * 2.5)
                {
                    int lineTwips = (int)Math.Round(line.LineHeightPt * 20.0);
                    spacingEl.SetAttributeValue(W + "line", lineTwips.ToString(CultureInfo.InvariantCulture));
                    spacingEl.SetAttributeValue(W + "lineRule", "atLeast");
                }
                else
                {
                    spacingEl.SetAttributeValue(W + "line", "240");
                    spacingEl.SetAttributeValue(W + "lineRule", "auto");
                }

                var pPr = new XElement(W + "pPr", spacingEl);
                if (align == "center" || align == "right")
                    pPr.Add(new XElement(W + "jc", new XAttribute(W + "val", align)));

                // Strict OOXML CT_RPr element order: rFonts -> b/bCs -> i/iCs -> color -> sz/szCs
                var rPr = new XElement(W + "rPr",
                    new XElement(W + "rFonts",
                        new XAttribute(W + "ascii", fontFam),
                        new XAttribute(W + "hAnsi", fontFam),
                        new XAttribute(W + "cs", fontFam))
                );
                if (line.Bold)
                {
                    rPr.Add(new XElement(W + "b"));
                    rPr.Add(new XElement(W + "bCs"));
                }
                if (line.Italic)
                {
                    rPr.Add(new XElement(W + "i"));
                    rPr.Add(new XElement(W + "iCs"));
                }
                rPr.Add(new XElement(W + "color", new XAttribute(W + "val", colorHex)));
                rPr.Add(new XElement(W + "sz", new XAttribute(W + "val", halfPt.ToString(CultureInfo.InvariantCulture))));
                rPr.Add(new XElement(W + "szCs", new XAttribute(W + "val", halfPt.ToString(CultureInfo.InvariantCulture))));

                var tEl = new XElement(W + "t", clean);
                if (clean.StartsWith(" ") || clean.EndsWith(" "))
                    tEl.SetAttributeValue(XML + "space", "preserve");

                content.Add(new XElement(W + "p", pPr, new XElement(W + "r", rPr, tEl)));
            }
            if (!content.HasElements)
                content.Add(new XElement(W + "p"));
            return content;
        }

        private static string SanitizeHexColor(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "111827";
            string s = raw.Trim().TrimStart('#').ToUpperInvariant();
            if (s.Length == 3)
                s = "" + s[0] + s[0] + s[1] + s[1] + s[2] + s[2];
            if (s.Length != 6) return "111827";
            for (int i = 0; i < 6; i++)
            {
                char c = s[i];
                bool ok = (c >= '0' && c <= '9') || (c >= 'A' && c <= 'F');
                if (!ok) return "111827";
            }
            return s;
        }

        private static void WriteUtf8Entry(ZipArchive zip, string path, string content)
        {
            var e = zip.CreateEntry(path, CompressionLevel.Optimal);
            using (var sw = new StreamWriter(e.Open(), new UTF8Encoding(false)))
                sw.Write(content);
        }

        private static string StripInvalidXmlChars(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char ch = s[i];
                if (ch == 0x9 || ch == 0xA || ch == 0xD || (ch >= 0x20 && ch <= 0xD7FF) || (ch >= 0xE000 && ch <= 0xFFFD))
                    sb.Append(ch);
            }
            return sb.ToString();
        }
    }
}
