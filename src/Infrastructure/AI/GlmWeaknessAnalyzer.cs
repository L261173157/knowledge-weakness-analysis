using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using KnowledgeWeakness.Core.AI;
using KnowledgeWeakness.Core.Abstractions;
using KnowledgeWeakness.Core.Analysis;
using Microsoft.Extensions.Logging;

namespace KnowledgeWeakness.Infrastructure.AI;

public class GlmWeaknessAnalyzer(
    HttpClient httpClient,
    ISettingsRepository settings,
    ILogger<GlmWeaknessAnalyzer> logger) : IWeaknessAnalyzer
{
    public async Task<AiWeaknessAnalysisResult> AnalyzeAsync(
        AiWeaknessAnalysisRequest request,
        CancellationToken ct = default)
    {
        var options = new GlmVisionOptions
        {
            ApiKey = await settings.GetSecretAsync(SettingsKeys.LanguageGlmApiKey, ct)
                     ?? await settings.GetSecretAsync(SettingsKeys.GlmApiKey, ct),
            Model = await settings.GetAsync(SettingsKeys.LanguageGlmModel, ct)
                    ?? await settings.GetAsync(SettingsKeys.GlmModel, ct)
                    ?? "glm-4.6",
            BaseUrl = await settings.GetAsync(SettingsKeys.LanguageGlmBaseUrl, ct)
                      ?? "https://open.bigmodel.cn/api/paas/v4"
        };

        if (string.IsNullOrWhiteSpace(options.ApiKey))
            throw new InvalidOperationException("语言模型 GLM API Key 未配置，请在设置页填写。");

        if (request.Candidates.Count == 0)
            return new AiWeaknessAnalysisResult("没有可分析的薄弱题。", [], "{}");

        var body = new
        {
            model = options.Model,
            temperature = options.Temperature,
            messages = new object[]
            {
                new { role = "system", content = WeaknessPromptBuilder.SystemPrompt },
                new { role = "user", content = WeaknessPromptBuilder.BuildUserText(request) }
            }
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{options.BaseUrl.TrimEnd('/')}/chat/completions");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        req.Content = JsonContent.Create(body);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));

        logger.LogInformation("Calling GLM weakness analyzer {Model}", options.Model);
        using var resp = await httpClient.SendAsync(req, cts.Token);
        var respText = await resp.Content.ReadAsStringAsync(cts.Token);
        if (!resp.IsSuccessStatusCode)
        {
            logger.LogError("GLM weakness analysis failed {Code}: {Body}", resp.StatusCode, respText);
            throw new HttpRequestException($"GLM 薄弱分析调用失败 ({(int)resp.StatusCode}): {respText}");
        }

        var messageContent = GlmResponseHelper.ExtractAssistantContent(respText);
        if (string.IsNullOrEmpty(messageContent))
        {
            throw new InvalidOperationException(
                "GLM 语言模型未返回内容（可能是限流或内容审核拒绝），请稍后重试。");
        }
        try
        {
            return AiWeaknessJsonParser.Parse(messageContent);
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Failed to parse GLM weakness JSON: {Content}", messageContent);
            throw new InvalidOperationException("模型返回非法薄弱分析 JSON，请重试。", ex);
        }
    }
}
