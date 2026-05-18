# syntax=docker/dockerfile:1.7
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore deps
COPY ["Directory.Packages.props", "Directory.Build.props", "global.json", "./"]
COPY ["src/Accounting.Domain/Accounting.Domain.csproj", "src/Accounting.Domain/"]
COPY ["src/Accounting.Application/Accounting.Application.csproj", "src/Accounting.Application/"]
COPY ["src/Accounting.Infrastructure/Accounting.Infrastructure.csproj", "src/Accounting.Infrastructure/"]
COPY ["src/Accounting.Api/Accounting.Api.csproj", "src/Accounting.Api/"]
COPY ["src/Accounting.Workers/Accounting.Workers.csproj", "src/Accounting.Workers/"]
RUN dotnet restore "src/Accounting.Api/Accounting.Api.csproj"

# Build
COPY . .
RUN dotnet publish "src/Accounting.Api/Accounting.Api.csproj" -c Release -o /app/publish --no-restore

# Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_RUNNING_IN_CONTAINER=true \
    TZ=Asia/Bangkok
RUN apt-get update && apt-get install -y --no-install-recommends \
    tzdata fonts-thai-tlwg \
    && rm -rf /var/lib/apt/lists/*
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Accounting.Api.dll"]
