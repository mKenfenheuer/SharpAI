# SharpAI

SharpAI is a self-hosted speech-to-text server for .NET 9. It exposes an OpenAI-style audio API (`/v1/audio/transcriptions`, `/v1/audio/translations`, `/v1/models`) and runs Whisper models locally through [Whisper.net](https://github.com/sandrohanea/whisper.net), the .NET binding for [whisper.cpp](https://github.com/ggml-org/whisper.cpp). FFmpeg converts and pre-processes the uploaded audio before transcription.

## Features

- OpenAI-style endpoints for audio transcription, translation and model listing.
- Local inference with the Whisper `large-v3-turbo` model in GGML format.
- Automatic model download on first use, stored in a local `Data/Models` directory.
- Input conversion with FFmpeg: any format FFmpeg can decode is converted to 16 kHz mono PCM WAV.
- Speech segmentation by silence detection, loudness normalization and filtering of common Whisper hallucinations and repeated sentences.
- Response formats `json`, `text`, `verbose_json`, `srt` and `vtt`.
- Docker images for CPU and NVIDIA CUDA.

## Requirements

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) to build and run from source.
- [FFmpeg](https://ffmpeg.org/) with `ffmpeg` and `ffprobe` on the `PATH`. The Docker images install it.
- Docker, optional, for the container images.
- For the CUDA image: an NVIDIA GPU, a driver that supports CUDA 12.4 and the [NVIDIA Container Toolkit](https://docs.nvidia.com/datacenter/cloud-native/container-toolkit/latest/install-guide.html).
- Disk space and memory for the Whisper model (several GB).

## Quick start

Run the server from source:

```bash
cd SharpAI
dotnet run
```

The default launch profile listens on `http://localhost:5164` and uses the `Development` environment. The first transcription request downloads the model into `SharpAI/Data/Models/`. This can take several minutes.

Transcribe a file:

```bash
curl http://localhost:5164/v1/audio/transcriptions \
  -F "file=@speech.mp3;type=audio/mpeg" \
  -F "model=whisper-1"
```

Set the content type explicitly with `;type=...`. Without it, curl sends `application/octet-stream`, and the transcription endpoint rejects the file (see [Supported input](#supported-input)).

## Docker

### CPU image

`Dockerfile` builds with the `SharpAI/` directory as the build context:

```bash
docker build -f Dockerfile -t sharpai:cpu SharpAI
```

```bash
docker run -d --name sharpai \
  -p 8080:80 \
  -v /path/to/sharpai-data:/app/Data \
  sharpai:cpu
```

### CUDA image

`Dockerfile.cuda` builds with the repository root as the build context. It replaces the CPU runtime with `Whisper.net.Runtime.Cuda` and publishes a self-contained `linux-x64` binary on top of `nvidia/cuda:12.4.0-runtime-ubuntu22.04`:

```bash
docker build -f Dockerfile.cuda -t sharpai:cuda .
```

```bash
docker run -d --name sharpai \
  --gpus all \
  -p 8080:80 \
  -v /path/to/sharpai-data:/app/Data \
  sharpai:cuda
```

### Notes

- Both images listen on port 80 inside the container (`ASPNETCORE_HTTP_PORTS=80`).
- The working directory is `/app`, so models are stored in `/app/Data/Models`. Mount `/app/Data` to keep downloaded models across container restarts.
- Containers run in the `Production` environment. In this environment the service warms up the model at startup, so the first start downloads the model before the first request.
- The repository has no `.dockerignore`. Build from a clean checkout so that local `bin/`, `obj/` and `Data/` directories do not end up in the build context.

## API

| Method | Path | Description |
| --- | --- | --- |
| `POST` | `/v1/audio/transcriptions` | Transcribe an audio file. |
| `POST` | `/v1/audio/translations` | Transcribe an audio file with the language set to English. |
| `GET` | `/v1/models` | List available models. |
| `GET` | `/v1/models/audio` | List available audio models. |
| `GET` | `/v1/models/{model_id}` | Get a single model. |

In the `Development` environment, the OpenAPI document is available at `/openapi/v1.json`.

The server has no authentication. Do not expose it to untrusted networks without a reverse proxy that handles access control.

### POST /v1/audio/transcriptions

The request uses `multipart/form-data`. Form field names are matched case-insensitively against the request properties.

| Field | Type | Default | Description |
| --- | --- | --- | --- |
| `file` | file | required | Audio file to transcribe. |
| `model` | string | `whisper-1` | Model name. See [Models](#models). |
| `language` | string | `auto` | ISO-639-1 language code, for example `en` or `de`. `auto` enables language detection. |
| `prompt` | string | none | Initial prompt that guides style and vocabulary. |
| `temperature` | number | `0.1` | Sampling temperature. Values above `0.3` are capped at `0.3`. |
| `responseformat` | string | `json` | `json`, `text`, `verbose_json`, `srt` or `vtt`. |
| `timestampgranularities` | string | none | Comma-separated list of `segment` and `word`. `word` adds a `words` array to `verbose_json`. |

The current version binds the response format and timestamp granularities only under the names `responseformat` and `timestampgranularities` (any casing, for example `ResponseFormat`). The OpenAI field names `response_format` and `timestamp_granularities` are ignored, and the server falls back to `json`.

#### Supported input

The transcription endpoint accepts these content types:

`audio/mpeg`, `audio/mp3`, `audio/wav`, `audio/wave`, `audio/mp4`, `audio/m4a`, `audio/webm`, `audio/ogg`, `audio/flac`

If the client sends no content type, the server derives it from the file extension (`.mp3`, `.wav`, `.mp4`, `.m4a`, `.webm`, `.ogg`, `.flac`). Other content types, including `application/octet-stream` and `audio/x-wav`, return `400 Bad Request`. The maximum request size is 10 GB.

#### Response: `json` (default)

Segment `start` and `end` values are integers in milliseconds.

```bash
curl http://localhost:5164/v1/audio/transcriptions \
  -F "file=@speech.mp3;type=audio/mpeg" \
  -F "language=en"
```

```json
{
  "segments": [
    { "id": 0, "start": 0, "end": 4200, "text": "Welcome to the weekly status meeting." },
    { "id": 1, "start": 4800, "end": 9100, "text": "Let us start with the open issues." }
  ],
  "text": "Welcome to the weekly status meeting. Let us start with the open issues."
}
```

#### Response: `text`

```bash
curl http://localhost:5164/v1/audio/transcriptions \
  -F "file=@speech.wav;type=audio/wav" \
  -F "responseformat=text"
```

```text
Welcome to the weekly status meeting. Let us start with the open issues.
```

#### Response: `verbose_json`

`duration` is in seconds. Segment and word `start` and `end` values are in milliseconds. `language` echoes the request value, or `auto` if none was given; it is not the detected language. `temperature`, `avg_logprob`, `compression_ratio`, `no_speech_prob` and `seek` contain fixed placeholder values. Word timestamps are estimated by dividing each segment evenly across its words.

```bash
curl http://localhost:5164/v1/audio/transcriptions \
  -F "file=@speech.mp3;type=audio/mpeg" \
  -F "responseformat=verbose_json" \
  -F "timestampgranularities=segment,word"
```

```json
{
  "text": "Welcome to the weekly status meeting.",
  "task": "transcribe",
  "language": "auto",
  "duration": 4.4,
  "segments": [
    {
      "id": 0,
      "seek": 0,
      "start": 0,
      "end": 4200,
      "text": "Welcome to the weekly status meeting.",
      "temperature": 0.2,
      "avg_logprob": -0.5,
      "compression_ratio": 2.4,
      "no_speech_prob": 0.1
    }
  ],
  "words": [
    { "word": "Welcome", "start": 0, "end": 700 },
    { "word": "to", "start": 700, "end": 1400 }
  ]
}
```

The `words` array is shortened in this example.

#### Response: `srt` and `vtt`

`srt` returns SubRip subtitles as `text/plain`. `vtt` returns WebVTT subtitles as `text/vtt`. See [Known limitations](#known-limitations) for the current timestamp issue.

```bash
curl http://localhost:5164/v1/audio/transcriptions \
  -F "file=@speech.mp3;type=audio/mpeg" \
  -F "responseformat=vtt"
```

### POST /v1/audio/translations

Accepts the same form fields as `/v1/audio/transcriptions` and returns the same response formats. The server sets `language` to `en` and runs the same transcription pipeline. It does not enable the Whisper translate task, so the result depends on how the model handles non-English audio with a forced English language. In `verbose_json`, `task` is `translate`. This endpoint does not check the content type of the upload.

```bash
curl http://localhost:5164/v1/audio/translations \
  -F "file=@interview_de.mp3;type=audio/mpeg"
```

### GET /v1/models

```bash
curl http://localhost:5164/v1/models
```

```json
{
  "object": "list",
  "data": [
    { "id": "large-v3-turbo", "object": "model", "created": 1763640000, "owned_by": "openai" },
    { "id": "whisper-1", "object": "model", "created": 1763640000, "owned_by": "openai" }
  ]
}
```

`created` is the current time of the request, not the model creation date. `GET /v1/models/audio` returns the same list with an additional `"type": "audio"` field per entry.

### GET /v1/models/{model_id}

```bash
curl http://localhost:5164/v1/models/whisper-1
```

```json
{ "id": "whisper-1", "object": "model", "created": 1763640000, "owned_by": "openai", "type": "audio" }
```

Unknown model IDs return `404 Not Found`.

### Errors

Errors use an OpenAI-style error object:

```json
{ "error": { "message": "No audio file provided", "type": "invalid_request_error" } }
```

| Status | Cause |
| --- | --- |
| `400` | Missing or empty file, or unsupported content type. |
| `404` | Unknown model ID on `/v1/models/{model_id}`. |
| `500` | Conversion or transcription failure. The body also contains a `request_id` that matches the server log. |

### OpenAI client libraries

Clients that use the OpenAI request format can point their base URL at the server. The server ignores the API key, but most SDKs require a non-empty value.

```python
from openai import OpenAI

client = OpenAI(base_url="http://localhost:5164/v1", api_key="unused")

with open("speech.mp3", "rb") as audio:
    result = client.audio.transcriptions.create(model="whisper-1", file=audio)

print(result.text)
```

Because `response_format` is not bound (see above), the server always returns the default `json` response to such clients. It contains the `text` field, which SDKs read.

## Models

`WhisperModelManager` defines one model:

| Name | File | Location |
| --- | --- | --- |
| `large-v3-turbo` | `ggml-large-v3-turbo.bin` | `Data/Models/` |

Model resolution works as follows:

1. The requested name is normalized. OpenAI-style names map to internal names, for example `whisper-1` to `base`, `whisper-medium` to `medium` and `whisper-large-v3-turbo` to `large-v3-turbo`.
2. If the normalized name is not configured, the server logs a warning and uses `large-v3-turbo`. In the current version, every request therefore uses `large-v3-turbo`.
3. If `Data/Models/ggml-large-v3-turbo.bin` does not exist, the server downloads the model from Hugging Face with the Whisper.net downloader. Only one download runs at a time.
4. The model is loaded once with GPU use and flash attention enabled and stays in memory.

`Data/Models` is relative to the process working directory: `SharpAI/Data/Models` when started with `dotnet run` from `SharpAI/`, and `/app/Data/Models` in the Docker images.

To provide the model manually, place a GGML Whisper model file named `ggml-large-v3-turbo.bin` in `Data/Models/` before the first request. The whisper.cpp project publishes GGML models on [Hugging Face](https://huggingface.co/ggerganov/whisper.cpp).

GPU acceleration depends on the native runtime package. The default build references `Whisper.net.Runtime`. The CUDA image replaces it with `Whisper.net.Runtime.Cuda` and `Whisper.net.Runtime.Cuda.Linux`.

## Configuration

SharpAI has no application-specific settings. It uses the standard ASP.NET Core configuration sources (`appsettings.json`, `appsettings.{Environment}.json`, environment variables and command-line arguments).

| Key | Description |
| --- | --- |
| `ASPNETCORE_ENVIRONMENT` | `Development` exposes `/openapi/v1.json`. `Production` (the default outside the launch profile) warms up the model at startup. |
| `ASPNETCORE_HTTP_PORTS` | HTTP ports to listen on. The Docker images set `80`. |
| `ASPNETCORE_URLS` | Alternative to `ASPNETCORE_HTTP_PORTS`, for example `http://0.0.0.0:5000`. |
| `Logging:LogLevel:Default` | Default log level. `Information` in `appsettings.json`, `Debug` in `appsettings.Development.json`. |
| `Logging:LogLevel:SharpAI.Services.WhisperModelInstance` | Log level for native whisper.cpp messages. Set to `Critical` by default to suppress them. |

Environment variables use `__` as the section separator, for example `Logging__LogLevel__Default=Debug`.

Fixed values in the code:

| Setting | Value |
| --- | --- |
| Maximum request and multipart body size | 10 GB |
| Request queue capacity | 50 requests |
| Concurrent transcriptions | 1 (requests are processed in order) |

## Processing pipeline

1. The upload is converted to 16 kHz mono 16-bit PCM WAV with FFmpeg.
2. The audio is loudness-normalized, and FFmpeg `silencedetect` splits it into speech segments. Segments shorter than one second are dropped, and segments less than three seconds apart are merged.
3. Each segment is extracted with speech filters (high-pass, low-pass, loudness and dynamic normalization). Segments with too little energy are skipped.
4. Whisper transcribes each segment with beam search and strict entropy, log-probability and no-speech thresholds.
5. Short fragments, common hallucination phrases and repeated sentences are removed. Segment timestamps are shifted back to the position in the original audio.

## Known limitations

- Every request uses `large-v3-turbo`, regardless of the `model` field.
- `response_format` and `timestamp_granularities` must be sent as `responseformat` and `timestampgranularities`.
- curl and some clients send `application/octet-stream` or `audio/x-wav`, which the transcription endpoint rejects. Set a supported content type explicitly.
- The automatic download requests the `large-v3` GGML model from the Whisper.net downloader and saves it as `ggml-large-v3-turbo.bin`. To use the turbo weights, place the turbo model file in `Data/Models/` yourself.
- SRT and VTT timestamps are scaled incorrectly: segment times are stored in milliseconds but formatted as seconds.
- The translation endpoint forces English as the language instead of using the Whisper translate task.
- The form also accepts a non-standard `stream` field for Server-Sent Events output. This path is experimental and not reliable in the current version.

## Project structure

```text
.
├── Dockerfile                  CPU image (build context: SharpAI/)
├── Dockerfile.cuda             CUDA image (build context: repository root)
├── .devcontainer/              VS Code dev container with GPU access
├── .vscode/launch.json         VS Code debug configuration
├── SharpAI.sln
├── LICENSE
└── SharpAI/
    ├── Program.cs              Host setup, JSON options, upload limits, service registration
    ├── SharpAI.csproj
    ├── appsettings.json
    ├── appsettings.Development.json
    ├── Properties/
    │   └── launchSettings.json Local launch profiles (port 5164)
    ├── Api/
    │   ├── Audio/AudioController.cs     /v1/audio/transcriptions, /v1/audio/translations
    │   └── Models/ModelsController.cs   /v1/models
    ├── Models/                 Request, response and internal queue types
    ├── Services/
    │   ├── WhisperHostedService.cs      Request queue, FFmpeg pipeline, transcription, output formats
    │   └── WhisperModelManager.cs       Model configuration, download and loading
    └── Data/                   Runtime data; models are stored in Data/Models/
```

## License

SharpAI is released under the MIT License. See [LICENSE](LICENSE). Copyright Maximilian Kenfenheuer.

## Acknowledgments

- [Whisper.net](https://github.com/sandrohanea/whisper.net): .NET bindings and native runtimes for whisper.cpp.
- [whisper.cpp](https://github.com/ggml-org/whisper.cpp): C/C++ implementation of the OpenAI Whisper model.
- [FFMpegCore](https://github.com/rosenbjerg/FFMpegCore): .NET wrapper for FFmpeg.
