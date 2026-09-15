using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components.Forms;

namespace ARIS1.Services.Resources
{
    public record StoredResourceFile(string StoredFileName, string OriginalFileName, string ContentType, long SizeBytes);

    // Private on-disk storage for uploaded learning-resource documents. Files live OUTSIDE wwwroot under
    // {root}/{schoolId}/{guid}{ext}, so they can only be downloaded through the authorised
    // /resources/{id}/file endpoint. The user's file name is only kept as metadata, never used in a path.
    public class ResourceFileStore
    {
        public const long MaxFileBytes = 20 * 1024 * 1024;

        private static readonly Dictionary<string, string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            [".pdf"] = "application/pdf",
            [".doc"] = "application/msword",
            [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            [".ppt"] = "application/vnd.ms-powerpoint",
            [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            [".xls"] = "application/vnd.ms-excel",
            [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            [".txt"] = "text/plain"
        };

        public static string AcceptAttribute => string.Join(",", AllowedTypes.Keys);

        // Stored names are always a 32-hex-digit GUID plus one allowed extension.
        private static readonly Regex StoredNamePattern = new(@"^[0-9a-f]{32}\.[a-z]{3,4}$", RegexOptions.Compiled);

        private readonly string _root;

        public ResourceFileStore(IConfiguration configuration, IWebHostEnvironment environment)
        {
            var configured = configuration["Resources:StoragePath"];
            _root = string.IsNullOrWhiteSpace(configured)
                ? Path.Combine(environment.ContentRootPath, "App_Data", "resources")
                : Path.GetFullPath(configured);
        }

        // Returns null with an error message when the file isn't acceptable.
        public static string? Validate(IBrowserFile file)
        {
            var extension = Path.GetExtension(file.Name);
            if (string.IsNullOrEmpty(extension) || !AllowedTypes.ContainsKey(extension))
                return $"Only these file types can be uploaded: {string.Join(", ", AllowedTypes.Keys)}.";
            if (file.Size > MaxFileBytes)
                return $"Files can be at most {MaxFileBytes / (1024 * 1024)} MB. Add large files or videos as a link instead.";
            if (file.Size == 0)
                return "The selected file is empty.";
            return null;
        }

        public async Task<StoredResourceFile> SaveAsync(IBrowserFile file, int schoolId)
        {
            var error = Validate(file);
            if (error != null) throw new InvalidOperationException(error);

            var extension = Path.GetExtension(file.Name).ToLowerInvariant();
            var storedName = Guid.NewGuid().ToString("N") + extension;
            var directory = Path.Combine(_root, schoolId.ToString());
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, storedName);

            try
            {
                await using var source = file.OpenReadStream(MaxFileBytes);
                await using var target = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                await source.CopyToAsync(target);
            }
            catch
            {
                TryDelete(schoolId, storedName);
                throw;
            }

            var originalName = Path.GetFileName(file.Name);
            if (originalName.Length > 255) originalName = originalName[^255..];
            return new StoredResourceFile(storedName, originalName, AllowedTypes[extension], new FileInfo(path).Length);
        }

        public Stream? OpenRead(int schoolId, string? storedName)
        {
            var path = ResolvePath(schoolId, storedName);
            return path != null && File.Exists(path) ? File.OpenRead(path) : null;
        }

        // Only used to clean up a file whose database row failed to save. Normal "delete" is a soft delete that
        // keeps the file, because Year Rollover copies share the same stored file.
        public void TryDelete(int schoolId, string? storedName)
        {
            try
            {
                var path = ResolvePath(schoolId, storedName);
                if (path != null && File.Exists(path)) File.Delete(path);
            }
            catch
            {
                // Best effort only.
            }
        }

        private string? ResolvePath(int schoolId, string? storedName)
        {
            if (string.IsNullOrEmpty(storedName) || !StoredNamePattern.IsMatch(storedName)) return null;
            var path = Path.GetFullPath(Path.Combine(_root, schoolId.ToString(), storedName));
            var schoolRoot = Path.GetFullPath(Path.Combine(_root, schoolId.ToString())) + Path.DirectorySeparatorChar;
            return path.StartsWith(schoolRoot, StringComparison.OrdinalIgnoreCase) ? path : null;
        }
    }
}
