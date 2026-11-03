# Smart CCTV Mini web: ASP.NET MVC 5 (.NET Framework 4.8 code) hosted on Mono + xsp4 (Linux).
# Debian buster is EOL, so apt reads archive.debian.org. Build needs internet (apt + nuget):
# build on a connected machine and ship the image (see ../deploy).
FROM debian:buster AS base
RUN set -eux; \
    printf 'deb http://archive.debian.org/debian buster main\ndeb http://archive.debian.org/debian-security buster/updates main\n' > /etc/apt/sources.list; \
    printf 'Acquire::Check-Valid-Until "false";\nAcquire::Retries "5";\n' > /etc/apt/apt.conf.d/99archive; \
    apt-get update -qq; \
    apt-get install -y -qq --no-install-recommends mono-xsp4 mono-mcs libmono-system-data4.0-cil curl ca-certificates; \
    rm -rf /var/lib/apt/lists/*

FROM base AS build
RUN set -eux; \
    apt-get update -qq; apt-get install -y -qq --no-install-recommends nuget ca-certificates-mono; \
    rm -rf /var/lib/apt/lists/*
WORKDIR /src
COPY src/packages.config .
RUN nuget restore packages.config -PackagesDirectory packages -Source https://www.nuget.org/api/v2/ -NonInteractive
COPY src/ .
RUN set -eux; mkdir -p /webroot/bin; \
    for p in packages/Microsoft.AspNet.Mvc.5.2.9/lib/net45 packages/Microsoft.AspNet.Razor.3.2.9/lib/net45 \
             packages/Microsoft.AspNet.WebPages.3.2.9/lib/net45 packages/Microsoft.Web.Infrastructure.2.0.0/lib/net40; do \
      cp $p/*.dll /webroot/bin/; done; \
    REFS=$(for d in /webroot/bin/*.dll; do printf -- '-r:%s ' "$d"; done); \
    mcs -target:library -out:/webroot/bin/MiniWeb.dll -sdk:4.5 -langversion:7 -nowarn:1591 \
        -r:System.dll -r:System.Core.dll -r:System.Data.dll -r:System.Web.dll -r:System.Web.Extensions.dll \
        -r:System.Runtime.Serialization.dll -r:Microsoft.CSharp.dll -r:System.Net.Http.dll $REFS \
        $(find . -path ./packages -prune -o -name '*.cs' -print); \
    cp -r Views Content Global.asax Web.config /webroot/

FROM base
WORKDIR /app
COPY --from=build /webroot ./
COPY start.sh /start.sh
EXPOSE 8080
CMD ["/bin/sh", "/start.sh"]
