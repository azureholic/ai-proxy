using Azure.Core;
using System.IdentityModel.Tokens.Jwt;

namespace AzureAI.Proxy.OpenAIHandlers;

public static class OpenAIAccessToken
{
    private const string OPENAI_SCOPE = "https://cognitiveservices.azure.com/.default";

    public async static Task<string> GetAccessTokenAsync(TokenCredential managedIdenitityCredential, CancellationToken cancellationToken)
    {
        if (managedIdenitityCredential == null)
        {
            throw new ArgumentNullException(nameof(managedIdenitityCredential));
        }

        var accessToken = await managedIdenitityCredential.GetTokenAsync(
            new TokenRequestContext(
                new[] { OPENAI_SCOPE }
                ),
            cancellationToken
            );

        return accessToken.Token;
    }

    public static bool IsTokenExpired(string accessToken)
    {
        if (string.IsNullOrEmpty(accessToken))
        {
            // If token is null or empty, consider it expired
            return true;
        }

        try
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            
            if (!tokenHandler.CanReadToken(accessToken))
            {
                // If token is invalid and can't be read, consider it expired
                return true;
            }
            
            var jwtToken = tokenHandler.ReadToken(accessToken);
            if (jwtToken == null)
            {
                return true;
            }
            
            var expDate = jwtToken.ValidTo;
            return expDate < DateTime.UtcNow;
        }
        catch
        {
            // If any error occurs during token validation, consider it expired
            return true;
        }
    }
}
