FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

ARG PROJECT
COPY . .
RUN dotnet restore "src/${PROJECT}/${PROJECT}.csproj"
RUN dotnet publish "src/${PROJECT}/${PROJECT}.csproj" -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

ARG PROJECT
ENV PROJECT=${PROJECT}
ENV ASPNETCORE_URLS=http://+:8080

COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["sh", "-c", "dotnet ${PROJECT}.dll"]
