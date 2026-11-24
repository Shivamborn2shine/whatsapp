using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;

namespace ScanDrop.Native.Core
{
    public sealed class ShopPairResult
    {
        public string ShopId;
        public string ShopSlug;
        public string ShopName;
        public string DeviceId;
        public string DeviceSecret;
        public string PairingCode;
        public string SubscriptionStatus;
    }

    public static class ShopPairingClient
    {
        public static bool PairWithCloud(
            string cloudBaseUrl,
            string pairingCode,
            string machineFingerprint,
            string appVersion,
            out ShopPairResult result,
            out string error)
        {
            result = null;
            error = null;

            string code = (pairingCode ?? "").Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(code))
            {
                error = "Please enter the Pairing Code provided by your Scan Drop administrator.";
                return false;
            }

            string baseUrl = (string.IsNullOrWhiteSpace(cloudBaseUrl) ? "https://scandrop-xi.vercel.app" : cloudBaseUrl).TrimEnd('/');
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
                var ser = new JavaScriptSerializer();
                string payload = ser.Serialize(new
                {
                    pairingCode = code,
                    deviceName = Environment.MachineName,
                    machineFingerprint = machineFingerprint ?? "",
                    windowsVersion = Environment.OSVersion.VersionString,
                    appVersion = appVersion ?? "4.1.0"
                });

                var req = (HttpWebRequest)WebRequest.Create(baseUrl + "/api/pair");
                req.Method = "POST";
                req.ContentType = "application/json";
                req.Timeout = 15000;
                req.UserAgent = "ScanDrop-Station/" + (appVersion ?? "4.1.0");
                byte[] bytes = Encoding.UTF8.GetBytes(payload);
                req.ContentLength = bytes.Length;
                using (var s = req.GetRequestStream()) s.Write(bytes, 0, bytes.Length);

                string responseText;
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var reader = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                {
                    responseText = reader.ReadToEnd();
                }

                var dict = ser.Deserialize<Dictionary<string, object>>(responseText);
                if (dict == null || !dict.ContainsKey("success") || !Convert.ToBoolean(dict["success"]))
                {
                    error = dict != null && dict.ContainsKey("error")
                        ? Convert.ToString(dict["error"])
                        : "Pairing failed.";
                    return false;
                }

                var shopObj = dict.ContainsKey("shop") ? dict["shop"] as Dictionary<string, object> : null;
                if (shopObj == null)
                {
                    error = "Cloud returned an invalid shop profile.";
                    return false;
                }

                result = new ShopPairResult
                {
                    ShopId = shopObj.ContainsKey("id") ? Convert.ToString(shopObj["id"]) : "",
                    ShopSlug = shopObj.ContainsKey("slug") ? Convert.ToString(shopObj["slug"]) : "",
                    ShopName = shopObj.ContainsKey("name") ? Convert.ToString(shopObj["name"]) : "",
                    SubscriptionStatus = shopObj.ContainsKey("subscriptionStatus") ? Convert.ToString(shopObj["subscriptionStatus"]) : "active",
                    DeviceId = dict.ContainsKey("deviceId") ? Convert.ToString(dict["deviceId"]) : "",
                    DeviceSecret = dict.ContainsKey("deviceSecret") ? Convert.ToString(dict["deviceSecret"]) : "",
                    PairingCode = code
                };

                if (string.IsNullOrWhiteSpace(result.ShopSlug) || string.IsNullOrWhiteSpace(result.DeviceSecret))
                {
                    error = "Incomplete pairing response from server.";
                    return false;
                }

                return true;
            }
            catch (WebException wex)
            {
                if (wex.Response != null)
                {
                    try
                    {
                        using (var reader = new StreamReader(wex.Response.GetResponseStream(), Encoding.UTF8))
                        {
                            string errBody = reader.ReadToEnd();
                            var ser = new JavaScriptSerializer();
                            var dict = ser.Deserialize<Dictionary<string, object>>(errBody);
                            if (dict != null && dict.ContainsKey("error"))
                            {
                                error = Convert.ToString(dict["error"]);
                                return false;
                            }
                        }
                    }
                    catch { }
                }
                error = "Could not reach Scan Drop cloud (" + wex.Message + "). Check your internet connection.";
                return false;
            }
            catch (Exception ex)
            {
                error = "Pairing error: " + ex.Message;
                return false;
            }
        }
    }
}
