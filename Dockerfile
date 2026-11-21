FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
# Keep restore cached until project metadata changes.
COPY *.slnx *.props ./
COPY src/Api/*.csproj src/Api/
COPY src/Application/*.csproj src/Application/
COPY src/Domain/*.csproj src/Domain/
COPY src/Hmi/*.csproj src/Hmi/
COPY src/Infrastructure/*.csproj src/Infrastructure/
COPY src/Protocols/*.csproj src/Protocols/
COPY src/Security/*.csproj src/Security/
COPY src/Simulation/*.csproj src/Simulation/
RUN dotnet restore src/Api/Api.csproj
COPY . .
RUN dotnet publish src/Api/Api.csproj -c Release --no-restore -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0.15
WORKDIR /app
# Writable persistent state for cookie keys and optional OPC UA PKI.
RUN mkdir -p /var/lib/scada && chown "$APP_UID" /var/lib/scada
ENV ASPNETCORE_URLS=http://+:8080 \
    HOME=/var/lib/scada \
    Protocols__OpcUa__PkiDirectory=/var/lib/scada/pki
COPY --from=build /app/publish .
USER $APP_UID
EXPOSE 8080
HEALTHCHECK --interval=10s --timeout=5s --start-period=30s --retries=3 \
    CMD ["dotnet", "Api.dll", "--healthcheck"]
ENTRYPOINT ["dotnet", "Api.dll"]
