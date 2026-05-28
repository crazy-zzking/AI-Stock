using AIStock.Core.Enums;

namespace AIStock.Core.Interfaces;

/// <summary>
/// 数据源解析器接口
/// </summary>
public interface IDataProviderResolver
{
    /// <summary>
    /// 获取指定能力的数据提供者
    /// </summary>
    /// <param name="capability">数据能力</param>
    /// <returns>数据提供者列表（按优先级排序）</returns>
    IEnumerable<IDataProvider> GetProviders(DataCapability capability);

    /// <summary>
    /// 获取指定能力的首选数据提供者
    /// </summary>
    /// <param name="capability">数据能力</param>
    /// <returns>首选数据提供者</returns>
    IDataProvider? GetPrimaryProvider(DataCapability capability);

    /// <summary>
    /// 注册数据提供者
    /// </summary>
    /// <param name="provider">数据提供者</param>
    void RegisterProvider(IDataProvider provider);

    /// <summary>
    /// 获取所有已注册的提供者
    /// </summary>
    /// <returns>所有数据提供者</returns>
    IEnumerable<IDataProvider> GetAllProviders();

    /// <summary>
    /// 获取默认数据提供者
    /// </summary>
    /// <returns>默认数据提供者</returns>
    IDataProvider GetDefaultProvider();
}
