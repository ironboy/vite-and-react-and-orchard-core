namespace RestRoutes;

public static class SetupRoutes
{
    public static void MapRestRoutes(this WebApplication app)
    {
        app.UseRestRoutesExceptionHandler();

        app.MapAuthEndpoints();
        app.MapGetRoutes();
        app.MapPostRoutes();
        app.MapPutRoutes();
        app.MapDeleteRoutes();
    }
}
