using AIStock.Core.Models;

namespace AIStock.Core.Interfaces;

/// <summary>
/// 小作文分析接口
/// </summary>
public interface IEssayAnalyzer
{
    /// <summary>
    /// 分析文本内容
    /// </summary>
    Task<EssayAnalysisResult> AnalyzeTextAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// 分析图片内容（多模态识别）
    /// </summary>
    Task<EssayAnalysisResult> AnalyzeImageAsync(string imageUrl, CancellationToken cancellationToken = default);

    /// <summary>
    /// 分析多张图片内容（多模态识别），可附带正文文字一并分析
    /// </summary>
    Task<EssayAnalysisResult> AnalyzeImagesAsync(IReadOnlyList<string> imageUrls, string? accompanyingText = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 分析音频内容（ASR）
    /// </summary>
    Task<EssayAnalysisResult> AnalyzeAudioAsync(string audioUrl, CancellationToken cancellationToken = default);
}

/// <summary>
/// 知识星球抓取接口
/// </summary>
public interface IKnowledgeStarCollector
{
    /// <summary>
    /// 采集器ID
    /// </summary>
    string CollectorId { get; }

    /// <summary>
    /// 获取最新内容
    /// </summary>
    Task<List<KnowledgeStarContent>> GetLatestContentAsync(int count = 20, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取指定星球的内容
    /// </summary>
    Task<List<KnowledgeStarContent>> GetGroupContentAsync(string groupId, int count = 20, CancellationToken cancellationToken = default);

    /// <summary>
    /// 搜索内容
    /// </summary>
    Task<List<KnowledgeStarContent>> SearchContentAsync(string keyword, int count = 20, CancellationToken cancellationToken = default);
}
