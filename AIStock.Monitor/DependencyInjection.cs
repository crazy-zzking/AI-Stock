using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AIStock.Monitor;

public static class DependencyInjection
{
    /// <summary>
    /// 只注册告警通知器（IAlertNotifier）。配了 WebhookUrl 用 Webhook（同时写日志），否则仅日志。
    /// Worker 等不依赖持仓监控的项目用这个，避免引入 PortfolioMonitorService 的 Execution 依赖。
    /// </summary>
    public static IServiceCollection AddAlerting(this IServiceCollection services)
    {
        services.AddSingleton<IAlertNotifier>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<MonitorOptions>>().Value;
            if (!string.IsNullOrWhiteSpace(options.WebhookUrl))
            {
                return new WebhookAlertNotifier(
                    sp.GetRequiredService<IHttpClientFactory>(),
                    sp.GetRequiredService<ILogger<WebhookAlertNotifier>>(),
                    options.WebhookUrl);
            }
            return new LogAlertNotifier(sp.GetRequiredService<ILogger<LogAlertNotifier>>());
        });
        return services;
    }

    /// <summary>
    /// 注册完整监控服务（告警通知器 + 账户/持仓监控）。需调用方已注册 Execution 服务（IPositionManager 等）。
    /// 配置绑定与后台宿主由调用方（Web/Worker）负责。
    /// </summary>
    public static IServiceCollection AddMonitorServices(this IServiceCollection services)
    {
        services.AddAlerting();
        services.AddScoped<PortfolioMonitorService>();
        return services;
    }
}
