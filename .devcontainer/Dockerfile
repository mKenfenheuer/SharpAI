FROM ubuntu:22.04 AS base
RUN apt-get update && apt-get install -y \
    curl \
    wget \
    ca-certificates \
    git \
    build-essential \
    cmake \
    && rm -rf /var/lib/apt/lists/*

# Install CUDA 
RUN wget https://developer.download.nvidia.com/compute/cuda/repos/ubuntu2204/x86_64/cuda-keyring_1.1-1_all.deb
RUN dpkg -i cuda-keyring_1.1-1_all.deb
RUN apt-get update && apt-get install -y \
    cuda-toolkit-12-4 cuda-drivers

# Install .NET 9 runtime
RUN wget https://packages.microsoft.com/config/ubuntu/22.04/packages-microsoft-prod.deb -O packages-microsoft-prod.deb \
    && dpkg -i packages-microsoft-prod.deb \
    && rm packages-microsoft-prod.deb \
    && apt-get update \
    && apt-get install -y dotnet-sdk-9.0 \
    && rm -rf /var/lib/apt/lists/*

# Install FFmpeg for audio processing
RUN apt-get update && apt-get install -y \
    ffmpeg \
    && rm -rf /var/lib/apt/lists/*