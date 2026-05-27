using Amazon;
using Amazon.Rekognition;
using Amazon.Rekognition.Model;
using Amazon.S3;
using Amazon.S3.Model;
using IFNRCONFaceSearch.Models;
using Microsoft.AspNetCore.Mvc;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IFNRCONFaceSearch.Controllers
{
    public class FaceSearchController : Controller
    {
        private readonly IConfiguration _config;
        private const float THRESHOLD = 60f;

        private static readonly Dictionary<string, string> DriveFolders = new()
        {
            { "9-april",  "13vjlfWvT6AEY1ueWOPIBVKOPo5j6Z5qV" },
            { "10-april", "1PPQx0MJ7CEtXMUvDVHfQdIg0yitvHSQ4" },
            { "11-april", "1wC-8lBSi7AcDvy8q-hSvTKTYI3WjMHof" },
            { "12-april", "14rgCytwUSHtQFoBR1IbPR7TS8EhTT3-2" },
        };

        public FaceSearchController(IConfiguration config)
        {
            _config = config;
        }

        // ── Helpers ───────────────────────────────────────────────────────────
        private bool IsLoggedIn() => HttpContext.Session.GetString("face_search_auth") == "true";
        private string GetConnectionString()
        {
            var server = _config["Database:Server"];
            var port = _config["Database:Port"];
            var database = _config["Database:Name"];
            var user = _config["Database:User"];
            var password = _config["Database:Password"];

            // Fallback to hardcoded if config not reading
            if (string.IsNullOrEmpty(server)) server = "ifnr.org";
            if (string.IsNullOrEmpty(port)) port = "3306";
            if (string.IsNullOrEmpty(database)) database = "ifnruser123_db";
            if (string.IsNullOrEmpty(user)) user = "ifnruser123_root";
            if (string.IsNullOrEmpty(password)) password = "6Hsds;UimhZ(";

            return new MySql.Data.MySqlClient.MySqlConnectionStringBuilder
            {
                Server = server,
                Port = uint.Parse(port),
                Database = database,
                UserID = user,
                Password = password,
                SslMode = MySql.Data.MySqlClient.MySqlSslMode.Disabled,
                AllowPublicKeyRetrieval = true,
                ConnectionTimeout = 30,
                DefaultCommandTimeout = 30
            }.ConnectionString;
        }
        private AmazonRekognitionClient GetRekognition()
        {
            return new AmazonRekognitionClient(
                _config["AWS:AccessKey"],
                _config["AWS:SecretKey"],
                RegionEndpoint.GetBySystemName(_config["AWS:Region"] ?? "us-east-1")
            );
        }

        private AmazonS3Client GetS3()
        {
            return new AmazonS3Client(
                _config["AWS:AccessKey"],
                _config["AWS:SecretKey"],
                RegionEndpoint.GetBySystemName(_config["AWS:Region"] ?? "us-east-1")
            );
        }

        private string GetS3Url(string s3Key)
        {
            var bucket = _config["AWS:Bucket"] ?? "our-photos26";
            var region = _config["AWS:Region"] ?? "us-east-1";
            return $"https://{bucket}.s3.{region}.amazonaws.com/{s3Key}";
        }

        private string IndexPath(string day) =>
            Path.Combine(Directory.GetCurrentDirectory(), "App_Data", "facesets", $"{day}-aws.json");

        // ── LOGIN ─────────────────────────────────────────────────────────────
        [HttpGet]
        public IActionResult Login()
        {
            if (IsLoggedIn()) return RedirectToAction("Index");
            return View(new LoginViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DoLogin(LoginViewModel model)
        {
            if (!ModelState.IsValid) return View("Login", model);

            var regId = model.RegistrationId.Trim();
            var email = model.Email.Trim().ToLower();

            // ── Demo login for portfolio/resume ──────────────────────────────
            if ((regId == "DEMO123" || regId == "demo123") &&
                 email == "demo@facesearch.com")
            {
                HttpContext.Session.SetString("face_search_auth", "true");
                HttpContext.Session.SetString("face_search_name", "Demo User");
                return RedirectToAction("Index");
            }

            // ── Real database login ──────────────────────────────────────────
            bool found = false;
            string name = "";

            try
            {
                var connStr = GetConnectionString();
                using var conn = new MySql.Data.MySqlClient.MySqlConnection(connStr);
                conn.Open();

                // Check tbl_con_member_2026
                if (!found)
                {
                    var sql = @"SELECT fname, lname FROM tbl_con_member_2026
                        WHERE is_active=1 AND is_deleted=0
                        AND (registration_id=@rid OR its_regid=@rid)
                        AND (LOWER(email)=@em OR LOWER(alt_email)=@em) LIMIT 1";
                    using var cmd = new MySql.Data.MySqlClient.MySqlCommand(sql, conn);
                    cmd.Parameters.AddWithValue("@rid", regId);
                    cmd.Parameters.AddWithValue("@em", email);
                    using var rd = cmd.ExecuteReader();
                    if (rd.Read()) { found = true; name = $"{rd["fname"]} {rd["lname"]}".Trim(); }
                }

                // Check tbl_members
                if (!found)
                {
                    var sql = @"SELECT fname, lname FROM tbl_members
                        WHERE is_active=1
                        AND (registration_id=@rid OR its_regid=@rid)
                        AND (LOWER(email)=@em OR LOWER(alt_email)=@em) LIMIT 1";
                    using var cmd = new MySql.Data.MySqlClient.MySqlCommand(sql, conn);
                    cmd.Parameters.AddWithValue("@rid", regId);
                    cmd.Parameters.AddWithValue("@em", email);
                    using var rd = cmd.ExecuteReader();
                    if (rd.Read()) { found = true; name = $"{rd["fname"]} {rd["lname"]}".Trim(); }
                }

                // Check tbl_icmr_registration_2026
                if (!found)
                {
                    var sql = @"SELECT fname, lname FROM tbl_icmr_registration_2026
                        WHERE is_active=1 AND is_deleted=0
                        AND (icmr_reg_id=@rid OR its_regid=@rid OR icmr_id=@rid)
                        AND (LOWER(email)=@em OR LOWER(alt_email)=@em) LIMIT 1";
                    using var cmd = new MySql.Data.MySqlClient.MySqlCommand(sql, conn);
                    cmd.Parameters.AddWithValue("@rid", regId);
                    cmd.Parameters.AddWithValue("@em", email);
                    using var rd = cmd.ExecuteReader();
                    if (rd.Read()) { found = true; name = $"{rd["fname"]} {rd["lname"]}".Trim(); }
                }
            }
            catch (Exception)
            {
                // DB connection failed - only demo works
            }

            if (found)
            {
                HttpContext.Session.SetString("face_search_auth", "true");
                HttpContext.Session.SetString("face_search_name", name);
                return RedirectToAction("Index");
            }

            TempData["Error"] = "Registration ID or Email not found. Please check and try again.";
            return View("Login", model);
        }

        [HttpGet]
        public IActionResult Logout()
        {
            HttpContext.Session.Remove("face_search_auth");
            HttpContext.Session.Remove("face_search_name");
            return RedirectToAction("Login");
        }

        // ── INDEX (Search Form) ───────────────────────────────────────────────
        [HttpGet]
        public IActionResult Index()
        {
            if (!IsLoggedIn()) return RedirectToAction("Login");

            var days = DriveFolders.Keys.ToList();
            var indexed = new Dictionary<string, bool>();
            foreach (var day in days)
                indexed[day] = System.IO.File.Exists(IndexPath(day));

            ViewBag.Days = days;
            ViewBag.Indexed = indexed;
            ViewBag.UserName = HttpContext.Session.GetString("face_search_name") ?? "Attendee";
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Search(IFormFile face, string day)
        {
            if (!IsLoggedIn())
                return Json(new { error = "Unauthorized. Please login first." });

            if (face == null || face.Length == 0)
                return Json(new { error = "Please upload a face photo." });

            if (string.IsNullOrEmpty(day))
                return Json(new { error = "Please select an event day." });

            // ── Demo mode ────────────────────────────────────────────────────
            if (HttpContext.Session.GetString("face_search_name") == "Demo User")
            {
                await Task.Delay(2000); // simulate search
                var demoResults = new[]
                {
            new { url = "https://picsum.photos/seed/face1/400/300",
                  filename = "event-photo-1.jpg", confidence = 95.5m, s3_key = "demo/1" },
            new { url = "https://picsum.photos/seed/face2/400/300",
                  filename = "event-photo-2.jpg", confidence = 89.2m, s3_key = "demo/2" },
            new { url = "https://picsum.photos/seed/face3/400/300",
                  filename = "event-photo-3.jpg", confidence = 82.7m, s3_key = "demo/3" },
            new { url = "https://picsum.photos/seed/face4/400/300",
                  filename = "event-photo-4.jpg", confidence = 76.1m, s3_key = "demo/4" }
        };
                return Json(new
                {
                    success = true,
                    matched = demoResults,
                    match_count = demoResults.Length,
                    day
                });
            }

           
            if (!IsLoggedIn())
                return Json(new { error = "Unauthorized. Please login first." });

            if (face == null || face.Length == 0)
                return Json(new { error = "Please upload a face photo." });

            if (string.IsNullOrEmpty(day))
                return Json(new { error = "Please select an event day." });

            var idxPath = IndexPath(day);
            if (!System.IO.File.Exists(idxPath))
                return Json(new { error = $"Photos for '{day}' are not indexed yet." });

            var indexJson = await System.IO.File.ReadAllTextAsync(idxPath);
            var index = JsonSerializer.Deserialize<FaceIndex>(indexJson);
            if (index?.CollectionId == null)
                return Json(new { error = "Index corrupted. Please re-index." });

            try
            {
                using var ms = new MemoryStream();
                await face.CopyToAsync(ms);
                var imageBytes = ResizeImage(ms.ToArray(), 1000);

                using var rek = GetRekognition();
                var result = await rek.SearchFacesByImageAsync(new SearchFacesByImageRequest
                {
                    CollectionId = index.CollectionId,
                    Image = new Amazon.Rekognition.Model.Image
                    {
                        Bytes = new MemoryStream(imageBytes)
                    },
                    MaxFaces = 500,
                    FaceMatchThreshold = THRESHOLD
                });

                var matched = new List<object>();
                var seen = new HashSet<string>();

                foreach (var match in result.FaceMatches)
                {
                    var faceId = match.Face.FaceId;
                    var confidence = Math.Round((decimal)match.Similarity, 1);

                    if (!index.Faces.TryGetValue(faceId, out var s3Key)) continue;
                    if (!seen.Add(s3Key)) continue;

                    matched.Add(new
                    {
                        url = GetS3Url(s3Key),
                        filename = Path.GetFileName(s3Key),
                        confidence,
                        s3_key = s3Key
                    });
                }

                matched = matched.OrderByDescending(x => ((dynamic)x).confidence).ToList();

                return Json(new
                {
                    success = true,
                    matched,
                    match_count = matched.Count,
                    day
                });
            }
            catch (AmazonRekognitionException ex) when (
                            ex.Message.Contains("no face") ||
                            ex.Message.Contains("Invalid image") ||
                            ex.Message.Contains("face"))
            {
                return Json(new { error = "No face detected. Please upload a clear front-facing photo." });
            }
            catch (Exception ex)
            {
                return Json(new { error = $"Search failed: {ex.Message}" });
            }
        }

        // ── ADMIN: Copy Drive → S3 ────────────────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> CopyDriveToS3(string day, int chunk_size = 5)
        {
            if (!DriveFolders.TryGetValue(day, out var folderId))
                return Json(new { error = $"Unknown day '{day}'." });

            var bucket = _config["AWS:Bucket"] ?? "our-photos26";
            var dir = Path.Combine(Directory.GetCurrentDirectory(), "App_Data", "facesets");
            Directory.CreateDirectory(dir);
            var copyPath = Path.Combine(dir, $"{day}-copy.json");

            CopyProgress progress;
            if (System.IO.File.Exists(copyPath))
                progress = JsonSerializer.Deserialize<CopyProgress>(await System.IO.File.ReadAllTextAsync(copyPath))!;
            else
                progress = new CopyProgress { Day = day };

            var allFiles = await DriveListImages(folderId);
            if (allFiles == null) return Json(new { error = "Could not list Drive folder." });

            var total = allFiles.Count;
            var remaining = allFiles.Where(f => !progress.CopiedFiles.Contains(f.Id)).ToList();

            if (!remaining.Any())
                return Json(new { done = true, day, totalPhotos = total, copiedSoFar = progress.CopiedFiles.Count, remaining = 0 });

            using var s3 = GetS3();
            var chunk = remaining.Take(chunk_size).ToList();
            int done = 0;
            var errors = new List<string>();

            foreach (var file in chunk)
            {
                var s3Key = $"{day}/{file.Name}";
                try
                {
                    await s3.GetObjectMetadataAsync(bucket, s3Key);
                    progress.CopiedFiles.Add(file.Id);
                    await System.IO.File.WriteAllTextAsync(copyPath, JsonSerializer.Serialize(progress));
                    done++;
                    continue;
                }
                catch { }

                var imageData = await DriveDownloadFile(file.Id);
                if (imageData == null)
                {
                    errors.Add($"{file.Name}: could not download");
                    progress.CopiedFiles.Add(file.Id);
                    await System.IO.File.WriteAllTextAsync(copyPath, JsonSerializer.Serialize(progress));
                    continue;
                }

                try
                {
                    await s3.PutObjectAsync(new PutObjectRequest
                    {
                        BucketName = bucket,
                        Key = s3Key,
                        InputStream = new MemoryStream(imageData),
                        ContentType = "image/jpeg"
                    });
                    progress.CopiedFiles.Add(file.Id);
                    done++;
                }
                catch (Exception ex) { errors.Add($"{file.Name}: {ex.Message}"); }

                await System.IO.File.WriteAllTextAsync(copyPath, JsonSerializer.Serialize(progress));
                await Task.Delay(100);
            }

            var nowCopied = progress.CopiedFiles.Count;
            var stillLeft = total - nowCopied;

            return Json(new { done = stillLeft <= 0, day, totalPhotos = total, copiedSoFar = nowCopied, remaining = Math.Max(0, stillLeft), newlyCopied = done, errors });
        }

        // ── ADMIN: Index Day ──────────────────────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> IndexDay(string day, int chunk_size = 8)
        {
            if (!DriveFolders.ContainsKey(day))
                return Json(new { error = $"Unknown day '{day}'." });

            var bucket = _config["AWS:Bucket"] ?? "our-photos26";
            var collectionId = $"ifnrcon-{day}";
            var dir = Path.Combine(Directory.GetCurrentDirectory(), "App_Data", "facesets");
            Directory.CreateDirectory(dir);
            var idxPath = IndexPath(day);

            FaceIndex index;
            if (System.IO.File.Exists(idxPath))
                index = JsonSerializer.Deserialize<FaceIndex>(await System.IO.File.ReadAllTextAsync(idxPath))!;
            else
                index = new FaceIndex { CollectionId = collectionId, Day = day };

            using var rek = GetRekognition();
            using var s3 = GetS3();

            try { await rek.CreateCollectionAsync(new CreateCollectionRequest { CollectionId = collectionId }); }
            catch (ResourceAlreadyExistsException) { }

            var s3Objects = new List<string>();
            string? contToken = null;
            do
            {
                var resp = await s3.ListObjectsV2Async(new ListObjectsV2Request
                {
                    BucketName = bucket,
                    Prefix = $"{day}/",
                    ContinuationToken = contToken
                });
                s3Objects.AddRange(resp.S3Objects
                    .Where(o => o.Key.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                             || o.Key.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
                             || o.Key.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                    .Select(o => o.Key));
                contToken = resp.IsTruncated == true ? resp.NextContinuationToken : null;
            } while (contToken != null);

            var total = s3Objects.Count;
            var remaining = s3Objects.Where(k => !index.IndexedKeys.Contains(k)).ToList();

            if (!remaining.Any())
                return Json(new { done = true, day, totalPhotos = total, faceTokens = index.Faces.Count, newlyIndexed = 0, remaining = 0, errors = new List<string>() });

            var chunk = remaining.Take(chunk_size).ToList();
            int done = 0;
            var errors = new List<string>();

            foreach (var s3Key in chunk)
            {
                try
                {
                    var result = await rek.IndexFacesAsync(new IndexFacesRequest
                    {
                        CollectionId = collectionId,
                        Image = new Amazon.Rekognition.Model.Image
                        {
                            S3Object = new Amazon.Rekognition.Model.S3Object
                            {
                                Bucket = bucket,
                                Name = s3Key
                            }
                        },
                        DetectionAttributes = new List<string>(),
                        MaxFaces = 10,
                        QualityFilter = QualityFilter.AUTO
                    });

                    foreach (var record in result.FaceRecords)
                        index.Faces[record.Face.FaceId] = s3Key;

                    done++;
                }
                catch (Exception ex) { errors.Add($"{Path.GetFileName(s3Key)}: {ex.Message}"); }

                index.IndexedKeys.Add(s3Key);
                await System.IO.File.WriteAllTextAsync(idxPath, JsonSerializer.Serialize(index));
                await Task.Delay(100);
            }

            var nowIndexed = index.IndexedKeys.Count;
            var stillLeft = total - nowIndexed;

            return Json(new { done = stillLeft <= 0, day, totalPhotos = total, indexedSoFar = nowIndexed, remaining = Math.Max(0, stillLeft), newlyIndexed = done, faceTokens = index.Faces.Count, errors });
        }

        // ── Image resize (no System.Drawing needed for basic resize) ──────────
        private static byte[] ResizeImage(byte[] imageData, int maxWidth)
        {
            using var ms = new MemoryStream(imageData);
            using var original = System.Drawing.Image.FromStream(ms);

            if (original.Width <= maxWidth)
            {
                using var outMs = new MemoryStream();
                original.Save(outMs, ImageFormat.Jpeg);
                return outMs.ToArray();
            }

            int newHeight = (int)(original.Height * (double)maxWidth / original.Width);
            using var resized = new Bitmap(maxWidth, newHeight);
            using var g = Graphics.FromImage(resized);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(original, 0, 0, maxWidth, newHeight);

            using var resultMs = new MemoryStream();
            resized.Save(resultMs, ImageFormat.Jpeg);
            return resultMs.ToArray();
        }

        // ── Google Drive helpers ──────────────────────────────────────────────
        private async Task<List<DriveFile>?> DriveListImages(string folderId)
        {
            var token = await GetDriveToken();
            if (token == null) return null;

            var files = new List<DriveFile>();
            string? pageToken = null;

            using var http = new HttpClient();
            do
            {
                var url = $"https://www.googleapis.com/drive/v3/files?q='{folderId}'+in+parents+and+mimeType+contains+'image/'+and+trashed=false&fields=nextPageToken,files(id,name)&pageSize=1000"
                          + (pageToken != null ? $"&pageToken={pageToken}" : "");
                http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                var resp = await http.GetAsync(url);
                var json = await resp.Content.ReadAsStringAsync();
                var data = JsonSerializer.Deserialize<DriveListResponse>(json);
                if (data?.Files != null) files.AddRange(data.Files);
                pageToken = data?.NextPageToken;
            } while (pageToken != null);

            return files;
        }

        private async Task<byte[]?> DriveDownloadFile(string fileId)
        {
            var token = await GetDriveToken();
            if (token == null) return null;

            using var http = new HttpClient();
            http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            var resp = await http.GetAsync($"https://www.googleapis.com/drive/v3/files/{fileId}?alt=media");
            if (!resp.IsSuccessStatusCode) return null;
            return await resp.Content.ReadAsByteArrayAsync();
        }

        private async Task<string?> GetDriveToken()
        {
            var keyPath = Path.Combine(Directory.GetCurrentDirectory(), "App_Data", "gdrive-key.json");
            if (!System.IO.File.Exists(keyPath)) return null;

            var keyJson = await System.IO.File.ReadAllTextAsync(keyPath);
            var key = JsonSerializer.Deserialize<ServiceAccountKey>(keyJson);
            if (key == null) return null;

            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var expiry = now + 3600;

            var header = Base64UrlEncode(JsonSerializer.Serialize(new { alg = "RS256", typ = "JWT" }));
            var payload = Base64UrlEncode(JsonSerializer.Serialize(new
            {
                iss = key.ClientEmail,
                scope = "https://www.googleapis.com/auth/drive.readonly",
                aud = "https://oauth2.googleapis.com/token",
                exp = expiry,
                iat = now
            }));

            var signingInput = $"{header}.{payload}";
            var rsa = System.Security.Cryptography.RSA.Create();
            rsa.ImportFromPem(key.PrivateKey.ToCharArray());
            var sig = rsa.SignData(System.Text.Encoding.UTF8.GetBytes(signingInput),
                System.Security.Cryptography.HashAlgorithmName.SHA256,
                System.Security.Cryptography.RSASignaturePadding.Pkcs1);
            var jwt = $"{signingInput}.{Base64UrlEncode(sig)}";

            using var http = new HttpClient();
            var resp = await http.PostAsync("https://oauth2.googleapis.com/token",
                new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string,string>("grant_type","urn:ietf:params:oauth:grant-type:jwt-bearer"),
                    new KeyValuePair<string,string>("assertion", jwt)
                }));

            var json = await resp.Content.ReadAsStringAsync();
            var data = JsonSerializer.Deserialize<TokenResponse>(json);
            return data?.AccessToken;
        }

        private static string Base64UrlEncode(string input) =>
            Base64UrlEncode(System.Text.Encoding.UTF8.GetBytes(input));

        private static string Base64UrlEncode(byte[] input) =>
            Convert.ToBase64String(input).Replace("+", "-").Replace("/", "_").TrimEnd('=');
    }

    // ── Data models ───────────────────────────────────────────────────────────
    public class FaceIndex
    {
        [JsonPropertyName("collection_id")] public string CollectionId { get; set; } = "";
        [JsonPropertyName("day")] public string Day { get; set; } = "";
        [JsonPropertyName("faces")] public Dictionary<string, string> Faces { get; set; } = new();
        [JsonPropertyName("indexed_keys")] public HashSet<string> IndexedKeys { get; set; } = new();
    }

    public class CopyProgress
    {
        [JsonPropertyName("day")] public string Day { get; set; } = "";
        [JsonPropertyName("copied_files")] public HashSet<string> CopiedFiles { get; set; } = new();
    }

    public class DriveFile
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("name")] public string Name { get; set; } = "";
    }

    public class DriveListResponse
    {
        [JsonPropertyName("files")] public List<DriveFile>? Files { get; set; }
        [JsonPropertyName("nextPageToken")] public string? NextPageToken { get; set; }
    }

    public class ServiceAccountKey
    {
        [JsonPropertyName("client_email")] public string ClientEmail { get; set; } = "";
        [JsonPropertyName("private_key")] public string PrivateKey { get; set; } = "";
    }

    public class TokenResponse
    {
        [JsonPropertyName("access_token")] public string? AccessToken { get; set; }
    }
}