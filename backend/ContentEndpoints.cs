global using Dyndata;
global using static Dyndata.Factory;

using OrchardCore.ContentManagement;
using OrchardCore.ContentManagement.Records;
using Microsoft.AspNetCore.Mvc;
using YesSql.Services;
using System.Text.Json;

public static class ContentEndpoints
{
    public static void MapContentEndpoints(this WebApplication app)
    {
        app.MapGet("api/content/{contentType}", async (
            string contentType,
            [FromServices] YesSql.ISession session) =>
        {
            var contentItems = await session
                .Query()
                .For<ContentItem>()
                .With<ContentItemIndex>(x => x.ContentType == contentType && x.Published)
                .ListAsync();

            var jsonOptions = new JsonSerializerOptions
            {
                ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles
            };
            var jsonString = JsonSerializer.Serialize(contentItems, jsonOptions);
            var plainObjects = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(jsonString);
            if (plainObjects == null) return Results.Ok(new List<object>());

            var allReferencedIds = new HashSet<string>();
            foreach (var obj in plainObjects)
            {
                CollectContentItemIds(obj, allReferencedIds);
            }

            if (allReferencedIds.Count > 0)
            {
                var referencedItems = await session
                    .Query()
                    .For<ContentItem>()
                    .With<ContentItemIndex>(x => x.ContentItemId.IsIn(allReferencedIds))
                    .ListAsync();

                var refJsonString = JsonSerializer.Serialize(referencedItems, jsonOptions);
                var plainRefItems = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(refJsonString);
                if (plainRefItems != null)
                {
                    var itemsDictionary = new Dictionary<string, Dictionary<string, JsonElement>>();
                    foreach (var item in plainRefItems)
                    {
                        if (item.TryGetValue("ContentItemId", out var idElement))
                        {
                            var id = idElement.GetString();
                            if (id != null) itemsDictionary[id] = item;
                        }
                    }

                    foreach (var obj in plainObjects)
                    {
                        PopulateContentItemIds(obj, itemsDictionary);
                    }
                }
            }

            // Clean up the bullshit!
            var cleanObjects = plainObjects.Select(obj => CleanObject(obj, contentType)).ToList();

            return Results.Json(cleanObjects);
        });
    }

    private static Dictionary<string, object> CleanObject(Dictionary<string, JsonElement> obj, string contentType)
    {
        var clean = new Dictionary<string, object>();

        // Get basic fields
        if (obj.TryGetValue("ContentItemId", out var id))
            clean["id"] = id.GetString()!;

        if (obj.TryGetValue("DisplayText", out var title))
            clean["title"] = title.GetString()!;

        // Get the content type section (e.g., "Pet", "PetOwner")
        if (obj.TryGetValue(contentType, out var typeSection) && typeSection.ValueKind == JsonValueKind.Object)
        {
            var typeDict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(typeSection.GetRawText());
            if (typeDict != null)
            {
                foreach (var kvp in typeDict)
                {
                    var value = ExtractFieldValue(kvp.Value);
                    if (value != null)
                    {
                        clean[ToCamelCase(kvp.Key)] = value;
                    }
                }
            }
        }

        return clean;
    }

    private static object? ExtractFieldValue(JsonElement element)
    {
        // Handle Text fields: { "Text": "value" } → "value"
        if (element.ValueKind == JsonValueKind.Object)
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(element.GetRawText());
            if (dict != null)
            {
                // Check for Text field
                if (dict.ContainsKey("Text") && dict.Count == 1)
                {
                    return dict["Text"].GetString();
                }

                // Check for Items array (populated relations)
                if (dict.ContainsKey("Items"))
                {
                    var items = dict["Items"];
                    if (items.ValueKind == JsonValueKind.Array)
                    {
                        var itemsList = new List<object>();
                        foreach (var item in items.EnumerateArray())
                        {
                            if (item.ValueKind == JsonValueKind.Object)
                            {
                                var itemDict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(item.GetRawText());
                                if (itemDict != null)
                                {
                                    // Get the content type from the item
                                    string? itemType = null;
                                    if (itemDict.TryGetValue("ContentType", out var ct))
                                    {
                                        itemType = ct.GetString();
                                    }
                                    itemsList.Add(CleanObject(itemDict, itemType ?? ""));
                                }
                            }
                        }
                        // Return null (serializes to remove key) if 0 items, object if one item otherwise array
                        return itemsList.Count == 0 ? null : itemsList.Count == 1 ? itemsList[0] : itemsList;
                    }
                }

                // Otherwise return the whole object cleaned
                var cleaned = new Dictionary<string, object>();
                foreach (var kvp in dict)
                {
                    var value = ExtractFieldValue(kvp.Value);
                    if (value != null)
                    {
                        cleaned[ToCamelCase(kvp.Key)] = value;
                    }
                }
                return cleaned;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            var list = new List<object>();
            foreach (var item in element.EnumerateArray())
            {
                var value = ExtractFieldValue(item);
                if (value != null)
                {
                    list.Add(value);
                }
            }
            return list;
        }
        else if (element.ValueKind == JsonValueKind.String)
        {
            return element.GetString();
        }
        else if (element.ValueKind == JsonValueKind.Number)
        {
            return element.GetDouble();
        }
        else if (element.ValueKind == JsonValueKind.True || element.ValueKind == JsonValueKind.False)
        {
            return element.GetBoolean();
        }

        return null;
    }

    private static string ToCamelCase(string str)
    {
        if (string.IsNullOrEmpty(str) || char.IsLower(str[0]))
            return str;
        return char.ToLower(str[0]) + str.Substring(1);
    }

    private static void CollectContentItemIds(Dictionary<string, JsonElement> obj, HashSet<string> ids)
    {
        foreach (var kvp in obj)
        {
            if (kvp.Key == "ContentItemIds" && kvp.Value.ValueKind == JsonValueKind.Array)
            {
                foreach (var id in kvp.Value.EnumerateArray())
                {
                    if (id.ValueKind == JsonValueKind.String)
                    {
                        var idStr = id.GetString();
                        if (idStr != null) ids.Add(idStr);
                    }
                }
            }
            else if (kvp.Value.ValueKind == JsonValueKind.Object)
            {
                var nested = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(kvp.Value.GetRawText());
                if (nested != null) CollectContentItemIds(nested, ids);
            }
            else if (kvp.Value.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in kvp.Value.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Object)
                    {
                        var nested = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(item.GetRawText());
                        if (nested != null) CollectContentItemIds(nested, ids);
                    }
                }
            }
        }
    }

    private static void PopulateContentItemIds(
        Dictionary<string, JsonElement> obj,
        Dictionary<string, Dictionary<string, JsonElement>> itemsDictionary)
    {
        var keysToProcess = obj.Keys.ToList();

        foreach (var key in keysToProcess)
        {
            var value = obj[key];

            if (key == "ContentItemIds" && value.ValueKind == JsonValueKind.Array)
            {
                var items = new List<Dictionary<string, JsonElement>>();
                foreach (var id in value.EnumerateArray())
                {
                    if (id.ValueKind == JsonValueKind.String)
                    {
                        var idStr = id.GetString();
                        if (idStr != null && itemsDictionary.TryGetValue(idStr, out var item))
                        {
                            items.Add(item);
                        }
                    }
                }

                obj["Items"] = JsonSerializer.SerializeToElement(items);
                obj.Remove("ContentItemIds");
            }
            else if (value.ValueKind == JsonValueKind.Object)
            {
                var nested = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(value.GetRawText());
                if (nested != null)
                {
                    PopulateContentItemIds(nested, itemsDictionary);
                    obj[key] = JsonSerializer.SerializeToElement(nested);
                }
            }
            else if (value.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in value.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Object)
                    {
                        var nested = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(item.GetRawText());
                        if (nested != null) PopulateContentItemIds(nested, itemsDictionary);
                    }
                }
            }
        }
    }
}