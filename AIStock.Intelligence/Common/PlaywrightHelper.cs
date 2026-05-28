using Microsoft.Playwright;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AIStock.Intelligence.Common;

/// <summary>
/// Playwright 代理工具类
/// </summary>
public class PlaywrightHelper : IAsyncDisposable
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<PlaywrightHelper> _logger;
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _disposed;

    public PlaywrightHelper(IConfiguration configuration, ILogger<PlaywrightHelper> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// 获取或创建浏览器实例
    /// </summary>
    private async Task<IBrowser> GetOrCreateBrowserAsync(bool useProxy = false)
    {
        if (_browser != null && _browser.IsConnected)
            return _browser;

        await _lock.WaitAsync();
        try
        {
            if (_browser != null && _browser.IsConnected)
                return _browser;

            _playwright = await Playwright.CreateAsync();

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

            _browser = await _playwright.Chromium.LaunchAsync(launchOptions);
            return _browser;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// 创建带代理的浏览器实例
    /// </summary>
    public async Task<(IBrowser browser, IPlaywright playwright)> CreateBrowserAsync(bool useProxy = false)
    {
        var browser = await GetOrCreateBrowserAsync(useProxy);
        return (browser, _playwright!);
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
        var browser = await GetOrCreateBrowserAsync(useProxy);
        var page = await browser.NewPageAsync();
        try
        {
            return await action(page);
        }
        finally
        {
            await page.CloseAsync();
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

    public async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            if (_browser != null)
            {
                await _browser.CloseAsync();
                _browser = null;
            }
            _playwright?.Dispose();
            _playwright = null;
            _lock.Dispose();
            _disposed = true;
        }
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
