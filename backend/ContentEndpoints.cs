global using Dyndata;
global using static Dyndata.Factory;

using OrchardCore.ContentManagement;
using OrchardCore.ContentManagement.Records;
using Microsoft.AspNetCore.Mvc;
using YesSql.Services;
using System.Text.Json;
using System.Text.RegularExpressions;

public static class ContentEndpoints
{
    public static void MapContentEndpoints(this WebApplication app)
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

    // Extract existing logic into reusable method
    private static async Task<List<Dictionary<string, object>>> FetchCleanContent(
        string contentType,
        YesSql.ISession session)
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
        if (plainObjects == null) return new List<Dictionary<string, object>>();

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

        // Clean up the bullshit
        var cleanObjects = plainObjects.Select(obj => CleanObject(obj, contentType)).ToList();
        return cleanObjects;
    }

    private static List<Dictionary<string, object>> ApplyQueryFilters(
        IQueryCollection query,
        List<Dictionary<string, object>> data)
    {
        string? where = query["where"];
        string? orderby = query["orderby"];
        string? limit = query["limit"];
        string? offset = query["offset"];

        // If no query params, return data as-is
        if (string.IsNullOrEmpty(where) &&
            string.IsNullOrEmpty(orderby) &&
            string.IsNullOrEmpty(limit) &&
            string.IsNullOrEmpty(offset))
        {
            return data;
        }

        // Convert to Dyndata Arr
        var arrItems = new List<Obj>();
        foreach (var d in data)
        {
            arrItems.Add(ConvertToObj(d));
        }
        var arr = Arr(arrItems.ToArray());

        // Apply WHERE filters
        if (!string.IsNullOrEmpty(where))
        {
            arr = ApplyWhereFilters(arr, where);
        }

        // Apply ORDER BY
        if (!string.IsNullOrEmpty(orderby))
        {
            arr = ApplyOrderBy(arr, orderby);
        }

        // Apply LIMIT and OFFSET
        int? limitInt = !string.IsNullOrEmpty(limit) && Regex.IsMatch(limit, @"^\d{1,}$")
            ? int.Parse(limit) : null;
        int? offsetInt = !string.IsNullOrEmpty(offset) && Regex.IsMatch(offset, @"^\d{1,}$")
            ? int.Parse(offset) : null;

        if (offsetInt.HasValue && limitInt.HasValue)
        {
            arr = Arr(arr.Slice(offsetInt.Value, offsetInt.Value + limitInt.Value));
        }
        else if (limitInt.HasValue)
        {
            arr = Arr(arr.Slice(0, limitInt.Value));
        }

        // Convert back to List<Dictionary>
        return ConvertFromArr(arr);
    }

    private static Obj ConvertToObj(Dictionary<string, object> dict)
    {
        var obj = Obj();
        foreach (var kvp in dict)
        {
            if (kvp.Value is Dictionary<string, object> nestedDict)
            {
                obj[kvp.Key] = ConvertToObj(nestedDict);  // Recursive for nested objects
            }
            else if (kvp.Value is List<object> list)
            {
                // Handle arrays (if any)
                var arr = Arr();
                foreach (var item in list)
                {
                    if (item is Dictionary<string, object> itemDict)
                    {
                        arr.Push(ConvertToObj(itemDict));
                    }
                    else
                    {
                        arr.Push(item);
                    }
                }
                obj[kvp.Key] = arr;
            }
            else
            {
                obj[kvp.Key] = kvp.Value;
            }
        }
        return obj;
    }

    private static List<Dictionary<string, object>> ConvertFromArr(Arr arr)
    {
        var result = new List<Dictionary<string, object>>();
        foreach (Obj item in arr)
        {
            result.Add(ConvertFromObj(item));
        }
        return result;
    }

    private static Dictionary<string, object> ConvertFromObj(Obj obj)
    {
        var dict = new Dictionary<string, object>();
        foreach (var key in obj.GetKeys())
        {
            var value = obj[key];
            if (value is Obj nestedObj)
            {
                dict[key] = ConvertFromObj(nestedObj);  // Recursive!
            }
            else if (value is Arr nestedArr)
            {
                var list = new List<object>();
                foreach (var item in nestedArr)
                {
                    if (item is Obj itemObj)
                    {
                        list.Add(ConvertFromObj(itemObj));
                    }
                    else
                    {
                        list.Add(item);
                    }
                }
                dict[key] = list;
            }
            else
            {
                dict[key] = value;
            }
        }
        return dict;
    }

    private static Arr ApplyWhereFilters(Arr data, string where)
    {
        // Split by operators (but keep them)
        var ops1 = Arr("!=", ">=", "<=", "=", ">", "<", "_LIKE_", "_AND_", "LIKE", "AND");
        var ops2 = Arr("!=", ">=", "<=", "=", ">", "<", "LIKE", "AND", "LIKE", "AND");

        foreach (var op in ops1)
        {
            var splitParts = where.Split((string)op);
            where = string.Join($"_-_{ops1.IndexOf(op)}_-_", splitParts);
        }

        var parts = Arr(where.Split("_-_"));
        var mappedParts = Arr();
        var idx = 0;
        foreach (var x in parts)
        {
            if (idx % 2 == 0)
            {
                mappedParts.Push(x);
            }
            else
            {
                mappedParts.Push(ops2[int.Parse((string)x)]);
            }
            idx++;
        }

        // Validate structure (AND every 4th position)
        var i = 0;
        var faulty = false;
        foreach (var x in mappedParts)
        {
            if (i++ % 4 == 3 && (string)x != "AND")
            {
                faulty = true;
                break;
            }
        }

        if (faulty)
        {
            return data; // Invalid syntax, return unfiltered
        }

        // Extract keys, values, operators
        var keys = Arr();
        var values = Arr();
        var operators = Arr();

        for (var j = 0; j < mappedParts.Length; j++)
        {
            if (j % 4 == 0)
            {
                keys.Push(Regex.Replace((string)mappedParts[j], @"[^A-Za-z0-9_\-,\.]", ""));
            }
            else if (j % 4 == 2)
            {
                values.Push(mappedParts[j]);
            }
            else if (j % 2 == 1 && j % 4 != 3)
            {
                operators.Push(mappedParts[j]);
            }
        }

        // Apply each filter sequentially
        var result = data;
        for (var idx2 = 0; idx2 < keys.Length; idx2++)
        {
            var key = (string)keys[idx2];
            var op = (string)operators[idx2];
            var value = values[idx2];

            result = ApplySingleFilter(result, key, op, value);
        }

        return result;
    }

    private static Arr ApplySingleFilter(Arr data, string key, string op, dynamic value)
    {
        var keyParts = key.Split('.');

        return data.Filter(item =>
        {
            var itemValue = GetNestedValue(item, keyParts);

            if (itemValue == null) return false;

            var itemStr = itemValue.ToString();
            var valueStr = value?.ToString() ?? "";

            return op switch
            {
                "=" => itemStr == valueStr,
                "!=" => itemStr != valueStr,
                ">" => CompareNumeric(itemStr, valueStr) > 0,
                "<" => CompareNumeric(itemStr, valueStr) < 0,
                ">=" => CompareNumeric(itemStr, valueStr) >= 0,
                "<=" => CompareNumeric(itemStr, valueStr) <= 0,
                "LIKE" => itemStr.ToLower().Contains(valueStr.ToLower()),
                _ => false
            };
        });
    }

    private static dynamic? GetNestedValue(dynamic obj, string[] keyParts)
    {
        dynamic current = obj;

        foreach (var part in keyParts)
        {
            if (current is Obj dynObj && dynObj.HasKey(part))
            {
                current = dynObj[part];
            }
            else
            {
                return null;
            }
        }

        return current;
    }

    private static int CompareNumeric(string a, string b)
    {
        if (double.TryParse(a, out var aNum) && double.TryParse(b, out var bNum))
        {
            return aNum.CompareTo(bNum);
        }
        return string.Compare(a, b, StringComparison.Ordinal);
    }

    private static Arr ApplyOrderBy(Arr data, string orderby)
    {
        // Clean orderby string
        orderby = Regex.Replace(orderby, @"[^A-Za-z0-9_\-,\.]", "");
        var orderFields = orderby.Split(",");

        // Convert to list for LINQ sorting
        var list = new List<Obj>();
        foreach (Obj item in data)
        {
            list.Add(item);
        }

        IOrderedEnumerable<Obj>? ordered = null;

        foreach (var field in orderFields)
        {
            var trimmed = field.Trim();
            var cleaned = Regex.Replace(trimmed, @"\+", "");
            var isDesc = cleaned.StartsWith("-");
            var fieldName = isDesc ? cleaned.Substring(1) : cleaned;
            var keyParts = fieldName.Split('.');

            if (ordered == null)
            {
                // First sort
                ordered = isDesc
                    ? list.OrderByDescending(item => GetNestedValue(item, keyParts)?.ToString() ?? "")
                    : list.OrderBy(item => GetNestedValue(item, keyParts)?.ToString() ?? "");
            }
            else
            {
                // ThenBy for multiple sorts
                ordered = isDesc
                    ? ordered.ThenByDescending(item => GetNestedValue(item, keyParts)?.ToString() ?? "")
                    : ordered.ThenBy(item => GetNestedValue(item, keyParts)?.ToString() ?? "");
            }
        }

        var finalList = ordered != null ? ordered.ToList() : list;
        return Arr(finalList.ToArray());
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