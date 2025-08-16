# Base runtime
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app

# Base build com dependências
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
RUN apt-get update && apt-get install -y clang zlib1g-dev && rm -rf /var/lib/apt/lists/*
WORKDIR /src

# Copiamos apenas o csproj para cache
COPY ["PaymentProcessor.Worker/PaymentProcessor.Worker.csproj", "PaymentProcessor.Worker/"]
RUN dotnet restore "PaymentProcessor.Worker/PaymentProcessor.Worker.csproj"

# Copiamos todo o código
COPY . .
WORKDIR "/src/PaymentProcessor.Worker"

# Publicação AOT
RUN dotnet publish -c Release -o /app/publish /p:PublishAot=true /p:UseAppHost=true

# Imagem final enxuta
FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["./PaymentProcessor.Worker"]