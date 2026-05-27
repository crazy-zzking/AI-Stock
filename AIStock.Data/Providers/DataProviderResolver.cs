using AIStock.Core.Enums;
using AIStock.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace AIStock.Data.Providers;

/// <summary>
/// 数据源解析器实现
/// </summary>
public class DataProviderResolver : IDataProviderResolver
{
    private readonly List<IDataProvider> _providers = new();
    private readonly Dictionary<DataCapability, List<IDataProvider>> _capabilityProviders = new();
    private readonly ILogger<DataProviderResolver> _logger;
    private readonly object _lock = new();

    public DataProviderResolver(ILogger<DataProviderResolver> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 获取指定能力的数据提供者
    /// </summary>
    public IEnumerable<IDataProvider> GetProviders(DataCapability capability)
    {
        lock (_lock)
        {
            if (_capabilityProviders.TryGetValue(capability, out var providers))
            {
                return providers.ToList();
            }

            // 如果没有显式配置，返回所有支持该能力的提供者
            var matchingProviders = _providers
                .Where(p => p.Capabilities.Contains(capability))
                .ToList();

            _capabilityProviders[capability] = matchingProviders;
            return matchingProviders.ToList();
        }
    }

    /// <summary>
    /// 获取指定能力的首选数据提供者
    /// </summary>
    public IDataProvider? GetPrimaryProvider(DataCapability capability)
    {
        return GetProviders(capability).FirstOrDefault();
    }

    /// <summary>
    /// 注册数据提供者
    /// </summary>
    public void RegisterProvider(IDataProvider provider)
    {
        if (provider == null)
            throw new ArgumentNullException(nameof(provider));

        lock (_lock)
        {
            if (!_providers.Contains(provider))
            {
                _providers.Add(provider);
                _logger.LogInformation("Registered data provider: {ProviderId} ({ProviderName})", 
                    provider.ProviderId, provider.ProviderName);

                // 清除缓存的能力映射
                _capabilityProviders.Clear();
            }
        }
    }

    /// <summary>
    /// 获取所有已注册的提供者
    /// </summary>
    public IEnumerable<IDataProvider> GetAllProviders()
    {
        lock (_lock)
        {
            return _providers.ToList();
        }
    }
}
