# Bugfixes

## Bugfix: Recursive Population for Nested ContentPicker References, 2025-10-29 15:45

**Issue:** The `/api/expand/{contentType}` endpoint was not recursively populating nested ContentPicker fields. For example, Recipe → ContentPicker(IngredientAmount) → ContentPicker(Ingredient) - the `ingredientId` field in IngredientAmount remained as an ID string instead of being populated with the full Ingredient object.

**Root Cause:** The `CollectContentItemIds` function was collecting IDs from ALL fields ending in "Id", including `ContentItemId` (the object's own ID). This caused objects to fetch themselves as references, breaking the population logic.

**Solution:**
1. Modified `CollectContentItemIds` to skip both `"id"` and `"ContentItemId"` when collecting reference IDs
2. Modified `PopulateContentItemIds` to skip both `"id"` and `"ContentItemId"` when replacing ID fields with populated objects
3. Added a second population pass in `FetchCleanContent` to handle IDs that appear after cleanup (needed for array-type ContentPicker fields)

**Affected Files:**
- `backend/RestRoutes/GetRoutes.Population.cs`: Added checks to skip `"ContentItemId"` in collection and population logic
- `backend/RestRoutes/GetRoutes.Request.cs`: Added second population pass after cleanup to handle newly exposed reference fields from arrays

**Result:** ContentPicker fields now properly populate nested references recursively. Example: Recipe now returns full Ingredient objects within IngredientAmount items instead of just IDs.

**Testing:** Successfully tested creating content via POST with two levels of nested ContentPicker fields (e.g., Recipe → IngredientAmount → Ingredient) and verified recursive population works correctly through `/api/expand/{contentType}` endpoints.

## Bugfix: MediaField POST Support with JSON Type Compatibility, 2025-10-29 16:45

**Issue:** POST requests with MediaField data (e.g., Pet with image field) were succeeding but the image field values were being saved as empty nested arrays `[[]]` instead of the actual paths provided in the request.

**Root Cause:** Type mismatch between Newtonsoft.Json and System.Text.Json. The initial implementation used Newtonsoft.Json's `JArray` type when constructing MediaField data, but Orchard Core's `ContentItem.Content` dynamic property uses System.Text.Json internally. When assigning a `JArray` to the dynamic Content property, the serialization was failing silently, resulting in empty arrays.

**Solution:**
1. Changed MediaField handling to use `List<string>` instead of `JArray` for compatibility with System.Text.Json
2. Added specific detection for MediaField structure (objects with "paths" and "mediaTexts" properties)
3. Extract paths and mediaTexts arrays into `List<string>` collections
4. Assign directly to Content properties using System.Text.Json-compatible types

**Key Code Change:**
```csharp
// Before (didn't work - type mismatch):
var paths = new JArray();  // Newtonsoft.Json type
contentItem.Content[contentType][pascalKey] = new JObject { ["Paths"] = paths };

// After (works - compatible type):
var paths = new List<string>();  // System.Collections.Generic type
contentItem.Content[contentType][pascalKey]["Paths"] = paths;
```

**Affected Files:**
- `backend/RestRoutes/PostRoutes.cs:148-176`: Added MediaField detection and handling with System.Text.Json-compatible types

**Result:** MediaField data now properly saves when creating content via POST. Image paths and media texts are correctly stored and retrieved.

**Testing:** Successfully tested creating a Pet with an image field containing a media path. The path is correctly stored in the database and retrieved via both `/api/{contentType}` and `/api/expand/{contentType}` endpoints.

**Example POST body that works:**
```json
{
  "title": "Odie",
  "species": "dog",
  "image": {
    "paths": ["_Users/4qn6twzb1y5zd7f8004294agc8/f23749d1-87b1-4807-9cb3-c407c5ea704e.jpg"],
    "mediaTexts": [""]
  }
}
```

**Successful response from GET `/api/expand/Pet/{id}`:**
```json
{
  "id": "45zh4a6sa6w131bqghcm2rqb47",
  "title": "Odie",
  "species": "dog",
  "image": {
    "paths": ["_Users/4qn6twzb1y5zd7f8004294agc8/f23749d1-87b1-4807-9cb3-c407c5ea704e.jpg"],
    "mediaTexts": [""]
  }
}
```

**Technical Note:** This fix specifically addresses MediaField. Other complex field types like BagPart that require custom driver processing may need a different approach using `IContentItemDisplayManager.UpdateEditorAsync()` to invoke Orchard's field driver pipeline.

## Feature: BagPart POST/PUT Support with Newtonsoft.Json Removal, 2025-10-29 18:30

**Issue:** BagPart content items (nested/contained items like Recipe → IngredientAmount) could not be created or updated via POST/PUT endpoints. Additionally, PostRoutes.cs and PutRoutes.cs had Newtonsoft.Json dependencies that could be removed in favor of System.Text.Json.

**Root Cause:**
1. No handling for the `items` field to create/update BagPart ContentItems
2. Inconsistent field naming for ContentPicker arrays (input used `ownerId` but output used `owner`)
3. "owner" and "author" in RESERVED_FIELDS prevented using them as ContentPicker field names
4. Newtonsoft.Json dependencies (JObject, JArray) in POST/PUT routes

**Solution:**
1. Added BagPart support: `items` field creates/updates BagPart ContentItems with proper structure
2. Fixed GetRoutes.Cleanup.cs:132 to return `isIdReference=true` for multiple ContentItemIds, ensuring consistent "Id" suffix
3. Removed "owner" and "author" from RESERVED_FIELDS in PostRoutes.cs and PutRoutes.cs
4. Replaced Newtonsoft.Json types with standard C# collections (Dictionary<string, object>, List<object>)

**Affected Files:**
- `backend/RestRoutes/PostRoutes.cs`: Added BagPart handling, removed Newtonsoft.Json, removed "owner"/"author" from RESERVED_FIELDS
- `backend/RestRoutes/PutRoutes.cs`: Added BagPart handling, removed Newtonsoft.Json, removed "owner"/"author" from RESERVED_FIELDS
- `backend/RestRoutes/GetRoutes.Cleanup.cs`: Fixed line 132 for consistent ContentItemIds naming

**Result:**
- BagPart items can now be created and updated via POST/PUT
- Input/output field names are consistent (`ownerId` for both)
- No external Newtonsoft.Json dependency
- ContentPicker fields can use "owner"/"author" names without conflicts

**Testing:** Successfully tested POST creating Recipe with 2 IngredientAmount items, PUT updating to 1 item, and GET/expand showing correctly populated nested references.

**Example POST with BagPart:**
```json
POST /api/Recipe
{
  "title": "Test Recipe",
  "description": "A test recipe",
  "items": [
    {
      "contentType": "IngredientAmount",
      "amount": 100,
      "unit": ["grams"],
      "ingredientId": "41b4gw9e2zsptwc7fpb9570b5s"
    }
  ]
}
```

**Example PUT with BagPart:**
```json
PUT /api/Recipe/{id}
{
  "title": "Updated Recipe",
  "items": [
    {
      "contentType": "IngredientAmount",
      "amount": 200,
      "unit": ["grams"],
      "ingredientId": "41b4gw9e2zsptwc7fpb9570b5s"
    }
  ]
}
```
