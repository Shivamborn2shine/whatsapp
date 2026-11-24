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
        private static readonly XNamespace WP = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
        private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
        private static readonly XNamespace XML = "http://www.w3.org/XML/1998/namespace";
        private const double EmuPerCm = 360000.0;

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
    }
}
