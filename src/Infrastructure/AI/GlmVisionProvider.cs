using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using KnowledgeWeakness.Core.AI;
using Microsoft.Extensions.Logging;

namespace KnowledgeWeakness.Infrastructure.AI;

public class GlmVisionOptions
{
    public string BaseUrl { get; set; } = "https://open.bigmodel.cn/api/paas/v4";
    public string Model { get; set; } = "glm-4.6v";
    public string? ApiKey { get; set; }
    public double Temperature { get; set; } = 0.1;
    public int TimeoutSeconds { get; set; } = 240;
}

public class GlmVisionProvider(
    HttpClient httpClient,
    GlmVisionOptions options,
    ILogger<GlmVisionProvider> logger) : IVisionModel
{
    public string ProviderCode => "zhipu-glm4v";
    public string DisplayName => "智谱 GLM-4V";

    public async Task<PaperExtraction> ExtractPaperAsync(
        IReadOnlyList<byte[]> pageImages,
        SubjectContext subject,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
            throw new InvalidOperationException("GLM API Key 未配置，请在设置页填写。");

        var content = new List<object>
        {
            new { type = "text", text = VisionPromptBuilder.BuildUserText(subject) }
        };
        foreach (var img in pageImages)
        {
            var dataUrl = "data:image/jpeg;base64," + Convert.ToBase64String(img);
            content.Add(new { type = "image_url", image_url = new { url = dataUrl } });
        }

        var body = new
        {
            model = options.Model,
            temperature = options.Temperature,
            messages = new object[]
            {
                new { role = "system", content = VisionPromptBuilder.SystemPrompt },
                new { role = "user", content }
            }
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{options.BaseUrl.TrimEnd('/')}/chat/completions");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        req.Content = JsonContent.Create(body);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));

        logger.LogInformation("Calling GLM vision model {Model}", options.Model);
        using var resp = await httpClient.SendAsync(req, cts.Token);
        var respText = await resp.Content.ReadAsStringAsync(cts.Token);

        if (!resp.IsSuccessStatusCode)
        {
            logger.LogError("GLM API failed {Code}: {Body}", resp.StatusCode, respText);
            throw new HttpRequestException($"GLM API 调用失败 ({(int)resp.StatusCode}): {respText}");
        }

        var messageContent = ExtractAssistantContent(respText);
        try
        {
            return VisionJsonParser.Parse(messageContent);
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Failed to parse GLM JSON: {Content}", messageContent);
            throw new InvalidOperationException("模型返回非法 JSON，请重试或更换 Provider。", ex);
        }
    }

    private static string ExtractAssistantContent(string respJson)
    {
        using var doc = JsonDocument.Parse(respJson);
        var choices = doc.RootElement.GetProperty("choices");
        var first = choices[0];
        var msg = first.GetProperty("message");
        return msg.GetProperty("content").GetString() ?? "";
    }
}
