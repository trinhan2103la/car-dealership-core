# Ngày 15: image Core_API (ABP layered, .NET 10). Multi-stage, chạy user non-root.
# Build từ ROOT repo car-dealership-core:
#   docker build -f Dockerfile -t cdp-core-api .

# ---- Stage build ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy toàn bộ solution rồi restore+publish. (Build context đã lọc bin/obj qua .dockerignore.)
COPY . .
RUN dotnet restore src/CarDealership.CoreService.HttpApi.Host/CarDealership.CoreService.HttpApi.Host.csproj
RUN dotnet publish src/CarDealership.CoreService.HttpApi.Host/CarDealership.CoreService.HttpApi.Host.csproj \
    -c Release -o /app --no-restore

# ---- Stage runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# User non-root (image aspnet đã có sẵn user "app" uid 1654).
USER app

COPY --from=build /app .

# Lắng nghe HTTP 8080 trong container (không HTTPS nội bộ; TLS do reverse proxy/hosting lo).
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "CarDealership.CoreService.HttpApi.Host.dll"]
