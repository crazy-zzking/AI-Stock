using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AIStock.Monitor;

public static class DependencyInjection
{
    /// <summary>
    /// 注册监控服务。告警通知器按配置选择：配了 WebhookUrl 用 Webhook（同时写日志），否则仅日志。
    /// 配置绑定与后台宿主由调用方（Web/Worker）负责。
    /// </summary>
    public static IServiceCollection AddMonitorServices(this IServiceCollection services)
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

        services.AddScoped<PortfolioMonitorService>();
        return services;
    }
}
