using AIStock.Core.Interfaces;
using AIStock.Core.Models;
using AIStock.Risk.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AIStock.Risk;

public static class DependencyInjection
{
    public static IServiceCollection AddRiskServices(this IServiceCollection services, IConfiguration? configuration = null)
    {
        if (configuration != null)
            services.Configure<RiskConfig>(configuration.GetSection("Risk"));

        services.AddScoped<IRiskEngine>(sp =>
        {
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<RiskEngineService>>();
            var options = sp.GetService<Microsoft.Extensions.Options.IOptions<RiskConfig>>();
            var config = options?.Value ?? new RiskConfig();
            return new RiskEngineService(logger, config);
        });

        services.AddScoped<IPositionSizer, PositionSizerService>();
        services.AddScoped<IStopLossSystem, StopLossSystemService>();
        return services;
    }
}
