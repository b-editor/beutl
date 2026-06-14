# Beutl GPU-crash repro image: .NET SDK + SwiftShader runtime deps + native-debug tools.
# Build NATIVE for the host arch (Apple silicon -> linux/arm64) so kernel cores are the GUEST process,
# not a qemu emulator core:  docker build --platform linux/arm64 -t beutl-ss:10.0-arm64 <this-dir>
FROM mcr.microsoft.com/dotnet/sdk:10.0
RUN apt-get update -qq && apt-get install -y -qq \
        libfontconfig1 libfreetype6 libexpat1 libvulkan1 \
        fontconfig fonts-noto-core fonts-noto-cjk fonts-liberation \
        gdb elfutils binutils
