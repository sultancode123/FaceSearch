FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 10000

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY . .
WORKDIR "/src/FaceSearch"
RUN dotnet restore "FaceSearch.csproj"
RUN dotnet build "FaceSearch.csproj" -c Release -o /app/build

FROM build AS publish
WORKDIR "/src/FaceSearch"
RUN dotnet publish "FaceSearch.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "FaceSearch.dll"]