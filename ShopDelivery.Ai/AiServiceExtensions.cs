using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ShopDelivery.Ai;

public static class AiServiceExtensions
{
    public static IServiceCollection AddAiServices(this IServiceCollection services, IConfiguration config)
    {
        var endpointText = config["AzureOpenAI:Endpoint"];
        var keyText = config["AzureOpenAI:Key"];
        var deployment = config["AzureOpenAI:Deployment"];

        if (!string.IsNullOrWhiteSpace(endpointText) &&
            !string.IsNullOrWhiteSpace(keyText) &&
            !string.IsNullOrWhiteSpace(deployment))
        {
            try
            {
                var endpoint = new Uri(endpointText);
                var key = new System.ClientModel.ApiKeyCredential(keyText);

                IChatClient chat = new AzureOpenAIClient(endpoint, key)
                    .GetChatClient(deployment)
                    .AsIChatClient();

                services.AddChatClient(chat);
            }
            catch (Exception)
            {
                // Ignore invalid Azure OpenAI configuration and keep the app running.
            }
        }

        services.AddScoped<ShoppingAssistant>();
        return services;
    }
}

public sealed class ShoppingAssistant(IServiceProvider serviceProvider)
{
    public async Task<string> AskAsync(string message, CancellationToken ct = default)
    {
        var chat = serviceProvider.GetService<IChatClient>();
        if (chat is null)
        {
            return "AI assistance is not configured yet.";
        }

        var response = await chat.GetResponseAsync(
        [
            new(ChatRole.System, "You are a helpful shopping delivery assistant. Help users find products and track orders."),
            new(ChatRole.User, message)
        ], cancellationToken: ct);

        return response.Text;
    }
}