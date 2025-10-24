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
