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
