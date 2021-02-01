FROM mcr.microsoft.com/dotnet/core/aspnet:3.1-buster-slim AS base
WORKDIR /app
EXPOSE 80

FROM mcr.microsoft.com/dotnet/core/sdk:3.1-buster AS build
WORKDIR /src
COPY ["Journal-Limpet/Journal-Limpet.csproj", "Journal-Limpet/"]
COPY ["Journal-Limpet.Shared/Journal-Limpet.Shared.csproj", "Journal-Limpet.Shared/"]
RUN dotnet restore "Journal-Limpet/Journal-Limpet.csproj"
COPY . .
WORKDIR "/src/Journal-Limpet"
RUN dotnet build "Journal-Limpet.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Journal-Limpet.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Journal-Limpet.dll"]