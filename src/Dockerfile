FROM microsoft/dotnet:2.1-sdk AS build
WORKDIR /app

# copy csproj and restore as distinct layers
COPY nuget.config .
COPY *.sln .
COPY HomeAutio.Mqtt.Apex/*.csproj ./HomeAutio.Mqtt.Apex/
RUN dotnet restore

# copy and build everything else
COPY HomeAutio.Mqtt.Apex/. ./HomeAutio.Mqtt.Apex/
RUN dotnet build -c Release

# copy everything else and build
FROM build AS publish
WORKDIR /app/HomeAutio.Mqtt.Apex
RUN dotnet publish -o out

# build runtime image
FROM microsoft/dotnet:2.1-runtime AS runtime
WORKDIR /app
COPY --from=publish /app/HomeAutio.Mqtt.Apex/out ./

VOLUME ["/app/appsettings.json", "/app/logs"]
ENTRYPOINT ["dotnet", "HomeAutio.Mqtt.Apex.dll"]