FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY Microservices/Catalog/Catalog.csproj Microservices/Catalog/
COPY Microservices/Contracts/Contracts.csproj Microservices/Contracts/
COPY global.json ./
RUN dotnet restore Microservices/Catalog/Catalog.csproj

COPY Microservices/Catalog/. Microservices/Catalog/
COPY Microservices/Contracts/. Microservices/Contracts/
RUN dotnet publish Microservices/Catalog/Catalog.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /app/publish .
EXPOSE 8094
ENTRYPOINT ["dotnet", "Catalog.dll"]
