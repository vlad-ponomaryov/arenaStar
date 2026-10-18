# Arena dedicated server image (Server-Spec §10.4). Build context: the repository root (sources and the content pack live in Assets/).
#   docker compose -f Server/docker-compose.yml up -d --build
# or
#   docker build -f Server/Dockerfile -t arena-server .
#   docker run -d -p 7777:7777/udp --stop-timeout 190 -v arena-logs:/app/logs arena-server
# The base images (mcr.microsoft.com/dotnet/sdk and runtime 10.0, amd64 and arm64) are downloaded by Docker - a person starts it.
# Server/Dockerfile.dockerignore keeps Library/, Temp/, Builds/ and Tools/out/ out of the context (BuildKit).

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Server/ Server/
COPY Assets/_Project/Scripts/Core/ Assets/_Project/Scripts/Core/
COPY Assets/_Project/Scripts/Bots/ Assets/_Project/Scripts/Bots/
COPY Assets/_Project/Scripts/Net/ Assets/_Project/Scripts/Net/
COPY Assets/_Project/Data/Net/ArenaContent.bytes Assets/_Project/Data/Net/ArenaContent.bytes
RUN dotnet publish Server/Arena.Server/Arena.Server.csproj -c Release -o /app --nologo

FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
COPY --from=build /app ./
# LAN discovery makes no sense inside a container network; the config inside the image can be replaced by a volume.
RUN sed -i 's/"LanDiscovery": true/"LanDiscovery": false/' config/server.json \
    && sed -i 's/"MetricsBindAddress": "127.0.0.1"/"MetricsBindAddress": "0.0.0.0"/' config/server.json \
    && mkdir -p /app/logs && chown -R app:app /app/logs
USER app
EXPOSE 7777/udp
STOPSIGNAL SIGTERM
ENTRYPOINT ["dotnet", "Arena.Server.dll"]
