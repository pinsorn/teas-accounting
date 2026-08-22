# syntax=docker/dockerfile:1.7
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore deps
COPY ["Directory.Packages.props", "Directory.Build.props", "global.json", "./"]
COPY ["src/Accounting.Domain/Accounting.Domain.csproj", "src/Accounting.Domain/"]
COPY ["src/Accounting.Application/Accounting.Application.csproj", "src/Accounting.Application/"]
COPY ["src/Accounting.Infrastructure/Accounting.Infrastructure.csproj", "src/Accounting.Infrastructure/"]
COPY ["src/Accounting.Api/Accounting.Api.csproj", "src/Accounting.Api/"]
RUN dotnet restore "src/Accounting.Api/Accounting.Api.csproj"

# Build
COPY . .
# Docker context has no .git, so MinVer can't read the tag — override from the
# checked-in VERSION file. Bump backend/VERSION when tagging a release, or prod
# keeps showing the previous version. tr strips CRLF/whitespace so a Windows
# checkout can't feed MinVer an invalid version and fail the Coolify build.
RUN dotnet publish "src/Accounting.Api/Accounting.Api.csproj" -c Release -o /app/publish --no-restore \
    -p:MinVerVersionOverride=$(tr -d ' \r\n' < VERSION)

# Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_RUNNING_IN_CONTAINER=true \
    TZ=Asia/Bangkok
RUN apt-get update && apt-get install -y --no-install-recommends \
    tzdata fonts-thai-tlwg curl \
    && rm -rf /var/lib/apt/lists/*
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Accounting.Api.dll"]
