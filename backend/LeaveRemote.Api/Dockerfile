# Backend-ul .NET (Web API). Contextul de build este folderul „backend” (vezi docker-compose.yml).

# 1. Compilare și publicare
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
# Întâi doar proiectul, ca pachetele NuGet să fie păstrate în cache între build-uri.
COPY LeaveRemote.Api/LeaveRemote.Api.csproj LeaveRemote.Api/
RUN dotnet restore LeaveRemote.Api/LeaveRemote.Api.csproj
COPY LeaveRemote.Api/ LeaveRemote.Api/
RUN dotnet publish LeaveRemote.Api/LeaveRemote.Api.csproj -c Release -o /app --no-restore

# 2. Imaginea finală, doar cu runtime-ul ASP.NET Core
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
# Imaginea ASP.NET ascultă implicit pe portul 8080 (HTTP_PORTS=8080).
EXPOSE 8080
# Rulează cu utilizatorul neprivilegiat furnizat de imagine, nu ca root.
USER $APP_UID
ENTRYPOINT ["dotnet", "LeaveRemote.Api.dll"]
