namespace Helper.Api.Hosting;

internal static class ApiWebApplicationBuilderFactory
{
    public static WebApplicationBuilder Create(string[] args, string bootstrapDistRoot)
    {
        if (!Directory.Exists(bootstrapDistRoot))
        {
            return WebApplication.CreateBuilder(args);
        }

        return WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            WebRootPath = bootstrapDistRoot
        });
    }

    public static void LogStaticRootSelection(string bootstrapDistRoot)
    {
        if (Directory.Exists(bootstrapDistRoot))
        {
            Console.WriteLine($"[Startup] Web root configured: {bootstrapDistRoot}");
            return;
        }

        Console.WriteLine($"[Startup] Web root not found at {bootstrapDistRoot}. Static UI hosting disabled.");
    }
}
