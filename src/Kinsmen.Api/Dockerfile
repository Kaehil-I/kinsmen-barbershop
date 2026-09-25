# Build context is the repository root: docker build -f src/Kinsmen.Api/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json ./
# Restore separately so the package layer is cached until dependencies change.
COPY src/Kinsmen.Api/Kinsmen.Api.csproj src/Kinsmen.Api/packages.lock.json src/Kinsmen.Api/
RUN dotnet restore src/Kinsmen.Api/Kinsmen.Api.csproj --locked-mode
COPY src/Kinsmen.Api/ src/Kinsmen.Api/
RUN dotnet publish src/Kinsmen.Api/Kinsmen.Api.csproj -c Release --no-restore -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Kinsmen.Api.dll"]
