FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY Microservices/Gateway/Gateway.csproj Microservices/Gateway/
COPY Microservices/Contracts/Contracts.csproj Microservices/Contracts/
COPY global.json ./
RUN dotnet restore Microservices/Gateway/Gateway.csproj

COPY Microservices/Gateway/. Microservices/Gateway/
COPY Microservices/Contracts/. Microservices/Contracts/
RUN dotnet publish Microservices/Gateway/Gateway.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Gateway.dll"]
