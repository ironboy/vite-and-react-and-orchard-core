using OrchardCore.ContentManagement;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using Newtonsoft.Json.Linq;

public static class PostRoutes
{
    private static readonly HashSet<string> RESERVED_FIELDS = new(StringComparer.OrdinalIgnoreCase)
    {
        "id",
        "contentItemId",
        "title",
        "displayText",
        "owner",
        "author",
        "createdUtc",
        "modifiedUtc",
        "publishedUtc",
        "contentType",
        "published",
        "latest"
    };

    public static void MapPostRoutes(this WebApplication app)
    {
        app.MapPost("api/{contentType}", async (
            string contentType,
            [FromBody] Dictionary<string, object> body,
            [FromServices] IContentManager contentManager,
            [FromServices] YesSql.ISession session,
            HttpContext context) =>
        {
            try
            {
                // Validate fields
                var validFields = await FieldValidator.GetValidFieldsAsync(contentType, contentManager, session);
                var (isValid, invalidFields) = FieldValidator.ValidateFields(body, validFields, RESERVED_FIELDS);

                if (!isValid)
                {
                    return Results.Json(new {
                        error = "Invalid fields provided",
                        invalidFields = invalidFields,
                        validFields = validFields.OrderBy(f => f).ToList()
                    }, statusCode: 400);
                }

                var contentItem = await contentManager.NewAsync(contentType);

                // Extract and handle special fields explicitly
                contentItem.DisplayText = body.ContainsKey("title")
                    ? body["title"].ToString()
                    : "Untitled";

                contentItem.Owner = context.User?.Identity?.Name ?? "anonymous";
                contentItem.Author = contentItem.Owner;

                // Build content directly into the content item
                foreach (var kvp in body)
                {
                    // Skip all reserved fields
                    if (RESERVED_FIELDS.Contains(kvp.Key))
                        continue;

                    var pascalKey = ToPascalCase(kvp.Key);
                    var value = kvp.Value;

                    // Handle fields ending with "Id" - these are content item references
                    if (kvp.Key.EndsWith("Id", StringComparison.OrdinalIgnoreCase) &&
                        kvp.Key.Length > 2)
                    {
                        // Transform "ownerId" → "Owner" with ContentItemIds
                        var fieldName = pascalKey.Substring(0, pascalKey.Length - 2); // Remove "Id"
                        var idValue = value is JsonElement jsonEl && jsonEl.ValueKind == JsonValueKind.String
                            ? jsonEl.GetString()
                            : value.ToString();

                        // Assign as a List<string> to avoid wrapping
                        if (idValue != null)
                        {
                            contentItem.Content[contentType][fieldName]["ContentItemIds"] = new List<string> { idValue };
                        }
                    }
                    else if (value is JsonElement jsonElement)
                    {
                        // Extract the actual string value, not a wrapped JObject
                        if (jsonElement.ValueKind == JsonValueKind.String)
                        {
                            contentItem.Content[contentType][pascalKey]["Text"] = jsonElement.GetString();
                        }
                        else
                        {
                            contentItem.Content[contentType][pascalKey] = ConvertJsonElement(jsonElement);
                        }
                    }
                    else if (value is string strValue)
                    {
                        contentItem.Content[contentType][pascalKey]["Text"] = strValue;
                    }
                    else if (value is int or long or double or float or decimal)
                    {
                        contentItem.Content[contentType][pascalKey] = new JObject {
                            ["Value"] = JToken.FromObject(value)
                        };
                    }
                }

                await contentManager.CreateAsync(contentItem, VersionOptions.Published);
                await session.SaveChangesAsync();

                return Results.Json(new {
                    id = contentItem.ContentItemId,
                    title = contentItem.DisplayText
                }, statusCode: 201);
            }
            catch (Exception ex)
            {
                return Results.Json(new {
                    error = ex.Message
                }, statusCode: 500);
            }
        });
    }

    private static string ToPascalCase(string str)
    {
        if (string.IsNullOrEmpty(str) || char.IsUpper(str[0]))
            return str;
        return char.ToUpper(str[0]) + str.Substring(1);
    }

    private static JToken ConvertJsonElement(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            return new JObject { ["Text"] = element.GetString() };
        }
        else if (element.ValueKind == JsonValueKind.Number)
        {
            return new JObject { ["Value"] = element.GetDouble() };
        }
        else if (element.ValueKind == JsonValueKind.True || element.ValueKind == JsonValueKind.False)
        {
            return new JObject { ["Value"] = element.GetBoolean() };
        }

        // For complex types, just wrap as-is
        return new JObject { ["Text"] = element.ToString() };
    }
}
