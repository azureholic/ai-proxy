import asyncio
from openai import AsyncAzureOpenAI

# gets API Key from environment variable OPENAI_API_KEY
endpoint = "http://localhost:5227"
api_key = "12345"
client = AsyncAzureOpenAI(azure_endpoint = endpoint,api_key=api_key,api_version="2024-10-21")

async def main() -> None:
    stream = await client.chat.completions.create(
        model="gpt-35-turbo",
        messages = [ {"role": "user", "content": "What is chatgpt?"} ],
        stream=True
        
    )

    #async for data in stream:
    #    print(data.model_dump_json())
    #    print("test")

    async for choices in stream:
        print(choices.model_dump_json(indent=2))    
        print()

asyncio.run(main())