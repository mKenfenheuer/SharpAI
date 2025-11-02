# FROM nvidia/cuda:12.4.0-runtime-ubuntu22.04 AS base
FROM nvidia/cuda:12.4.0-runtime-ubuntu22.04 AS base
RUN apt-get update && apt-get install -y \
    curl \
    wget \
    ca-certificates \
    python3.11 \
    python3.11-venv \
    python3.11-dev \
    build-essential \
    ffmpeg \
    git \
    libsndfile1 \
    cuda-compat-13-0 \
    && rm -rf /var/lib/apt/lists/*

# Install .NET 9 runtime
RUN wget https://packages.microsoft.com/config/ubuntu/22.04/packages-microsoft-prod.deb -O packages-microsoft-prod.deb \
    && dpkg -i packages-microsoft-prod.deb \
    && rm packages-microsoft-prod.deb \
    && apt-get update \
    && apt-get install -y dotnet-sdk-9.0 \
    && rm -rf /var/lib/apt/lists/*

# Setup Python environment
RUN python3.11 -m venv /opt/pyenv
ENV PATH="/opt/pyenv/bin:$PATH"

ENV PYTHONNET_PYDLL=/usr/lib/x86_64-linux-gnu/libpython3.11.so
ENV LD_LIBRARY_PATH=/usr/lib/x86_64-linux-gnu:$LD_LIBRARY_PATH

RUN pip install torch torchaudio --index-url https://download.pytorch.org/whl/cu124

# Upgrade pip and install dependencies
WORKDIR /opt/
RUN git clone --branch faster-multi https://github.com/mkenfenheuer/chatterbox.git || true
WORKDIR /opt/chatterbox
RUN pip install -e .
RUN pip install langdetect

# Install FFmpeg for audio processing
RUN apt-get update && apt-get install -y \
    ffmpeg \
    && rm -rf /var/lib/apt/lists/*
