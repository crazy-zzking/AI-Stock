using Microsoft.Playwright;

namespace AIStock.Intelligence;

/// <summary>
/// Playwright 初始化辅助类
/// </summary>
public static class PlaywrightInitializer
{
    /// <summary>
    /// 确保 Playwright 浏览器已安装
    /// </summary>
    public static async Task EnsureInstalledAsync()
    {
        try
        {
            // 尝试创建浏览器实例来检查是否已安装
            using var playwright = await Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true
            });
            await browser.CloseAsync();
            Console.WriteLine("Playwright Chromium is installed and ready.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Playwright browser not found: {ex.Message}");
            Console.WriteLine("Run 'playwright install chromium' to install.");
            throw;
        }
    }
}
