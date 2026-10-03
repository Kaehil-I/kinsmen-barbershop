# Build context is the repository root: docker build -f src/Kinsmen.Web/Dockerfile .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json ./
# Restore separately so the package layer is cached until dependencies change.
COPY src/Kinsmen.Web/Kinsmen.Web.csproj src/Kinsmen.Web/packages.lock.json src/Kinsmen.Web/
RUN dotnet restore src/Kinsmen.Web/Kinsmen.Web.csproj --locked-mode
COPY src/Kinsmen.Web/ src/Kinsmen.Web/
RUN dotnet publish src/Kinsmen.Web/Kinsmen.Web.csproj -c Release --no-restore -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Kinsmen.Web.dll"]
