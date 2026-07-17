using KnowledgeWeakness.Core.AI;
using KnowledgeWeakness.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace KnowledgeWeakness.Infrastructure.AI;

public class VisionModelFactory(
    IHttpClientFactory httpFactory,
    ISettingsRepository settings,
    ILoggerFactory loggerFactory) : IVisionModelFactory
{
    public const string GlmCode = "zhipu-glm4v";

    public IReadOnlyList<string> AvailableProviders { get; } = new[] { GlmCode };

    public async Task<IVisionModel> CreateAsync(string providerCode, CancellationToken ct = default)
    {
        // All branches currently build a GLM provider, but keep the switch so the
        // providerCode is honored when more providers are added.
        return providerCode switch
        {
            GlmCode => await CreateGlmAsync(ct),
            _ => await CreateGlmAsync(ct)
        };
    }

    private async Task<GlmVisionProvider> CreateGlmAsync(CancellationToken ct)
    {
        // Fully async — no .GetAwaiter().GetResult(). The previous sync version
        // risked a UI-thread deadlock because DbContextFactory continuations need
        // the same thread that GetResult blocks.
        var opts = new GlmVisionOptions
        {
            ApiKey = await settings.GetSecretAsync(SettingsKeys.VisionGlmApiKey, ct)
                     ?? await settings.GetSecretAsync(SettingsKeys.GlmApiKey, ct),
            Model = await settings.GetAsync(SettingsKeys.VisionGlmModel, ct)
                    ?? await settings.GetAsync(SettingsKeys.GlmModel, ct)
                    ?? "glm-4.6v",
            BaseUrl = await settings.GetAsync(SettingsKeys.VisionGlmBaseUrl, ct)
                      ?? "https://open.bigmodel.cn/api/paas/v4"
        };
        var client = httpFactory.CreateClient("glm");
        return new GlmVisionProvider(client, opts, loggerFactory.CreateLogger<GlmVisionProvider>());
    }
}
