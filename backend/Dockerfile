# ---- build + test ----
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props D365Portal.sln ./
COPY src/D365Portal.Core/D365Portal.Core.csproj src/D365Portal.Core/
COPY src/D365Portal.Infrastructure/D365Portal.Infrastructure.csproj src/D365Portal.Infrastructure/
COPY src/D365Portal.Api/D365Portal.Api.csproj src/D365Portal.Api/
COPY tests/D365Portal.Tests/D365Portal.Tests.csproj tests/D365Portal.Tests/
RUN dotnet restore D365Portal.sln
COPY src/ src/
COPY tests/ tests/
# The deploy fails if any backend test fails (set SKIP_TESTS=true to bypass in an emergency).
ARG SKIP_TESTS=false
RUN if [ "$SKIP_TESTS" != "true" ]; then \
      dotnet test tests/D365Portal.Tests/D365Portal.Tests.csproj -c Release --no-restore --logger "console;verbosity=normal"; \
    fi
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
