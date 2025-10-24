using OrchardCore.ContentManagement;
using OrchardCore.ContentManagement.Records;
using YesSql.Services;
using System.Text.Json;

public static partial class GetRoutes
{
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
}
