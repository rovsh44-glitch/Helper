using System;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace Helper.Runtime.Infrastructure
{
    public class PlaywrightBrowserService
    {
        public async Task<string> FetchPageAsync(string url)
        {
            using var playwright = await Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
            
            var context = await browser.NewContextAsync(new BrowserNewContextOptions
            {
                UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"
            });

            var page = await context.NewPageAsync();
            
            try
            {
                // Navigate with 30s timeout
                await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 30000 });
                
                // Extract clean text content (Markdown-like)
                var content = await page.ContentAsync();
                return content;
            }
            catch (Exception ex)
            {
                return $"[Playwright Error] Failed to fetch {url}: {ex.Message}";
            }
        }
    }
}

