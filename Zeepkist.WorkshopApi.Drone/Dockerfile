﻿FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY "Zeepkist.WorkshopApi.Drone/Zeepkist.WorkshopApi.Drone.csproj" .
RUN dotnet restore
COPY . .
WORKDIR /src
RUN dotnet build -c Release -o /app/build

FROM build AS publish
WORKDIR "/src"
RUN dotnet publish -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "TNRD.Zeepkist.WorkshopApi.Drone.dll"]
