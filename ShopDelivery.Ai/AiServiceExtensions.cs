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
            // Not configured (e.g. fresh Codespace) — register a stub so the API still boots.
            services.AddSingleton<ReceiptExtractor>(_ =>
                throw new InvalidOperationException("Document Intelligence is not configured."));
            return services;
        }

        services.AddSingleton(new DocumentIntelligenceClient(new Uri(endpoint), new AzureKeyCredential(key)));
        services.AddSingleton<ReceiptExtractor>();
        return services;
    }
}