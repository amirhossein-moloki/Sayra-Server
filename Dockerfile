# Base stage for running the application
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
USER app
WORKDIR /app
EXPOSE 5000

# SDK stage for restoring and building
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Copy projects to restore dependencies
COPY ["Sayra.Backend.slnx", "./"]
COPY ["src/Sayra.Backend.Shared/Sayra.Backend.Shared.csproj", "src/Sayra.Backend.Shared/"]
COPY ["src/Sayra.Backend.Domain/Sayra.Backend.Domain.csproj", "src/Sayra.Backend.Domain/"]
COPY ["src/Sayra.Backend.Application/Sayra.Backend.Application.csproj", "src/Sayra.Backend.Application/"]
COPY ["src/Sayra.Backend.Infrastructure/Sayra.Backend.Infrastructure.csproj", "src/Sayra.Backend.Infrastructure/"]
COPY ["src/Sayra.Backend.Api/Sayra.Backend.Api.csproj", "src/Sayra.Backend.Api/"]

# Copy all Module csproj files
COPY ["src/Sayra.Backend.Modules/Workstations/Workstations.csproj", "src/Sayra.Backend.Modules/Workstations/"]
COPY ["src/Sayra.Backend.Modules/Sessions/Sessions.csproj", "src/Sayra.Backend.Modules/Sessions/"]
COPY ["src/Sayra.Backend.Modules/Authentication/Authentication.csproj", "src/Sayra.Backend.Modules/Authentication/"]
COPY ["src/Sayra.Backend.Modules/Configuration/Configuration.csproj", "src/Sayra.Backend.Modules/Configuration/"]
COPY ["src/Sayra.Backend.Modules/Updates/Updates.csproj", "src/Sayra.Backend.Modules/Updates/"]
COPY ["src/Sayra.Backend.Modules/Telemetry/Telemetry.csproj", "src/Sayra.Backend.Modules/Telemetry/"]
COPY ["src/Sayra.Backend.Modules/Events/Events.csproj", "src/Sayra.Backend.Modules/Events/"]
COPY ["src/Sayra.Backend.Modules/Commands/Commands.csproj", "src/Sayra.Backend.Modules/Commands/"]
COPY ["src/Sayra.Backend.Modules/Fleet/Fleet.csproj", "src/Sayra.Backend.Modules/Fleet/"]

RUN dotnet restore "Sayra.Backend.slnx"

# Copy full source
COPY . .
RUN dotnet build "Sayra.Backend.slnx" -c $BUILD_CONFIGURATION -o /app/build

# Publish stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "Sayra.Backend.slnx" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# Final production stage
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENV ASPNETCORE_URLS=http://*:5000
ENTRYPOINT ["dotnet", "Sayra.Backend.Api.dll"]
