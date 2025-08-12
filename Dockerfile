# 1) 建置階段：用 .NET 8 SDK 編譯與發佈
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY *.csproj ./
RUN dotnet restore
COPY . ./
RUN dotnet publish -c Release -o /app/out

# 2) 執行階段：用 .NET 8 ASP.NET Runtime 來跑
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/out ./

# 讓容器在 Render 指定的埠上對外服務（埠號由 Render 的 ${PORT} 提供）
# 我們不在這裡硬寫死埠號，會在 Render 服務的 Environment 變數設 ASPNETCORE_URLS
ENTRYPOINT ["dotnet", "WFH.Api.dll"]
