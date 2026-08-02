# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY global.json ./
COPY Directory.Build.props Directory.Packages.props ./
COPY src/Trading.Monitor.Domain/Trading.Monitor.Domain.csproj src/Trading.Monitor.Domain/
COPY src/Trading.Monitor.Domain/packages.lock.json src/Trading.Monitor.Domain/
COPY src/Trading.Monitor.Application/Trading.Monitor.Application.csproj src/Trading.Monitor.Application/
COPY src/Trading.Monitor.Application/packages.lock.json src/Trading.Monitor.Application/
COPY src/Trading.Monitor.Infrastructure/Trading.Monitor.Infrastructure.csproj src/Trading.Monitor.Infrastructure/
COPY src/Trading.Monitor.Infrastructure/packages.lock.json src/Trading.Monitor.Infrastructure/
COPY src/Trading.Monitor.Worker/Trading.Monitor.Worker.csproj src/Trading.Monitor.Worker/
COPY src/Trading.Monitor.Worker/packages.lock.json src/Trading.Monitor.Worker/

RUN --mount=type=secret,id=local_ca,required=false \
    if [ -s /run/secrets/local_ca ]; then \
      cp /run/secrets/local_ca /usr/local/share/ca-certificates/local-development-ca.crt; \
      update-ca-certificates; \
    fi

RUN dotnet restore src/Trading.Monitor.Worker/Trading.Monitor.Worker.csproj --locked-mode

COPY . .
RUN dotnet publish src/Trading.Monitor.Worker/Trading.Monitor.Worker.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

ENV DOTNET_RUNNING_IN_CONTAINER=true

ENV ASPNETCORE_URLS=http://+:8080

RUN --mount=type=secret,id=local_ca,required=false \
    if [ -s /run/secrets/local_ca ]; then \
      cp /run/secrets/local_ca /usr/local/share/ca-certificates/local-development-ca.crt; \
      update-ca-certificates; \
    fi

RUN mkdir -p /data /app/logs && \
    chown -R app:app /data /app

COPY --from=build --chown=app:app /app/publish .

EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "Trading.Monitor.Worker.dll"]
