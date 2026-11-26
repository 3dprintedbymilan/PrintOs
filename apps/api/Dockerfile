FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY nuget.config ./
COPY src/PrintOs.Api/PrintOs.Api.csproj src/PrintOs.Api/
RUN dotnet restore src/PrintOs.Api/PrintOs.Api.csproj
COPY src/ src/
RUN dotnet publish src/PrintOs.Api/PrintOs.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "PrintOs.Api.dll"]
