using System.Text.Json;
using Helper.Api.Hosting;

namespace Helper.Runtime.Tests;

public sealed class ApiStartupCommandsTests
{
    private static readonly object ConsoleSync = new();

    [Fact]
    public void TryHandleConfigInventoryCommand_ReturnsFalse_ForUnrelatedArgs()
    {
        Assert.False(ApiStartupCommands.TryHandleConfigInventoryCommand(["--help"]));
    }

    [Fact]
    public void TryHandleConfigInventoryCommand_WritesJsonInventory_WhenRequested()
    {
        lock (ConsoleSync)
        {
            var originalOut = Console.Out;
            var output = new StringWriter();

            try
            {
                Console.SetOut(output);

                var handled = ApiStartupCommands.TryHandleConfigInventoryCommand(["--dump-config-inventory", "json"]);

                Assert.True(handled);

                using var doc = JsonDocument.Parse(output.ToString());
                Assert.Equal(
                    "src/Helper.Api/Backend/Configuration/BackendEnvironmentInventory.cs",
                    doc.RootElement.GetProperty("source").GetString());
                Assert.True(doc.RootElement.GetProperty("definitions").GetArrayLength() > 0);
                Assert.False(string.IsNullOrWhiteSpace(doc.RootElement.GetProperty("generatedAtUtc").GetString()));
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }
    }
}

