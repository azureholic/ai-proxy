#BUILD
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY AzureAI.Proxy/ ./AzureAI.Proxy
RUN dotnet restore AzureAI.Proxy/AzureAI.Proxy.csproj
WORKDIR /src/AzureAI.Proxy
RUN dotnet build AzureAI.Proxy.csproj -c $BUILD_CONFIGURATION -o /app/build

#PUBLISH
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish AzureAI.Proxy.csproj -c $BUILD_CONFIGURATION -o /app/publish 

#FINAL
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
EXPOSE 8080
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet","./AzureAI.Proxy.dll"]
