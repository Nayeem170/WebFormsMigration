FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY Microservices/Frontend/CoreWebForms.csproj Microservices/Frontend/
COPY Microservices/Frontend/nuget.config Microservices/Frontend/
COPY Microservices/Contracts/Contracts.csproj Microservices/Contracts/
COPY global.json ./
RUN dotnet restore Microservices/Frontend/CoreWebForms.csproj

COPY Microservices/Frontend/. Microservices/Frontend/
COPY Microservices/Contracts/. Microservices/Contracts/
RUN dotnet publish Microservices/Frontend/CoreWebForms.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /app/publish .
EXPOSE 8081
ENTRYPOINT ["dotnet", "CoreWebForms.dll"]
