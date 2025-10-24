global using Dyndata;
global using static Dyndata.Factory;

using OrchardCore.ContentManagement;
using OrchardCore.ContentManagement.Records;
using Microsoft.AspNetCore.Mvc;
using YesSql.Services;
using System.Text.Json;
using System.Text.RegularExpressions;

public static partial class GetRoutes
{
    public static void MapGetRoutes(this WebApplication app)
    {
        // Get single item by ID
        app.MapGet("api/content/{contentType}/{id}", async (
            string contentType,
            string id,
            [FromServices] YesSql.ISession session,
            HttpContext context) =>
        {
            // Get clean populated data
            var cleanObjects = await FetchCleanContent(contentType, session);

            // Find the item with matching id
            var item = cleanObjects.FirstOrDefault(obj => obj.ContainsKey("id") && obj["id"]?.ToString() == id);

            if (item == null)
            {
                context.Response.StatusCode = 404;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync("null");
                return Results.Empty;
            }

            return Results.Json(item);
        });

        // Get all items (with optional filters)
        app.MapGet("api/content/{contentType}", async (
            string contentType,
            [FromServices] YesSql.ISession session,
            HttpContext context) =>
        {
            // Get clean populated data
            var cleanObjects = await FetchCleanContent(contentType, session);

            // Apply query filters
            var filteredData = ApplyQueryFilters(context.Request.Query, cleanObjects);

            return Results.Json(filteredData);
        });
    }
}
