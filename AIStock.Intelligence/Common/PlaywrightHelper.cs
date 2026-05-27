using Microsoft.Playwright;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AIStock.Intelligence.Common;

/// <summary>
/// Playwright 代理工具类
/// </summary>
public class PlaywrightHelper
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<PlaywrightHelper> _logger;

    public PlaywrightHelper(IConfiguration configuration, ILogger<PlaywrightHelper> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// 创建带代理的浏览器实例
    /// </summary>
    public async Task<(IBrowser browser, IPlaywright playwright)> CreateBrowserAsync(bool useProxy = false)
    {
        var playwright = await Playwright.CreateAsync();

        var launchOptions = new BrowserTypeLaunchOptions
        {
            Headless = true
        };

        // 只有明确指定时才使用代理
        if (useProxy)
        {
            var tunnelHost = _configuration["Proxy:TunnelHost"] ?? "c360.kdltps.com";
            var tunnelPort = _configuration.GetValue<int>("Proxy:TunnelPort", 15818);
            var tunnelUsername = _configuration["Proxy:TunnelUsername"] ?? "";
            var tunnelPassword = _configuration["Proxy:TunnelPassword"] ?? "";

            launchOptions.Proxy = new Proxy
            {
                Server = $"http://{tunnelHost}:{tunnelPort}",
                Username = tunnelUsername,
                Password = tunnelPassword
            };

            _logger.LogInformation("Playwright using tunnel proxy: {Host}:{Port}", tunnelHost, tunnelPort);
        }

        var browser = await playwright.Chromium.LaunchAsync(launchOptions);
        return (browser, playwright);
    }

    /// <summary>
    /// 创建带代理的页面
    /// </summary>
    public async Task<(IPage page, IBrowser browser, IPlaywright playwright)> CreatePageAsync(bool useProxy = false)
    {
        var (browser, playwright) = await CreateBrowserAsync(useProxy);
        var page = await browser.NewPageAsync();
        return (page, browser, playwright);
    }

    /// <summary>
    /// 执行带代理的浏览器操作
    /// </summary>
    public async Task<T> ExecuteWithBrowserAsync<T>(Func<IPage, Task<T>> action, bool useProxy = false)
    {
        var (browser, playwright) = await CreateBrowserAsync(useProxy);
        try
        {
            var page = await browser.NewPageAsync();
            return await action(page);
        }
        finally
        {
            await browser.CloseAsync();
            playwright.Dispose();
        }
    }

    /// <summary>
    /// 获取代理配置
    /// </summary>
    public ProxyConfig GetProxyConfig()
    {
        return new ProxyConfig
        {
            UseTunnelProxy = _configuration.GetValue<bool>("Proxy:UseTunnelProxy"),
            TunnelHost = _configuration["Proxy:TunnelHost"] ?? "c360.kdltps.com",
            TunnelPort = _configuration.GetValue<int>("Proxy:TunnelPort", 15818),
            TunnelUsername = _configuration["Proxy:TunnelUsername"] ?? "",
            TunnelPassword = _configuration["Proxy:TunnelPassword"] ?? ""
        };
    }
}

/// <summary>
/// 代理配置
/// </summary>
public class ProxyConfig
{
    public bool UseTunnelProxy { get; set; }
    public string TunnelHost { get; set; } = string.Empty;
    public int TunnelPort { get; set; }
    public string TunnelUsername { get; set; } = string.Empty;
    public string TunnelPassword { get; set; } = string.Empty;

    public string GetProxyUrl()
    {
        return $"http://{TunnelHost}:{TunnelPort}";
    }
}
