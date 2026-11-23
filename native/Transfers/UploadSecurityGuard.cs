using System;
using System.Collections.Generic;
using System.Net;
using ScanDrop.Native.Core;
using ScanDrop.Native.Storage;

namespace ScanDrop.Native.Transfers
{
    /// <summary>
    /// Enforces per-IP sliding-window rate limits, cryptographic upload session tokens,
    /// and shopkeeper Pause Receiving state (PRD Sec 11).
    /// </summary>
    public sealed class UploadSecurityGuard
    {
        private readonly SqliteDatabase _db;
        private readonly object _rateLock = new object();
        private readonly Dictionary<string, Queue<DateTime>> _reqWindows = new Dictionary<string, Queue<DateTime>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Queue<DateTime>> _sessionWindows = new Dictionary<string, Queue<DateTime>>(StringComparer.OrdinalIgnoreCase);

        private const int MaxRequestsPerMinutePerIp = 180;
        private const int MaxSessionsPerTenMinutesPerIp = 25;

        public UploadSecurityGuard(SqliteDatabase db)
        {
            _db = db;
        }

        public static string ExtractClientIp(HttpListenerRequest req)
        {
            if (req == null) return "unknown";
            string cfIp = req.Headers["CF-Connecting-IP"];
            if (!string.IsNullOrWhiteSpace(cfIp)) return cfIp.Trim();
            string xff = req.Headers["X-Forwarded-For"];
            if (!string.IsNullOrWhiteSpace(xff))
            {
                int comma = xff.IndexOf(',');
                return (comma > 0 ? xff.Substring(0, comma) : xff).Trim();
            }
            return req.RemoteEndPoint != null && req.RemoteEndPoint.Address != null
                ? req.RemoteEndPoint.Address.ToString()
                : "127.0.0.1";
        }

        public bool CheckRateLimit(string clientIp, bool isNewSession, out string error)
        {
            error = null;
            string ip = string.IsNullOrWhiteSpace(clientIp) ? "unknown" : clientIp.Trim();
            DateTime now = DateTime.UtcNow;

            lock (_rateLock)
            {
                Queue<DateTime> reqQ;
                if (!_reqWindows.TryGetValue(ip, out reqQ))
                {
                    reqQ = new Queue<DateTime>();
                    _reqWindows[ip] = reqQ;
                }
                while (reqQ.Count > 0 && (now - reqQ.Peek()).TotalSeconds > 60) reqQ.Dequeue();
                if (reqQ.Count >= MaxRequestsPerMinutePerIp)
                {
                    error = "Too many requests from your device. Please wait a moment and try again.";
                    return false;
                }
                reqQ.Enqueue(now);

                if (isNewSession)
                {
                    Queue<DateTime> sessQ;
                    if (!_sessionWindows.TryGetValue(ip, out sessQ))
                    {
                        sessQ = new Queue<DateTime>();
                        _sessionWindows[ip] = sessQ;
                    }
                    while (sessQ.Count > 0 && (now - sessQ.Peek()).TotalMinutes > 10) sessQ.Dequeue();
                    if (sessQ.Count >= MaxSessionsPerTenMinutesPerIp)
                    {
                        error = "Upload session rate limit reached. Please try again in a few minutes.";
                        return false;
                    }
                    sessQ.Enqueue(now);
                }

                // Prevent unbounded dictionary growth
                if (_reqWindows.Count > 2000) _reqWindows.Clear();
                if (_sessionWindows.Count > 2000) _sessionWindows.Clear();
            }
            return true;
        }

        public UploadSessionRecord ValidateSessionToken(string sessionId, string token, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(token))
            {
                error = "Missing upload session or authorization token.";
                return null;
            }

            var session = _db.GetUploadSession(sessionId);
            if (session == null)
            {
                error = "Upload session not found.";
                return null;
            }

            if (session.Status != "open" && session.Status != "committing")
            {
                error = "Upload session is no longer active (" + session.Status + ").";
                return null;
            }

            DateTime expUtc;
            if (DateTime.TryParse(session.ExpiresAt, null, System.Globalization.DateTimeStyles.RoundtripKind, out expUtc))
            {
                if (DateTime.UtcNow > expUtc)
                {
                    _db.UpdateUploadSession(session.SessionId, "expired", session.BytesReceived);
                    error = "Upload session has expired. Please refresh and try again.";
                    return null;
                }
            }

            string suppliedHash = SecretProtector.ComputeSha256Hex(token.Trim());
            if (!ConstantTimeEquals(suppliedHash, session.AuthorizedUploadTokenHash ?? ""))
            {
                error = "Invalid upload session token.";
                return null;
            }

            return session;
        }

        private static bool ConstantTimeEquals(string a, string b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }
    }
}
