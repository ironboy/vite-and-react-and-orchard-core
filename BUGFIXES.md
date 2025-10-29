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
