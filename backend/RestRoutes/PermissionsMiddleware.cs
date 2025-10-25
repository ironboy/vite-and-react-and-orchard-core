namespace RestRoutes;

using System.Security.Claims;

public static class PermissionsMiddleware
{
    public static async Task<IResult?> CheckPermissions(
        string contentType,
        string httpMethod,
        HttpContext context,
        YesSql.ISession session)
    {
        // Fetch all RestPermissions
        var permissions = await GetRoutes.FetchCleanContent("RestPermissions", session, populate: false);

        // Build permissions lookup: permissionsByRole[role][contentType][restMethod] = true
        var permissionsByRole = new Dictionary<string, Dictionary<string, Dictionary<string, bool>>>();

        if (permissions == null) permissions = new List<Dictionary<string, object>>();

        foreach (var permission in permissions)
        {
            if (permission == null) continue;

            // Safely convert to lists, handling various collection types
            var rolesValue = permission.GetValueOrDefault("roles");
            var roles = new List<string>();
            if (rolesValue is IEnumerable<object> enumRoles)
            {
                foreach (var r in enumRoles)
                {
                    var roleStr = r?.ToString();
                    if (!string.IsNullOrEmpty(roleStr)) roles.Add(roleStr);
                }
            }

            var contentTypesValue = permission.GetValueOrDefault("contentTypes");
            var contentTypes = new List<string>();
            if (contentTypesValue is IEnumerable<object> enumCT)
            {
                foreach (var ct in enumCT)
                {
                    var ctStr = ct?.ToString();
                    if (!string.IsNullOrEmpty(ctStr)) contentTypes.Add(ctStr);
                }
            }

            var restMethodsValue = permission.GetValueOrDefault("restMethods");
            var restMethods = new List<string>();
            if (restMethodsValue is IEnumerable<object> enumRM)
            {
                foreach (var rm in enumRM)
                {
                    var rmStr = rm?.ToString();
                    if (!string.IsNullOrEmpty(rmStr)) restMethods.Add(rmStr);
                }
            }

            foreach (var role in roles)
            {
                if (!permissionsByRole.ContainsKey(role))
                    permissionsByRole[role] = new Dictionary<string, Dictionary<string, bool>>();

                foreach (var ct in contentTypes)
                {
                    if (!permissionsByRole[role].ContainsKey(ct))
                        permissionsByRole[role][ct] = new Dictionary<string, bool>();

                    foreach (var restMethod in restMethods)
                    {
                        permissionsByRole[role][ct][restMethod] = true;
                    }
                }
            }
        }

        // Get user roles (or "Anonymous" if not authenticated)
        var userRoles = new List<string>();
        if (context.User.Identity?.IsAuthenticated == true)
        {
            userRoles = context.User.FindAll(ClaimTypes.Role)
                .Select(c => c.Value)
                .ToList();
        }

        // Always include Anonymous role
        if (!userRoles.Contains("Anonymous"))
        {
            userRoles.Add("Anonymous");
        }

        var requestMethod = httpMethod.ToUpper();

        // Check if any of the user's roles has permission
        var hasPermission = false;
        foreach (var role in userRoles)
        {
            if (permissionsByRole.ContainsKey(role) &&
                permissionsByRole[role].ContainsKey(contentType) &&
                permissionsByRole[role][contentType].ContainsKey(requestMethod))
            {
                hasPermission = true;
                break;
            }
        }

        if (!hasPermission)
        {
            return Results.Json(new
            {
                error = "Forbidden",
                message = $"User does not have permission to {requestMethod} {contentType}"
            }, statusCode: 403);
        }

        // Permission granted, return null to indicate success
        return null;
    }
}
