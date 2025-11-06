# Bugfixes / added features

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

## Feature: UserPickerField POST/PUT/GET Support with User Enrichment, 2025-10-29 20:54

**Issue:** UserPickerField was not supported in POST/PUT operations, and GET responses only returned basic user info (id, username) without additional fields like email, phone, firstName, lastName.

**Solution:**
1. **POST/PUT**: Accept user objects with `id` and `username` properties, convert to Orchard's parallel arrays (UserIds/UserNames)
2. **GET /api/expand**: Batch query UserIndex via YesSql to enrich user data with email, phone, and spread Properties object (firstName, lastName, custom fields)
3. Transform Orchard's awkward parallel arrays into clean object format

**Affected Files:**
- `backend/RestRoutes/PostRoutes.cs`: Added UserPickerField detection and unzipping
- `backend/RestRoutes/PutRoutes.cs`: Added UserPickerField detection and unzipping
- `backend/RestRoutes/GetRoutes.Cleanup.cs`: Added user enrichment with YesSql data
- `backend/RestRoutes/GetRoutes.Population.cs`: Added CollectUserIds method
- `backend/RestRoutes/GetRoutes.Request.cs`: Added batch UserIndex query and enrichment

**Result:**
- Clean API: POST/PUT with `[{id, username}]` format
- Rich responses: GET /api/expand returns email, phone, firstName, lastName, and any custom Properties
- Efficient: Single batch query via YesSql (no N+1)
- Future-proof: Properties object is spread automatically

**Example POST/PUT with UserPickerField:**
```json
POST /api/ArtistInfo
{
  "title": "Alice's Art",
  "description": "Contemporary artist",
  "customer": [
    {
      "id": "4d199s979mvpfyqmb5jnetyqm9",
      "username": "alice"
    }
  ]
}
```

**Example GET /api/expand response (enriched):**
```json
{
  "id": "4gx56nbr93whhym9n4b578vwct",
  "title": "Alice's Art",
  "customer": [
    {
      "id": "4d199s979mvpfyqmb5jnetyqm9",
      "username": "alice",
      "email": "alice@example.com",
      "phone": "+1234567890",
      "firstName": "Alice",
      "lastName": "Anderson"
    }
  ]
}
```

**Note:** POST/PUT requires BOTH `id` and `username` for each user. GET `/api/{contentType}` returns basic format (id, username), while GET `/api/expand/{contentType}` returns enriched format with all user fields.

## Feature: Add User ID to Auth Endpoints, 2025-11-04 12:15

**Issue:** Authentication endpoints (GET/POST `/api/auth/login` and POST `/api/auth/register`) were not returning the user ID in their responses, making it difficult for clients to identify and reference the authenticated user.

**Solution:** Added `id` field containing the user's `UserId` to all three authentication endpoint responses. Since the `IUser` interface doesn't expose `UserId` directly, we cast to the concrete `User` type to access the property.

**Affected Files:**
- `backend/RestRoutes/AuthEndpoints.cs:61`: Added `id` to POST `/api/auth/register` response
- `backend/RestRoutes/AuthEndpoints.cs:114`: Added `id` to POST `/api/auth/login` response
- `backend/RestRoutes/AuthEndpoints.cs:144`: Added `id` to GET `/api/auth/login` response

**Technical Note:** The `UserId` property is only available on the concrete `User` class, not on the `IUser` interface. All three endpoints cast to `User` using `var u = user as User;` and then access `u?.UserId` to safely retrieve the ID.

**Result:** All authentication endpoints now return the user's ID along with other user information (username, email, firstName, lastName, etc.), enabling clients to easily reference and store the authenticated user's identifier.

**Example POST /api/auth/register response:**
```json
{
  "id": "4d199s979mvpfyqmb5jnetyqm9",
  "username": "alice",
  "email": "alice@example.com",
  "firstName": "Alice",
  "lastName": "Anderson",
  "phone": "+1234567890",
  "role": "Customer",
  "message": "User created successfully"
}
```

**Example POST /api/auth/login response:**
```json
{
  "id": "4d199s979mvpfyqmb5jnetyqm9",
  "username": "alice",
  "email": "alice@example.com",
  "phoneNumber": "+1234567890",
  "firstName": "Alice",
  "lastName": "Anderson",
  "roles": ["Customer"]
}
```

**Example GET /api/auth/login response (current user):**
```json
{
  "id": "4d199s979mvpfyqmb5jnetyqm9",
  "username": "alice",
  "email": "alice@example.com",
  "phoneNumber": "+1234567890",
  "firstName": "Alice",
  "lastName": "Anderson",
  "roles": ["Customer"]
}
```

## Bugfix: Array Field Filtering Support, 2025-11-06 08:20

**Issue:** Query filtering on array fields (e.g., `unitType=Weight` or `unitType LIKE Volume`) was not working across GET, expand, and SSE endpoints. Arrays were being converted to string representations instead of checking individual elements, resulting in no matches.

**Root Cause:**
1. `ConvertToObj` methods only handled `List<object>`, not other enumerable types like `List<string>` or arrays
2. Filter parsing didn't trim whitespace from keys and values, causing mismatches when spaces were present
3. `ApplySingleFilter` converted entire arrays to strings for comparison instead of checking each element individually

**Solution:**
1. Changed `ConvertToObj` to handle any `IEnumerable` (except strings) by checking `kvp.Value is System.Collections.IEnumerable enumerable && kvp.Value is not string`
2. Added `.Trim()` to both keys and values during filter parsing to handle spaces around operators
3. Modified `ApplySingleFilter` to detect `Arr` types and iterate through elements, returning true if any element matches the filter condition

**Affected Files:**
- `backend/RestRoutes/GetRoutes.QueryFilters.cs:73-89`: Updated `ConvertToObj` to handle any IEnumerable
- `backend/RestRoutes/GetRoutes.QueryFilters.cs:196-203`: Added trimming to filter key/value parsing
- `backend/RestRoutes/GetRoutes.QueryFilters.cs:238-259`: Added array element checking in `ApplySingleFilter`
- `backend/RestRoutes/SseEndpoints.cs:128-144, 246-252`: Applied same fixes to SSE endpoint filtering
- `backend/RestRoutes/SseBackgroundService.cs:329-344, 436-443, 476-496`: Applied same fixes to background service filtering

**Result:** Array field filtering now works consistently across all endpoints (GET, expand, SSE). Any operator (=, !=, >, <, >=, <=, LIKE) can filter on array fields by checking if any element matches the condition.

**Testing:** Successfully tested filtering on `unitType` array field with both exact match (`unitType=Weight`) and partial match (`unitType LIKE Wei`), returning all items where any element in the array matches.

**Example queries that now work:**
```
GET /api/Ingredient?where=unitType=Weight
GET /api/Ingredient?where=unitType LIKE Vol
GET /api/sse/Ingredient?where=unitType=Volume
```

**Technical Note:** The fix treats array filtering as an OR operation - if ANY element in the array matches the condition, the item is included in results. This is the expected behavior for multi-select fields like taxonomy or enum arrays.

## Feature: Server-Sent Events (SSE) for Real-Time Updates, 2025-11-06 08:45

**Issue:** No real-time update mechanism existed for clients to receive new content items as they were created. Clients had to poll REST endpoints repeatedly to check for changes.

**Solution:** Implemented Server-Sent Events (SSE) with three components:
1. **SseConnectionManager**: Manages active SSE connections grouped by content type
2. **SseEndpoints**: Provides `/api/sse/{contentType}` endpoint that sends initial data and maintains connection
3. **SseBackgroundService**: Background service that polls database every 3 seconds for new items and broadcasts to matching connections

**Key Features:**
- Generic endpoint works with any content type: `/api/sse/{contentType}`
- Supports same query filters as GET routes: `?where=field=value` with all operators (=, !=, >, <, >=, <=, LIKE, AND)
- Efficient: Single database query per content type per interval, broadcasts to all matching connections
- Sends heartbeat every 20 seconds to keep connections alive
- Applies filters to both initial data and new items
- Automatic cleanup of disconnected clients

**Affected Files:**
- `backend/RestRoutes/SseConnectionManager.cs`: New file - Connection tracking and management
- `backend/RestRoutes/SseEndpoints.cs`: New file - SSE HTTP endpoint with initial data and heartbeat
- `backend/RestRoutes/SseBackgroundService.cs`: New file - Background polling and broadcasting service
- `backend/Program.cs:10-12`: Registered SSE services (singleton manager, hosted background service)
- `backend/RestRoutes/SetupRoutes.cs:15`: Added `app.MapSseEndpoints()` to register routes
- `backend/RestRoutes/GetRoutes.Population.cs:7,47,81`: Made collection methods public for SSE reuse
- `backend/RestRoutes/GetRoutes.Cleanup.cs:7`: Made CleanObject public for SSE reuse

**Configuration Constants:**
- `POLLING_INTERVAL_MS = 3000`: How often to check database for new items (3 seconds)
- `HEARTBEAT_INTERVAL_MS = 20000`: How often to send keepalive to clients (20 seconds)

**Result:** Clients can now connect to SSE endpoints and receive real-time notifications when new content items are created, with the same filtering capabilities as REST endpoints.

**Testing:** Successfully tested SSE connection with filtered endpoint, verified initial data arrives immediately, and confirmed new items broadcast within 3 seconds of creation via both REST API and Orchard admin panel.

**Example SSE connection:**
```
GET /api/sse/Ingredient?where=unitType=Volume

# Initial event (immediate):
event: initial
data: [{"id":"41b4gw9e2zsptwc7fpb9570b5s","title":"Mjölk","unitType":["Volume"]}]

# Heartbeat (every 20s):
: heartbeat

# New item event (within 3s of creation):
event: new
data: {"id":"4zs5m9n01j1wk2y2ky47jvkkmt","title":"Juice","unitType":["Volume"]}
```

**Technical Notes:**
- SSE uses text/event-stream content type with proper headers (no-cache, keep-alive, X-Accel-Buffering: no)
- Background service uses OrchardCore's shell context to access scoped YesSql sessions: `shellHost.GetScopeAsync(shellContext.Settings)`
- Only detects NEW items (CreatedUtc > lastCheckTime), not updates or deletes
- Multiple clients can connect to same endpoint - only one database query per content type per interval
- Filters apply to both initial data and new items consistently

**Client Usage Example (JavaScript):**
```javascript
const eventSource = new EventSource('/api/sse/Ingredient?where=unitType=Volume');

eventSource.addEventListener('initial', (e) => {
  const items = JSON.parse(e.data);
  console.log('Initial items:', items);
});

eventSource.addEventListener('new', (e) => {
  const item = JSON.parse(e.data);
  console.log('New item:', item);
});
```

**Testing Note:** In Chromium-based browsers (Chrome, Edge, etc.) you can test SSE automatically by entering the route directly in the browser address field (e.g., `http://localhost:5001/api/sse/Ingredient?where=unitType=Volume`). The browser will display the SSE events as they arrive. However, you cannot test SSE routes in tools like ThunderClient as they do not support the streaming nature of Server-Sent Events.

## Feature: $push Operator for BagPart Array Operations, 2025-11-06 09:00

**Issue:** When updating BagPart content (e.g., Recipe with IngredientAmount items), the entire `items` array had to be reposted via PUT, even to add a single item. For content with many BagPart items (e.g., an Auction with 100+ Bid items), this was inefficient and error-prone.

**Solution:** Implemented MongoDB-style `$push` operator for PUT requests that appends new items to existing BagPart arrays without requiring the full array to be sent. The implementation includes two quality-of-life improvements:

1. **Automatic contentType Inference**: When using `$push`, the `contentType` field is automatically inferred from existing BagPart items, so it can be omitted from new items
2. **Automatic Content Item ID Detection**: String values that match the content item ID format (26 alphanumeric characters) are automatically treated as ContentPicker references, even without the "Id" suffix

**Key Differences from POST/PUT:**

| Feature | POST (create) | PUT (replace) | PUT ($push) |
|---------|--------------|---------------|-------------|
| Operation | Create new item | Replace all BagPart items | Append to existing items |
| contentType | **Required** | **Required** | **Optional** (inferred) |
| Existing items | N/A | Discarded | Preserved + new appended |

**Note:** In all cases, content item ID references are automatically detected (26-char alphanumeric strings), whether the field ends with "Id" or not.

**Affected Files:**
- `backend/RestRoutes/PutRoutes.cs:94-161`: Added `$push` detection, contentType inference from existing items, and auto-detection of content item IDs
- `backend/RestRoutes/PostRoutes.cs:418-433`: Added auto-detection of content item IDs in `CreateBagPartItem`

**Result:**
- BagPart items can be appended efficiently using `{"items": {"$push": [...]}}` syntax
- contentType automatically inferred from existing items when using $push (not needed for first item)
- Content item IDs recognized automatically whether field ends with "Id" or not (e.g., both `"ingredientId"` and `"ingredient"` work)
- Significantly reduces payload size and complexity for incremental updates

**Testing:** Successfully tested creating Recipe with 1 item, then using $push to append 2 more items. Verified all 3 items present after operation and references properly expanded via `/api/expand` endpoint.

**Example: Standard PUT (replaces all items):**
```json
PUT /api/Recipe/4ctx3055kk3c6sq2abvr83s2n8
{
  "items": [
    {
      "contentType": "IngredientAmount",
      "ingredientId": "41b4gw9e2zsptwc7fpb9570b5s",
      "amount": 100,
      "unit": "g"
    },
    {
      "contentType": "IngredientAmount",
      "ingredientId": "4jyh1gfcjhv3k7y0zj57tbcqm5",
      "amount": 50,
      "unit": "g"
    }
  ]
}
```

**Example: PUT with $push (appends items):**
```json
PUT /api/Recipe/4ctx3055kk3c6sq2abvr83s2n8
{
  "items": {
    "$push": [
      {
        "ingredient": "4vktmhqt3cttwtqc95y31nq5wn",
        "amount": 3,
        "unit": "pieces"
      }
    ]
  }
}
```

**Result after $push (3 total items):**
```json
GET /api/expand/Recipe/4ctx3055kk3c6sq2abvr83s2n8
{
  "id": "4ctx3055kk3c6sq2abvr83s2n8",
  "title": "Test Recipe",
  "items": [
    {
      "id": "4ttryygd5ctj07eqctvhz3zfm5",
      "ingredient": {
        "id": "41b4gw9e2zsptwc7fpb9570b5s",
        "title": "Mjölk",
        "unitType": ["Volume"]
      },
      "amount": 100,
      "unit": "g"
    },
    {
      "id": "4en77tenw7m7zrx1gjasj9rjmj",
      "ingredient": {
        "id": "4jyh1gfcjhv3k7y0zj57tbcqm5",
        "title": "Oboy",
        "unitType": ["Weight"]
      },
      "amount": 50,
      "unit": "g"
    },
    {
      "ingredient": {
        "id": "4vktmhqt3cttwtqc95y31nq5wn",
        "title": "Ägg",
        "unitType": ["Weight"]
      },
      "amount": 3,
      "unit": "pieces"
    }
  ]
}
```

**Technical Notes:**
- contentType inference only works when existing items are present; first item creation via POST must specify contentType explicitly
- Content item ID auto-detection checks for exactly 26 alphanumeric characters (Orchard Core's standard ID format)
- Both `"ingredientId": "abc123..."` (explicit) and `"ingredient": "abc123..."` (auto-detected) work identically
- The $push operator preserves existing item IDs, order, and all properties - only appends new items to the end
- Multiple items can be pushed in a single request by including multiple objects in the $push array

**Use Cases:**
- **Auction bidding**: Add new Bid to Auction without resending all previous bids
- **Recipe management**: Add ingredients incrementally without reposting full recipe
- **Order items**: Append products to order as customer shops
- **Comment threads**: Add new comments to post without fetching all existing comments

## Feature: Named BagPart Support, 2025-11-06 09:40

**Background:** Orchard Core allows adding multiple BagPart instances to a single content type using the "Add Named Part" feature. For example, a Recipe might have both a default BagPart (for ingredients) and a named BagPart called "Step" (for recipe steps). Previously, the REST API only recognized the default BagPart mapped to the "items" field and ignored all named BagParts.

**Issue:** When Team 2 created a Recipe with both a default BagPart (IngredientAmount items) and a named BagPart "Step" (RecipeStep items), only the "items" array was returned in GET responses. The "step" field was completely missing despite being visible in the raw Orchard data.

**Solution:** Made BagPart detection generic to automatically detect and process ANY field containing a "ContentItems" array structure:

1. **GET Routes (Cleanup):** Modified to iterate through all object properties and detect any field with a "ContentItems" array, not just hardcoded "BagPart"
2. **POST Routes:** Added detection for arrays where first element has "contentType" property (BagPart signature)
3. **PUT Routes:** Extended both regular replacement and $push operations to support named BagParts
4. **Field Naming:** Automatically converts between Orchard format and API format:
   - `"BagPart"` → `"items"` (backwards compatible)
   - `"Step"` → `"step"`
   - `"Ingredients"` → `"ingredients"`

**Key Detection Logic:**
```csharp
// Detects BagPart by checking for contentType in first array element
var firstElement = jsonArrayElement.EnumerateArray().FirstOrDefault();
if (firstElement.ValueKind == JsonValueKind.Object &&
    firstElement.TryGetProperty("contentType", out _))
{
    // This is a BagPart field
}
```

**Affected Files:**
- `backend/RestRoutes/GetRoutes.Cleanup.cs:46-91`: Generic BagPart detection for any field with ContentItems array
- `backend/RestRoutes/PostRoutes.cs:79-119`: Detects and creates named BagParts during POST
- `backend/RestRoutes/PutRoutes.cs:84-219`: Handles both replacement and $push operations for named BagParts

**Result:** All BagParts (default and named) are now automatically supported across GET, POST, PUT, and $push operations. Recipe now correctly returns both "items" (IngredientAmount) and "step" (RecipeStep) arrays.

**Safety:** The detection logic is completely safe and won't conflict with ContentPicker fields:
- ContentPicker unpopulated: Array of strings → Not detected as BagPart
- ContentPicker populated: Objects with "id"/"title" but NO "contentType" → Not detected as BagPart
- BagPart: Objects WITH "contentType" property → Correctly detected

**Testing:**
- Created Recipe with both default BagPart ("items") and named BagPart ("step") via POST
- Verified GET returns both "items" and "step" arrays correctly
- Tested $push on named BagPart field: `{"step": {"$push": [...]}}` successfully appended items
- Tested ContentPicker fields remain unaffected (Pet with ownerId still works)

**Examples:**

POST with multiple BagParts:
```json
{
  "title": "Chocolate Drink Recipe",
  "items": [
    {
      "contentType": "IngredientAmount",
      "amount": 100,
      "unit": ["ml"],
      "ingredientId": "abc123..."
    }
  ],
  "step": [
    {
      "contentType": "RecipeStep",
      "title": "Mix ingredients",
      "description": "Stir well"
    }
  ]
}
```

GET response:
```json
{
  "id": "xyz789...",
  "title": "Chocolate Drink Recipe",
  "items": [...],
  "step": [...]
}
```

$push to named BagPart:
```json
{
  "step": {
    "$push": [
      {
        "contentType": "RecipeStep",
        "title": "Serve cold",
        "description": "Add ice"
      }
    ]
  }
}
```

## Known Limitation: Field Validation Requires Existing Content Item (WON'T FIX FOR NOW), 2025-11-06 10:00

**Issue:** When using POST/PUT endpoints to create/update content for a content type with NO existing items, optional fields or numeric fields with default values may not be accepted. The API works correctly AFTER creating at least one item via the Orchard Core admin interface.

**Root Cause:** The `FieldValidator.cs` uses two approaches to determine which fields are valid for a content type:

1. **If items exist**: Extracts field names from existing content items (lines 13-28)
2. **If NO items exist**: Creates a temporary item called `"_temp_schema_item"`, extracts its field schema, then deletes it (lines 30-45)

The temporary item approach can miss fields that are:
- Optional (not initialized on new items)
- Numeric with default values (may not appear in serialized output)
- Computed or lazy-loaded fields
- Fields that require specific initialization logic

**Affected Files:**
- `backend/RestRoutes/FieldValidator.cs:30-45`: Temporary item creation for schema extraction when no items exist

**Workaround:**
Create one "template" or "schema" item for each content type via the Orchard Core admin interface:
1. Navigate to Content → [Your Content Type] → New [Content Type]
2. Fill in a placeholder title (e.g., `"_template"` or `"_schema_example"`)
3. Fill in all fields, including optional ones, to establish the full field schema
4. Save the item
5. REST API POST/PUT will now work correctly for all fields
6. Optionally, use a naming convention (e.g., prefix with `"_"`) to make template items easy to filter out in queries

**Alternative Workaround:** Use `?where=title!=_template` in your GET requests to exclude template items from results.

**Technical Notes:**
- This limitation only affects content types with ZERO existing items
- Once any item exists (admin-created or REST-created), the REST API extracts the field schema from real items instead of using the temp item approach
- The temp item `"_temp_schema_item"` is created and immediately deleted during field validation - it never persists in the database
- Future fix would require a more comprehensive schema extraction method, possibly using Orchard Core's content definition APIs directly instead of relying on item instances

**Example Template Item Naming:**
```json
{
  "title": "_template_Recipe",
  "description": "Template item for schema - do not delete",
  "cookTime": 0,
  "servings": 1
}
```

**Reported By:** Team 1 (4th of November 2025)

## Bugfix: Fields Ending with "Id" Treated as ContentPicker References, 2025-11-06 10:40

**Issue:** Field names ending with "Id" (case-insensitive) were being treated as ContentPicker ID references even when the value was a number or boolean. For example, a field named `"startBid"` with numeric value `300` would be incorrectly processed as if it were a ContentPicker field called `"StartB"` with ContentItemIds, resulting in the value being saved as an empty object `{}` in the database.

**Root Cause:** The code checked `if (kvp.Key.EndsWith("Id", StringComparison.OrdinalIgnoreCase))` BEFORE checking the value type. This meant that ANY field ending with "Id" (like "startBid", "rapidId", "validId", etc.) would be routed to the ContentPicker handling code, even when the value was clearly not a content item ID (numbers and booleans can never be ContentItem IDs).

**Impact:** This bug affected:
- Numeric fields with names ending in "id": `startBid`, `minimumBid`, `maximumBid`, `rapidId`, `fluidId`, etc.
- Boolean fields with names ending in "id": `isValidId`, `hasRapidId`, etc.
- Any field whose name happened to end with "id" where the value wasn't a string ID

**Solution:** Added a type check BEFORE the field name check. The code now:
1. First checks if the value is a number or boolean (`JsonValueKind.Number`, `JsonValueKind.True`, `JsonValueKind.False`)
2. If so, skips the "Id" suffix handling entirely (numbers/booleans can't be ContentItem IDs)
3. Only applies the ContentPicker logic to fields ending with "Id" when the value is a string or array

**Code Change:**
```csharp
// Added BEFORE the EndsWith("Id") check:
bool isNumberOrBoolean = false;
if (value is JsonElement checkElement)
{
    isNumberOrBoolean = checkElement.ValueKind == JsonValueKind.Number ||
                      checkElement.ValueKind == JsonValueKind.True ||
                      checkElement.ValueKind == JsonValueKind.False;
}

// Modified check:
if (!isNumberOrBoolean &&
    kvp.Key.EndsWith("Id", StringComparison.OrdinalIgnoreCase) &&
    kvp.Key.Length > 2)
```

**Affected Files:**
- `backend/RestRoutes/PostRoutes.cs:121-134`: Added type check before EndsWith("Id") in main loop
- `backend/RestRoutes/PostRoutes.cs:422-429`: Added type check in CreateBagPartItem helper
- `backend/RestRoutes/PutRoutes.cs:221-234`: Added type check before EndsWith("Id") in main loop
- `backend/RestRoutes/PutRoutes.cs:491-498`: Added type check in CreateBagPartItem helper

**Result:** Numeric and boolean fields with names ending in "Id" now work correctly across POST and PUT operations. Values are properly saved with their correct types instead of becoming empty objects.

**Testing:** We don't have an example in our test database, but this was reported and confirmed by Team 1 using their `ProblemAuction` content type with a `startBid` numeric field. After the fix, their test showed:
- Before: `POST {"startBid": 777}` → Database: `"StartBid": {}`
- After: `POST {"startBid": 777}` → Database: `"StartBid": {"Value": 777}`

**Example from Team 1's Bug Report:**

POST request:
```json
POST /api/ProblemAuction
{
  "title": "Test Auction",
  "startBid": 300
}
```

Raw database content BEFORE fix:
```json
{
  "ProblemAuction": {
    "StartBid": {},
    "Seller": { ... }
  }
}
```

Raw database content AFTER fix:
```json
{
  "ProblemAuction": {
    "StartBid": {
      "Value": 300
    },
    "Seller": { ... }
  }
}
```

GET response (cleaned) after fix:
```json
{
  "id": "4yq9bf6j7ak060s7hjteb1gnnd",
  "title": "Test Auction",
  "startBid": 300,
  "seller": []
}
```

**Technical Note:** This fix is safe and won't affect legitimate ContentPicker fields because:
- ContentPicker values are always strings (single ID) or arrays of strings (multiple IDs)
- Numbers and booleans physically cannot be content item IDs in Orchard Core
- The type check happens first, so legitimate ID references are unaffected

**Reported By:** Team 1 (4th of November 2025) - Thank you Team 1 for the detailed bug report and test case!

## Bugfix: UserPickerField Broken When Field Name Ends with "Id", 2025-11-06 11:45

**Issue:** UserPickerFields with field names ending in "Id" (case-insensitive) were being misrouted to ContentPicker logic, causing POST/PUT requests to fail or return empty objects. This affected common field names like `userId`, `driverId`, `ownerId`, `assignedUserId`, etc.

**Root Cause:** The field name check for ContentPicker references (`EndsWith("Id")`) was executed BEFORE the UserPickerField detection logic. When a request included `"userId": [{id: "...", username: "..."}]`, the code would:
1. See field name ends with "Id"
2. Route to ContentPicker logic (expecting string or string array)
3. Fail because ContentPicker logic received an array of objects instead
4. Never reach the UserPickerField detection logic further down

**Impact:** UserPickerFields could not use field names ending in "Id", forcing developers to use awkward field names like `user`, `driver`, `assignedUser` instead of the more natural `userId`, `driverId`, `assignedUserId`.

**Solution:** Added early UserPickerField detection BEFORE the field name "Id" suffix check. The code now:
1. Checks if value is an array of objects with both `id` and `username` properties
2. If yes, marks it as UserPickerField and skips the ContentPicker "Id" logic
3. Allows the value to continue to the UserPickerField handling in the array logic section

**Code Fix:**
```csharp
// Check if this is a UserPickerField BEFORE checking field name ending with "Id"
// UserPickerFields are arrays of objects with both "id" and "username" properties
bool isUserPickerField = false;
if (value is JsonElement checkElement2 && checkElement2.ValueKind == JsonValueKind.Array)
{
    var firstElement = checkElement2.EnumerateArray().FirstOrDefault();
    if (firstElement.ValueKind == JsonValueKind.Object &&
        firstElement.TryGetProperty("id", out _) &&
        firstElement.TryGetProperty("username", out _))
    {
        isUserPickerField = true;
    }
}

// Handle fields ending with "Id" - these are content item references
// BUT skip this if the value is a number or boolean (e.g., "startBid" with number value)
// OR if it's a UserPickerField (which should be handled later in the array logic)
if (!isNumberOrBoolean &&
    !isUserPickerField &&
    kvp.Key.EndsWith("Id", StringComparison.OrdinalIgnoreCase) &&
    kvp.Key.Length > 2)
{
    // ContentPicker logic here...
}
```

**Affected Files:**
- `backend/RestRoutes/PostRoutes.cs`: Lines 130-150 (main loop), added early UserPickerField detection
- `backend/RestRoutes/PutRoutes.cs`: Lines 230-250 (main loop), added early UserPickerField detection

**Example from Team 4:**

Before fix - POST request that failed:
```json
POST /api/Car
{
  "brand": "Volvo",
  "model": "V70",
  "userId": [{
    "id": "4d199s979mvpfyqmb5jnetyqm9",
    "username": "testdriver"
  }]
}
```

Expected database structure (AFTER fix):
```json
{
  "Car": {
    "Brand": { "Text": "Volvo" },
    "Model": { "Text": "V70" },
    "UserId": {
      "UserIds": ["4d199s979mvpfyqmb5jnetyqm9"],
      "UserNames": ["testdriver"]
    }
  }
}
```

**Additional Fix - Auth Endpoints:** Also added missing `id` field to POST `/api/auth/register` response in Team 4's code to match documentation. All auth endpoints (register, login, get current user) now consistently return the user ID.

**Technical Note:** This fix is safe because:
- UserPickerFields are ALWAYS arrays of objects with `{id, username}` structure
- ContentPicker fields with "Id" suffix are ALWAYS strings or string arrays
- The detection is specific and cannot cause false positives
- The check happens before the "Id" suffix logic, so it has priority

**Reported By:** Team 4 (4th of November 2025) - Thank you Team 4 for reporting this issue!

## Bugfix: Query Filtering on Array Fields and Nested Properties, 2025-11-06 11:55

**Issue:** Team 1 reported that query filtering was failing with 500 errors in two scenarios:
1. Filtering on TextField: `/api/ArtistInfo?where=workTitle=Artist` - crashed with RuntimeBinderException
2. Filtering on UserPickerField nested property: `/api/ArtistInfo?where=customer.id={id}` - returned empty results

**Root Causes:**
1. **Dictionary Conversion Error**: The `ConvertToObj` function was passing `Dictionary<string, object>` values directly to Dyndata's `Obj[key] = value` setter. When the value was a `List<Dictionary<string, object>>` (like UserPickerField data), Dyndata's internal `TryToObjOrArr` method tried to access `.Key` on the Dictionary object (which doesn't exist - only KeyValuePair has that property), causing a RuntimeBinderException.

2. **Missing Array Traversal**: The `GetNestedValue` function didn't handle array fields. When filtering on `customer.id`, it needed to:
   - Recognize that `customer` is an array `[{id: "...", username: "..."}]`
   - Traverse into each array element
   - Extract the `id` property from each element
   - Return an array of those IDs for matching

**Solutions:**

1. **Fixed Dictionary Conversion** (Team 1's GetRoutes.QueryFilters.cs:90-99):
   - Added explicit handling for `List<Dictionary<string, object>>`
   - Recursively convert each dictionary to a Dyndata `Obj` before pushing to array
   - Prevents Dyndata from receiving raw Dictionary objects

2. **Added Array Traversal Support** (Both Team 1 and our GetRoutes.QueryFilters.cs:288-310):
   - Modified `GetNestedValue` to detect when current value is an `Arr`
   - When encountering array during property traversal (e.g., `customer.id`):
     - Iterate through array elements
     - Extract the requested property from each element
     - Return array of extracted values
   - The existing array matching logic in `ApplySingleFilter` then checks if ANY element matches

**Key Code Changes:**

In `ConvertToObj`:
```csharp
// Added after existing List<object> handling
else if (kvp.Value is List<Dictionary<string, object>> dictList)
{
    // Handle List<Dictionary<string, object>> (UserPickerField format)
    var arr = Arr();
    foreach (var item in dictList)
    {
        arr.Push(ConvertToObj(item));  // Convert each dictionary recursively
    }
    obj[kvp.Key] = arr;
}
```

In `GetNestedValue`:
```csharp
else if (current is Arr dynArr)
{
    // If current is an array, traverse into each object and collect the specified property
    // This allows filtering on array.property (e.g., customer.id)
    var results = Arr();
    foreach (var arrItem in dynArr)
    {
        if (arrItem is Obj arrObj && arrObj.HasKey(part))
        {
            results.Push(arrObj[part]);
        }
    }

    if (results.Length > 0)
    {
        current = results;
    }
    else
    {
        return null;
    }
}
```

**Affected Files:**
- Team 1's `backend/RestRoutes/GetRoutes.QueryFilters.cs:64-106`: Added `List<Dictionary<string, object>>` handling
- Team 1's `backend/RestRoutes/GetRoutes.QueryFilters.cs:277-317`: Added array traversal in `GetNestedValue`
- Our `backend/RestRoutes/GetRoutes.QueryFilters.cs:278-318`: Applied array traversal fix (dictionary handling already covered by IEnumerable)

**Result:**
- TextField filtering now works without crashes: `/api/ArtistInfo?where=workTitle=Artist` returns matching results
- UserPickerField filtering now works: `/api/ArtistInfo?where=customer.id={id}` returns items where any user in the customer array has the specified ID
- Filtering on nested array properties now works for all field types (UserPickerField, ContentPicker arrays, etc.)

**Testing:**

Team 1's database:
- Successfully tested `/api/ArtistInfo?where=workTitle=Artist` - returns 1 result with `workTitle: "Artist"`
- Successfully tested `/api/ArtistInfo?where=customer.id=4ef691565ferj1zdg3e1gcp24w` - returns items associated with that user
- Both queries return proper JSON without crashes

Our database (port 5001):
- Successfully tested `/api/ArtistInfo?where=customer.id=4hrarr86148ac309m1wz6tf9n6` - returns 2 results: "Mega-Carl" (only carl) and "The C-duo" (carl + caroline)
- Successfully tested `/api/ArtistInfo?where=customer.username=alice` - returns 1 result: "Alice's Art"
- Array filtering works correctly, matching ANY element in the customer UserPickerField array

**Technical Note:** Our main codebase already handled `List<Dictionary<string, object>>` through the more general `IEnumerable` check at line 73, so only the array traversal fix was needed. Team 1's code needed both fixes.

**Reported By:** Team 1 (6th of November 2025) - Thank you Team 1 for reporting this filtering issue!

**Note:** Team 1's code didn't have the earlier bugfixes from today (UserPickerField "Id" suffix fix and numeric field "Id" suffix fix) when they reported this issue, which is why they encountered the Dictionary conversion bug. Our main codebase already had the IEnumerable handling from earlier work, so we only needed the array traversal fix.
