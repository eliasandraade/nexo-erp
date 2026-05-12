FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

WORKDIR /src

COPY . .

RUN dotnet publish src/Nexo.Api/Nexo.Api.csproj \
    -c Release \
    -o /app/publish \
    --no-self-contained

FROM mcr.microsoft.com/dotnet/aspnet:8.0

WORKDIR /app

COPY --from=build /app/publish .

# cria usuário/grupo sem privilégios
RUN addgroup --system nexo && adduser --system --ingroup nexo nexo

# ownership da aplicação
RUN chown -R nexo:nexo /app

# executa como usuário sem privilégios
USER nexo

EXPOSE 8080

ENV ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["dotnet", "Nexo.Api.dll"]