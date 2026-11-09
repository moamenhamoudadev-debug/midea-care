# Stage 1: Build & Publish
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy solution and project files for caching layer
COPY ["MediCare.sln", "./"]
COPY ["src/MediCare.Data/MediCare.Data.csproj", "src/MediCare.Data/"]
COPY ["src/MediCare.Services/MediCare.Services.csproj", "src/MediCare.Services/"]
COPY ["src/MediCare.Web/MediCare.Web.csproj", "src/MediCare.Web/"]
COPY ["tests/MediCare.Tests/MediCare.Tests.csproj", "tests/MediCare.Tests/"]

RUN dotnet restore MediCare.sln

# Copy full source and publish
COPY . .
WORKDIR /src/src/MediCare.Web
RUN dotnet publish "MediCare.Web.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Stage 2: Production Runtime
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "MediCare.Web.dll"]
