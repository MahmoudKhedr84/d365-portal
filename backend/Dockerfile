# ---- build ----
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props D365Portal.sln ./
COPY src/D365Portal.Core/D365Portal.Core.csproj src/D365Portal.Core/
COPY src/D365Portal.Infrastructure/D365Portal.Infrastructure.csproj src/D365Portal.Infrastructure/
COPY src/D365Portal.Api/D365Portal.Api.csproj src/D365Portal.Api/
RUN dotnet restore src/D365Portal.Api/D365Portal.Api.csproj
COPY src/ src/
RUN dotnet publish src/D365Portal.Api/D365Portal.Api.csproj -c Release -o /app --no-restore /p:UseAppHost=false

# ---- runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "D365Portal.Api.dll"]
