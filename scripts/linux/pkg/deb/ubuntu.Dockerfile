ARG DISTRO_VERSION
FROM ubuntu:${DISTRO_VERSION} 

ARG DISTRO_VERSION_NAME
ARG DISTRO_VERSION

RUN apt update && DEBIAN_FRONTEND=noninteractive apt install -y dos2unix devscripts dh-make wget gettext-base lintian curl dh-systemd debhelper

ENV DEBEMAIL=support@ravendb.net DEBFULLNAME="Hibernating Rhinos LTD" 
ENV DEB_ARCHITECTURE="amd64" RAVEN_SO_ARCH_SUFFIX="x64"
ENV TARBALL_CACHE_DIR="/cache"
ENV DOTNET_RUNTIME_VERSION 5.0
ENV DOTNET_DEPS_VERSION 5.0.2
ENV DEB_DEPS "dotnet-runtime-deps-${DOTNET_RUNTIME_VERSION} (>= ${DOTNET_DEPS_VERSION}), libc6-dev (>= 2.27)"

ENV BUILD_DIR=/build
ENV OUTPUT_DIR=/dist/${DISTRO_VERSION}

COPY assets/ravendb/ /assets/ravendb/
COPY assets/ravendb/ /build/ravendb/
COPY assets/build.sh /build/

RUN find /build -type f -print0 | xargs -0 dos2unix -v

WORKDIR /build/ravendb

CMD /build/build.sh
    
