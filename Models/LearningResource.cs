using System.ComponentModel.DataAnnotations;

namespace ARIS1.Models
{
    // A learning material attached to one subject: an uploaded document (stored privately outside wwwroot and
    // served through the authorised /resources/{id}/file endpoint) or an external link (e.g. a YouTube video).
    // Teachers manage their own current-year subjects' resources; learners, linked parents and admins view them.
    public class LearningResource
    {
        [Key]
        public int LearningResourceId { get; set; }

        public int SchoolId { get; set; }
        public School School { get; set; } = null!;

        public int SubjectId { get; set; }
        public Subject Subject { get; set; } = null!;

        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string Type { get; set; } = LearningResourceTypes.Document;
        public string? Category { get; set; }
        public int? Term { get; set; } // null = all year

        // Uploaded documents only. StoredFileName is a random GUID-based name, never the user's file name.
        public string? StoredFileName { get; set; }
        public string? OriginalFileName { get; set; }
        public string? ContentType { get; set; }
        public long? FileSizeBytes { get; set; }

        // Links only (absolute http/https).
        public string? ExternalUrl { get; set; }

        public string UploadedByUserId { get; set; } = string.Empty;
        public User UploadedBy { get; set; } = null!;

        public bool IsActive { get; set; } = true;
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    }

    public static class LearningResourceTypes
    {
        public const string Document = "Document";
        public const string VideoLink = "VideoLink";
        public const string Link = "Link";

        public static readonly string[] All = { Document, VideoLink, Link };

        public static string Label(string type) => type switch
        {
            Document => "Document",
            VideoLink => "Video link",
            Link => "Web link",
            _ => type
        };
    }
}
