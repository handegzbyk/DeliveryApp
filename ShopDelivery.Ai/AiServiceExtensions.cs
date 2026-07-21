using Azure;
using Azure.AI.DocumentIntelligence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ShopDelivery.Ai;

public static class AiServiceExtensions
{
    public static IServiceCollection AddReceiptScanning(this IServiceCollection services, IConfiguration config)
    {
        var endpoint = config["DocumentIntelligence:Endpoint"];
        var key = config["DocumentIntelligence:Key"];

        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(key))
        {
            // Not configured (e.g. fresh Codespace) — leave both services unregistered so
            // endpoints can inject them as nullable (T?) and return a 503 instead of crashing.
            return services;
        }

        services.AddSingleton(new DocumentIntelligenceClient(new Uri(endpoint), new AzureKeyCredential(key)));
        services.AddSingleton<ReceiptExtractor>();
        services.AddSingleton<ProductTextExtractor>();
        return services;
    }
}
