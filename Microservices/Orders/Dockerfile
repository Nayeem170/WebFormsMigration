FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY Microservices/Orders/Orders.csproj Microservices/Orders/
COPY Microservices/Contracts/Contracts.csproj Microservices/Contracts/
COPY global.json ./
RUN dotnet restore Microservices/Orders/Orders.csproj

COPY Microservices/Orders/. Microservices/Orders/
COPY Microservices/Contracts/. Microservices/Contracts/
RUN dotnet publish Microservices/Orders/Orders.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /app/publish .
EXPOSE 8095
ENTRYPOINT ["dotnet", "Orders.dll"]
