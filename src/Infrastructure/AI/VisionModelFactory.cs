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

    public IVisionModel Create(string providerCode)
    {
        return providerCode switch
        {
            GlmCode => CreateGlm(),
            _ => CreateGlm()
        };
    }

    private GlmVisionProvider CreateGlm()
    {
        var opts = new GlmVisionOptions
        {
            ApiKey = settings.GetSecretAsync(SettingsKeys.GlmApiKey).GetAwaiter().GetResult(),
            Model = settings.GetAsync(SettingsKeys.GlmModel).GetAwaiter().GetResult() ?? "glm-4.6v"
        };
        var client = httpFactory.CreateClient("glm");
        return new GlmVisionProvider(client, opts, loggerFactory.CreateLogger<GlmVisionProvider>());
    }
}
