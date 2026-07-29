# ── Build Stage ──────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy solution and project files first for layer caching
COPY BSourceNotifier.sln ./
COPY src/BSourceNotifier.API/BSourceNotifier.API.csproj                     src/BSourceNotifier.API/
COPY src/BSourceNotifier.Application/BSourceNotifier.Application.csproj     src/BSourceNotifier.Application/
COPY src/BSourceNotifier.Contracts/BSourceNotifier.Contracts.csproj         src/BSourceNotifier.Contracts/
COPY src/BSourceNotifier.Domain/BSourceNotifier.Domain.csproj               src/BSourceNotifier.Domain/
COPY src/BSourceNotifier.Infrastructure/BSourceNotifier.Infrastructure.csproj src/BSourceNotifier.Infrastructure/

RUN dotnet restore src/BSourceNotifier.API/BSourceNotifier.API.csproj

# Copy everything else and publish
COPY . .
RUN dotnet publish src/BSourceNotifier.API/BSourceNotifier.API.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# ── Runtime Stage ───────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:5000
EXPOSE 5000

ENTRYPOINT ["dotnet", "BSourceNotifier.API.dll"]
