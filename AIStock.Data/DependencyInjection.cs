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

        // 东方财富（支持隧道代理）
        services.AddSingleton<IDataProvider>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<EastmoneyProvider>>();
            var config = configuration.GetSection("Proxy");
            var useTunnelProxy = config.GetValue<bool>("UseTunnelProxy");

            HttpClient httpClient;
            if (useTunnelProxy)
            {
                var tunnelHost = config["TunnelHost"] ?? "c360.kdltps.com";
                var tunnelPort = config.GetValue<int>("TunnelPort", 15818);
                var tunnelUsername = config["TunnelUsername"] ?? "";
                var tunnelPassword = config["TunnelPassword"] ?? "";
                httpClient = EastmoneyProvider.CreateHttpClientWithTunnelProxy(
                    tunnelHost, tunnelPort, tunnelUsername, tunnelPassword);
                logger.LogInformation("Eastmoney provider using tunnel proxy: {Host}:{Port}", tunnelHost, tunnelPort);
            }
            else
            {
                httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient();
            }

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
