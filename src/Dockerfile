ARG VERSION=1.0.0.0
FROM mcr.microsoft.com/dotnet/sdk:5.0-alpine AS build
ARG VERSION
WORKDIR /app

# copy csproj and restore as distinct layers
COPY nuget.config .
COPY *.sln .
COPY HomeAutio.Mqtt.Apex/*.csproj ./HomeAutio.Mqtt.Apex/
RUN dotnet restore

# copy and build everything else
COPY HomeAutio.Mqtt.Apex/. ./HomeAutio.Mqtt.Apex/
RUN dotnet build --configuration Release /p:Version=$VERSION

# publish
FROM build AS publish
WORKDIR /app/HomeAutio.Mqtt.Apex
RUN dotnet publish -o out

# build runtime image
FROM mcr.microsoft.com/dotnet/runtime:5.0-alpine AS runtime
WORKDIR /app
COPY --from=publish /app/HomeAutio.Mqtt.Apex/out ./

ENV ENVIRONMENT Production

VOLUME ["/app/appsettings.Production.json", "/app/logs"]
ENTRYPOINT ["dotnet", "HomeAutio.Mqtt.Apex.dll"]