#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["AMMStakingBot/AMMStakingBot.csproj", "AMMStakingBot/"]
RUN dotnet restore "AMMStakingBot/AMMStakingBot.csproj"
COPY . .
WORKDIR "/src/AMMStakingBot"
RUN dotnet build "AMMStakingBot.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "AMMStakingBot.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "AMMStakingBot.dll"]