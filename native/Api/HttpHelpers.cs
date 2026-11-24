using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace ScanDrop.Native.Api
{
    public sealed class MultipartFilePart
    {
        public string FileName;
        public string ContentType;
        public byte[] Bytes;
    }

    public static class HttpHelpers
    {
        public static void AddPublicCors(HttpListenerResponse res)
        {
            res.AddHeader("Access-Control-Allow-Origin", "*");
            res.AddHeader("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            res.AddHeader("Access-Control-Allow-Headers",
                "Content-Type, X-Requested-With, X-Upload-Token, X-Session-Id, X-File-Id, X-Chunk-Index, X-Byte-Offset, X-Chunk-Sha256, X-Idempotency-Key");
        }

        public static void AddLocalCors(HttpListenerResponse res)
        {
            res.AddHeader("Access-Control-Allow-Origin", "*");
            res.AddHeader("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            res.AddHeader("Access-Control-Allow-Headers", "Content-Type, X-Requested-With");
        }

        public static void WriteJson(HttpListenerResponse res, int status, object obj)
        {
            var ser = new JavaScriptSerializer { MaxJsonLength = 64 * 1024 * 1024 };
            byte[] buf = Encoding.UTF8.GetBytes(ser.Serialize(obj));
            res.StatusCode = status;
            res.ContentType = "application/json; charset=utf-8";
            res.ContentLength64 = buf.Length;
            res.OutputStream.Write(buf, 0, buf.Length);
            res.Close();
        }

        public static void WriteHtml(HttpListenerResponse res, int status, string html)
        {
            byte[] buf = Encoding.UTF8.GetBytes(html ?? "");
            res.StatusCode = status;
            res.ContentType = "text/html; charset=utf-8";
            res.ContentLength64 = buf.Length;
            res.OutputStream.Write(buf, 0, buf.Length);
            res.Close();
        }

        public static Dictionary<string, object> ReadJsonBody(HttpListenerRequest req, int maxBytes = 4 * 1024 * 1024)
        {
            using (var ms = new MemoryStream())
            {
                byte[] buf = new byte[16384];
                int r;
                long total = 0;
                while ((r = req.InputStream.Read(buf, 0, buf.Length)) > 0)
                {
                    total += r;
                    if (total > maxBytes) throw new InvalidOperationException("JSON body too large.");
                    ms.Write(buf, 0, r);
                }
                string raw = Encoding.UTF8.GetString(ms.ToArray());
                if (string.IsNullOrWhiteSpace(raw)) return new Dictionary<string, object>();
                return new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 }.Deserialize<Dictionary<string, object>>(raw)
                    ?? new Dictionary<string, object>();
            }
        }

        public static string GetStr(Dictionary<string, object> d, string key)
        {
            object v;
            return d != null && d.TryGetValue(key, out v) && v != null ? Convert.ToString(v) : null;
        }

        public static long GetLong(Dictionary<string, object> d, string key, long fallback = 0)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v) || v == null) return fallback;
            long parsed;
            return long.TryParse(Convert.ToString(v), out parsed) ? parsed : fallback;
        }

        public static int GetInt(Dictionary<string, object> d, string key, int fallback = 0)
        {
            return (int)GetLong(d, key, fallback);
        }

        public static bool GetBool(Dictionary<string, object> d, string key, bool fallback = false)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v) || v == null) return fallback;
            if (v is bool) return (bool)v;
            string s = Convert.ToString(v);
            if (s.Equals("true", StringComparison.OrdinalIgnoreCase) || s == "1") return true;
            if (s.Equals("false", StringComparison.OrdinalIgnoreCase) || s == "0") return false;
            return fallback;
        }

        public static void ParseMultipart(HttpListenerRequest req, long maxBodyBytes, out Dictionary<string, string> fields, out List<MultipartFilePart> files)
        {
            byte[] buffer;
            using (var ms = new MemoryStream())
            {
                byte[] chunk = new byte[65536];
                int r;
                long total = 0;
                while ((r = req.InputStream.Read(chunk, 0, chunk.Length)) > 0)
                {
                    total += r;
                    if (total > maxBodyBytes)
                        throw new InvalidOperationException("Request payload exceeds maximum multipart size limit.");
                    ms.Write(chunk, 0, r);
                }
                buffer = ms.ToArray();
            }

            fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            files = new List<MultipartFilePart>();
            string contentType = req.ContentType;
            if (string.IsNullOrEmpty(contentType)) throw new InvalidOperationException("Missing Content-Type");

            var m = Regex.Match(contentType, @"boundary=(?:""([^""]+)""|([^;\s]+))", RegexOptions.IgnoreCase);
            if (!m.Success) throw new InvalidOperationException("Missing multipart boundary");
            byte[] boundary = Encoding.ASCII.GetBytes("--" + (m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value));
            byte[] headerSep = new byte[] { 13, 10, 13, 10 };

            int pos = IndexOfBytes(buffer, boundary, 0);
            while (pos != -1)
            {
                pos += boundary.Length;
                if (pos + 1 < buffer.Length && buffer[pos] == 45 && buffer[pos + 1] == 45) break;
                if (pos + 1 < buffer.Length && buffer[pos] == 13 && buffer[pos + 1] == 10) pos += 2;

                int next = IndexOfBytes(buffer, boundary, pos);
                if (next == -1) break;

                int partEnd = next;
                if (partEnd >= 2 && buffer[partEnd - 2] == 13 && buffer[partEnd - 1] == 10) partEnd -= 2;

                int hdrEnd = IndexOfBytes(buffer, headerSep, pos);
                if (hdrEnd == -1 || hdrEnd > partEnd) { pos = next; continue; }

                string hdr = Encoding.UTF8.GetString(buffer, pos, hdrEnd - pos);
                int bodyStart = hdrEnd + 4;
                int bodyLen = partEnd - bodyStart;

                var nameMatch = Regex.Match(hdr, @"name=""([^""]+)""", RegexOptions.IgnoreCase);
                var fileMatch = Regex.Match(hdr, @"filename=""([^""]*)""", RegexOptions.IgnoreCase);
                var ctMatch = Regex.Match(hdr, @"Content-Type:\s*([^\r\n]+)", RegexOptions.IgnoreCase);

                if (nameMatch.Success)
                {
                    if (fileMatch.Success && !string.IsNullOrEmpty(fileMatch.Groups[1].Value))
                    {
                        var bytes = new byte[bodyLen];
                        Buffer.BlockCopy(buffer, bodyStart, bytes, 0, bodyLen);
                        files.Add(new MultipartFilePart
                        {
                            FileName = Path.GetFileName(fileMatch.Groups[1].Value),
                            ContentType = ctMatch.Success ? ctMatch.Groups[1].Value.Trim() : "application/octet-stream",
                            Bytes = bytes
                        });
                    }
                    else
                    {
                        fields[nameMatch.Groups[1].Value] = Encoding.UTF8.GetString(buffer, bodyStart, bodyLen);
                    }
                }
                pos = next;
            }
        }

        private static int IndexOfBytes(byte[] haystack, byte[] needle, int start)
        {
            int max = haystack.Length - needle.Length;
            for (int i = start; i <= max; i++)
            {
                bool match = true;
                for (int j = 0; j < needle.Length; j++)
                {
                    if (haystack[i + j] != needle[j]) { match = false; break; }
                }
                if (match) return i;
            }
            return -1;
        }
    }
}
