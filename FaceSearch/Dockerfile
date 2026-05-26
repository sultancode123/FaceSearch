FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 10000

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["FaceSearch/IFNRCONFaceSearch.csproj", "FaceSearch/"]
RUN dotnet restore "FaceSearch/IFNRCONFaceSearch.csproj"
COPY . .
WORKDIR "/src/FaceSearch"
RUN dotnet build "IFNRCONFaceSearch.csproj" -c Release -o /app/build

FROM build AS publish
WORKDIR "/src/FaceSearch"
RUN dotnet publish "IFNRCONFaceSearch.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "IFNRCONFaceSearch.dll"]