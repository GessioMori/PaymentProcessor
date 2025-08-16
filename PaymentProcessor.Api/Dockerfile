# Base runtime
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080

# Base build com dependências de sistema (cacheável)
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build-base
RUN apt-get update && apt-get install -y clang zlib1g-dev && rm -rf /var/lib/apt/lists/*
WORKDIR /src

# Copiamos só o csproj e restauramos para cache de pacotes
COPY ["PaymentProcessor.Api/PaymentProcessor.Api.csproj", "PaymentProcessor.Api/"]
RUN dotnet restore "PaymentProcessor.Api/PaymentProcessor.Api.csproj"

# Copiamos todo o código
COPY . .
WORKDIR "/src/PaymentProcessor.Api"

# Publicação AOT
RUN dotnet publish -c Release -o /app/publish /p:PublishAot=true /p:UseAppHost=true

# Imagem final enxuta
FROM base AS final
WORKDIR /app
COPY --from=build-base /app/publish .
ENTRYPOINT ["./PaymentProcessor.Api"]