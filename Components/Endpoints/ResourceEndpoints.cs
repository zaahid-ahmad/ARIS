using ARIS1.Data;
using ARIS1.Models;
using ARIS1.Services.Resources;
using Microsoft.EntityFrameworkCore;

namespace Microsoft.AspNetCore.Routing
{
    public static class ResourceEndpoints
    {
        // GET /resources/{id}/file — the only way to download an uploaded learning resource. Returns 404 (never 403)
        // for anything the caller may not view, so resource ids can't be probed across schools or subjects.
        public static IEndpointConventionBuilder MapResourceEndpoints(this IEndpointRouteBuilder endpoints)
        {
            return endpoints.MapGet("/resources/{id:int}/file", async (
                    int id, HttpContext context, AppDbContext db, ResourceAccessService access, ResourceFileStore store) =>
                {
                    var resource = await db.LearningResources.AsNoTracking().FirstOrDefaultAsync(r => r.LearningResourceId == id);
                    if (resource == null || resource.Type != LearningResourceTypes.Document) return Results.NotFound();
                    if (!await access.CanViewAsync(context.User, resource)) return Results.NotFound();

                    var stream = store.OpenRead(resource.SchoolId, resource.StoredFileName);
                    if (stream == null) return Results.NotFound();

                    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
                    var contentType = resource.ContentType ?? "application/octet-stream";
                    var fileName = resource.OriginalFileName ?? "resource";

                    // PDFs open in the browser; everything else downloads.
                    return contentType == "application/pdf"
                        ? Results.File(stream, contentType, enableRangeProcessing: true)
                        : Results.File(stream, contentType, fileName);
                })
                .RequireAuthorization();
        }
    }
}
