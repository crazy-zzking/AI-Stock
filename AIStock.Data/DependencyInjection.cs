using System.Net;
using AIStock.Core.Interfaces;
using AIStock.Data.Providers;
using AIStock.Data.Providers.Eastmoney;
using AIStock.Data.Providers.Sanhu;
using AIStock.Data.Providers.Tencent;
using AIStock.Data.Providers.Tdx;
using AIStock.Data.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AIStock.Data;

/// <summary>
/// 数据源 Provider 的统一注册扩展，供 Web 与 Worker 共用，避免注册逻辑分叉。
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddDataProviders(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IDataProviderResolver, DataProviderResolver>();

        // 交易日历
        services.AddSingleton<ITradingCalendar, TradingCalendarService>();

        // 带重试/熔断的 HttpClient
        services.AddHttpClient("default")
            .AddStandardResilienceHandler(options =>
            {
                options.Retry.MaxRetryAttempts = 3;
                options.Retry.BackoffType = Polly.DelayBackoffType.Exponential;
                options.Retry.Delay = TimeSpan.FromSeconds(1);
                options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(30);
                options.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(1);
                options.CircuitBreaker.FailureRatio = 0.5;
                options.CircuitBreaker.MinimumThroughput = 10;
            });

        // 东财专用 HttpClient：所有东方财富接口（行情/龙虎榜/研报等）统一走隧道代理，防封 IP
        services.AddHttpClient("eastmoney")
            .ConfigurePrimaryHttpMessageHandler(() =>
            {
                var proxy = configuration.GetSection("Proxy");
                if (proxy.GetValue<bool>("UseTunnelProxy"))
                {
                    var host = proxy["TunnelHost"] ?? "c360.kdltps.com";
                    var port = proxy.GetValue<int>("TunnelPort", 15818);
                    var user = proxy["TunnelUsername"] ?? "";
                    var pass = proxy["TunnelPassword"] ?? "";
                    return new HttpClientHandler
                    {
                        Proxy = new WebProxy($"{host}:{port}") { Credentials = new NetworkCredential(user, pass) },
                        UseProxy = true,
                    };
                }
                return new HttpClientHandler();
            })
            .AddStandardResilienceHandler(options =>
            {
                options.Retry.MaxRetryAttempts = 3;
                options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(30);
                options.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(1);
            });

        // 散户量化
        services.AddSingleton<IDataProvider>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<SanhuProvider>>();
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient();
            var config = configuration.GetSection("DataProviders:Sanhu");
            return new SanhuProvider(
                logger,
                httpClient,
                config["BaseUrl"]!,
                config["Token"]!,
                config["MyKey"] ?? "");
        });

        // 东方财富（行情/估值/资金流）— HttpClient 用东财专用 client（带隧道代理 + 重试熔断）
        services.AddSingleton<IDataProvider>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<EastmoneyProvider>>();
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient("eastmoney");
            return new EastmoneyProvider(logger, httpClient);
        });

        // 腾讯财经
        services.AddSingleton<IDataProvider>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<TencentProvider>>();
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient();
            return new TencentProvider(logger, httpClient);
        });

        // 通达信
        services.AddSingleton<IDataProvider>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<TdxProvider>>();
            var host = configuration["Tdx:Host"] ?? "119.147.212.81";
            var port = configuration.GetValue<int>("Tdx:Port", 7709);
            return new TdxProvider(logger, host, port);
        });

        return services;
    }

    /// <summary>
    /// 启动时将所有已注册的 IDataProvider 灌入 Resolver。
    /// </summary>
    public static void InitializeDataProviders(this IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IDataProviderResolver>();
        foreach (var provider in scope.ServiceProvider.GetServices<IDataProvider>())
        {
            resolver.RegisterProvider(provider);
        }
    }
}
