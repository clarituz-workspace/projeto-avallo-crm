FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props Avallo.slnx ./
COPY Avallo.Connectors.Abstractions/Avallo.Connectors.Abstractions.csproj Avallo.Connectors.Abstractions/
COPY Avallo.Connector.MercadoLivre/Avallo.Connector.MercadoLivre.csproj Avallo.Connector.MercadoLivre/
COPY Avallo.Connector.Shopee/Avallo.Connector.Shopee.csproj Avallo.Connector.Shopee/
COPY Avallo.Connector.Amazon/Avallo.Connector.Amazon.csproj Avallo.Connector.Amazon/
COPY Avallo.Client/Avallo.Client.csproj Avallo.Client/
COPY Avallo.Web/Avallo.Web.csproj Avallo.Web/
RUN dotnet restore Avallo.Web/Avallo.Web.csproj

COPY Avallo.Connectors.Abstractions/ Avallo.Connectors.Abstractions/
COPY Avallo.Connector.MercadoLivre/ Avallo.Connector.MercadoLivre/
COPY Avallo.Connector.Shopee/ Avallo.Connector.Shopee/
COPY Avallo.Connector.Amazon/ Avallo.Connector.Amazon/
COPY Avallo.Client/ Avallo.Client/
COPY Avallo.Web/ Avallo.Web/
COPY build-assets/blazor.web.js /tmp/blazor.web.js
# Os conectores nao entram no assembly do Core: o build os deposita em
# Avallo.Web/connectors e eles seguem para a imagem como plugin.
# O ls final e proposital - falha o build se nenhum plugin foi produzido.
RUN dotnet publish Avallo.Web/Avallo.Web.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    && cp /tmp/blazor.web.js /app/publish/wwwroot/_framework/blazor.web.js \
    && test -f /app/publish/wwwroot/_framework/blazor.web.js \
    && mkdir -p /app/publish/connectors \
    && cp Avallo.Web/connectors/Avallo.Connector.*.dll /app/publish/connectors/ \
    && cp Avallo.Web/connectors/Avallo.Connector.*.deps.json /app/publish/connectors/ \
    && ls -1 /app/publish/connectors/Avallo.Connector.*.dll

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
RUN test -f /app/wwwroot/_framework/blazor.web.js

ENV ASPNETCORE_ENVIRONMENT=Production
ENV DOTNET_EnableDiagnostics=0
EXPOSE 8080

ENTRYPOINT ["sh", "-c", "dotnet Avallo.Web.dll --urls http://0.0.0.0:${PORT:-8080}"]
