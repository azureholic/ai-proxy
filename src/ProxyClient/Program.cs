using Azure.AI.OpenAI;
using Microsoft.Extensions.Configuration;
using OpenAI.Chat;
using System.ClientModel;

IConfigurationRoot config = new ConfigurationBuilder()
    .AddUserSecrets<Program>()
    .Build();

var proxyEndpoint = config["ProxyEndPoint"];
var apiKey = config["APIKey"];

AzureOpenAIClient azureClient = new(
    new Uri(proxyEndpoint),
    new ApiKeyCredential(apiKey));



var deploymentName = "gpt-35-turbo";

var chatMessages = new List<ChatMessage>();
var systemChatMessage = new SystemChatMessage("You are a helpful AI Assistant");
var userChatMessage = new UserChatMessage("When was Microsoft Founded and what info can you give me on the founders in a maximum of 100 words");

chatMessages.Add(systemChatMessage);
chatMessages.Add(userChatMessage);


Console.WriteLine($"Using endpoint: {proxyEndpoint}");

ChatClient chatClient = azureClient.GetChatClient(deploymentName);

//run the loop to hit rate-limiter
//for (int i = 0; i < 7; i++)
//{

    //Console.WriteLine("Get answer to question: " + userChatMessage.Content);
    //ChatCompletion completion = chatClient.CompleteChat(chatMessages);
    //Console.WriteLine("Get Chat Completion Result");
    //Console.WriteLine($"{completion.Role}: {completion.Content[0].Text}");
//}
//end loop



Console.WriteLine("Get StreamingChat Completion Result");
CollectionResult<StreamingChatCompletionUpdate> completionUpdates = chatClient.CompleteChatStreaming(chatMessages);
foreach (StreamingChatCompletionUpdate completionUpdate in completionUpdates)
{
    foreach (ChatMessageContentPart contentPart in completionUpdate.ContentUpdate)
    {
        Console.Write(contentPart.Text);
    }
}


Console.WriteLine();



//embedding
//string embeddingDeploymentName = "text-embedding-ada-002";
//List<string> embeddingText = new List<string>();
//embeddingText.Add("When was Microsoft Founded?");

//var embeddingsOptions = new EmbeddingsOptions(embeddingDeploymentName, embeddingText);
//var embeddings = await client.GetEmbeddingsAsync(embeddingsOptions);
//Console.WriteLine("Get Embeddings Result");
//foreach (float item in embeddings.Value.Data[0].Embedding.ToArray())
//{
//    Console.WriteLine(item);
//}





Console.ReadLine();