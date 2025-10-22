# Deployment Guide

This guide covers various deployment options for SharpAI, from local development to production Docker deployments with GPU acceleration.

## 🏠 Local Development

### Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- [FFmpeg](https://ffmpeg.org/download.html)
- Git

### Setup

1. **Clone and setup**
   ```bash
   git clone <repository-url>
   cd SharpAI
   dotnet restore
   dotnet build
   ```

2. **Run in development mode**
   ```bash
   dotnet run --environment Development
   ```

3. **Access the API**
   - API: `http://localhost:5000`
   - OpenAPI/Swagger: `http://localhost:5000/openapi/v1.json`

---

## 🐳 Docker Deployment

SharpAI provides three Docker images optimized for different hardware configurations:

### Available Images

| Image Type | Dockerfile | Use Case | GPU Support |
|------------|------------|----------|-------------|
| **CPU** | `Dockerfile` | CPU-only servers, development | ❌ |
| **CUDA 11** | `Dockerfile.cuda11` | NVIDIA GPUs with CUDA 11.x | ✅ |
| **CUDA 12** | `Dockerfile.cuda12` | NVIDIA GPUs with CUDA 12.x | ✅ |

### CPU Deployment

**Best for**: Development, testing, or servers without GPU

```bash
# Build the image
docker build -t sharpai:cpu -f Dockerfile .

# Run the container
docker run -d \
  --name sharpai \
  -p 5000:80 \
  -v $(pwd)/Data:/app/Data \
  sharpai:cpu
```

### CUDA 12 Deployment (Recommended)

**Best for**: Production with modern NVIDIA GPUs

```bash
# Build the image
docker build -t sharpai:cuda12 -f Dockerfile.cuda12 .

# Run with GPU support
docker run -d \
  --name sharpai \
  -p 5000:80 \
  -v $(pwd)/Data:/app/Data \
  --gpus all \
  sharpai:cuda12
```

### CUDA 11 Deployment

**Best for**: Older NVIDIA GPUs or systems with CUDA 11.x

```bash
# Build the image
docker build -t sharpai:cuda11 -f Dockerfile.cuda11 .

# Run with GPU support
docker run -d \
  --name sharpai \
  -p 5000:80 \
  -v $(pwd)/Data:/app/Data \
  --gpus all \
  sharpai:cuda11
```

### Docker Compose

Create a `docker-compose.yml` for easier management:

```yaml
version: '3.8'

services:
  sharpai:
    image: sharpai:cuda12  # or cuda11/cpu
    container_name: sharpai
    ports:
      - "5000:80"
    volumes:
      - ./Data:/app/Data
      - ./logs:/app/logs
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
    deploy:
      resources:
        reservations:
          devices:
            - driver: nvidia
              count: all
              capabilities: [gpu]
    restart: unless-stopped
```

Run with:
```bash
docker compose up -d
```

---

## ☁️ Cloud Deployment

### Azure Container Instances

```bash
# Create resource group
az group create --name sharpai-rg --location eastus

# Deploy container (CPU)
az container create \
  --resource-group sharpai-rg \
  --name sharpai \
  --image your-registry/sharpai:cpu \
  --ports 80 \
  --dns-name-label sharpai-unique-name \
  --cpu 2 \
  --memory 4
```

### AWS ECS

1. **Create task definition** (`task-definition.json`):

```json
{
  "family": "sharpai",
  "networkMode": "awsvpc",
  "requiresCompatibilities": ["FARGATE"],
  "cpu": "1024",
  "memory": "2048",
  "executionRoleArn": "arn:aws:iam::account:role/ecsTaskExecutionRole",
  "containerDefinitions": [
    {
      "name": "sharpai-container",
      "image": "your-registry/sharpai:cpu",
      "portMappings": [
        {
          "containerPort": 80,
          "protocol": "tcp"
        }
      ],
      "logConfiguration": {
        "logDriver": "awslogs",
        "options": {
          "awslogs-group": "/ecs/sharpai",
          "awslogs-region": "us-east-1",
          "awslogs-stream-prefix": "ecs"
        }
      }
    }
  ]
}
```

2. **Deploy to ECS**

```bash
# Register task definition
aws ecs register-task-definition --cli-input-json file://task-definition.json

# Create service
aws ecs create-service \
  --cluster your-cluster \
  --service-name sharpai \
  --task-definition sharpai:1 \
  --desired-count 1 \
  --launch-type FARGATE \
  --network-configuration "awsvpcConfiguration={subnets=[subnet-12345],securityGroups=[sg-12345],assignPublicIp=ENABLED}"
```

### Google Cloud Run

```bash
# Build and push to Google Container Registry
docker build -t gcr.io/your-project/sharpai:cpu .
docker push gcr.io/your-project/sharpai:cpu

# Deploy to Cloud Run
gcloud run deploy sharpai \
  --image gcr.io/your-project/sharpai:cpu \
  --platform managed \
  --region us-central1 \
  --allow-unauthenticated \
  --memory 2Gi \
  --cpu 2
```

---

## 🔧 Production Configuration

### Environment Variables

```bash
# .env file
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://+:80
ASPNETCORE_FORWARDEDHEADERS_ENABLED=true

# Optional: Custom model directories
MODELS_DIRECTORY=/app/Data/Models
WHISPER_MODELS_DIRECTORY=/app/Data/WhisperModels

# Optional: Resource limits
MAX_UPLOAD_SIZE=100000000
MAX_CONCURRENT_MODELS=3
```

### Volume Mounts

```bash
# Essential directories to mount
-v /host/data:/app/Data                    # Model storage
-v /host/logs:/app/logs                    # Application logs
-v /host/config:/app/config                # Configuration overrides
-v /host/temp:/tmp                         # Temporary files
```

### Resource Requirements

| Component | CPU | RAM | Storage | GPU |
|-----------|-----|-----|---------|-----|
| **CPU Only** | 2+ cores | 4GB+ | 20GB+ | None |
| **CUDA 11/12** | 4+ cores | 8GB+ | 50GB+ | 4GB+ VRAM |

### Health Checks

```bash
# Docker health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=5s --retries=3 \
  CMD curl -f http://localhost:80/v1/models || exit 1
```

### Reverse Proxy (Nginx)

```nginx
upstream sharpai {
    server localhost:5000;
}

server {
    listen 80;
    server_name your-domain.com;
    
    client_max_body_size 100M;
    
    location / {
        proxy_pass http://sharpai;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        
        # For streaming responses
        proxy_buffering off;
        proxy_cache off;
    }
}
```

---

## 📊 Monitoring & Logging

### Logging Configuration

Create `appsettings.Production.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "SharpAI": "Information"
    },
    "Console": {
      "IncludeScopes": true,
      "TimestampFormat": "yyyy-MM-dd HH:mm:ss "
    }
  }
}
```

### Docker Logging

```bash
# View logs
docker logs sharpai -f

# Configure log rotation
docker run -d \
  --name sharpai \
  --log-driver json-file \
  --log-opt max-size=10m \
  --log-opt max-file=3 \
  -p 5000:80 \
  sharpai:cuda12
```

### Prometheus Metrics (Future)

SharpAI is designed to support Prometheus metrics for monitoring:

- Request counts and latencies
- Model loading times
- Memory and GPU usage
- Error rates

---

## 🚀 CI/CD with GitLab

The project includes GitLab CI configuration for automated builds and deployments:

```yaml
# .gitlab-ci.yml
stages:
 - build
 - deploy

build_cpu_image:
  stage: build
  script:
    - docker build -t $CI_REGISTRY_IMAGE:cpu-$CI_COMMIT_SHORT_SHA -f Dockerfile .
    - docker push $CI_REGISTRY_IMAGE:cpu-$CI_COMMIT_SHORT_SHA

build_cuda12_image:
  stage: build
  script:
    - docker build -t $CI_REGISTRY_IMAGE:cuda12-$CI_COMMIT_SHORT_SHA -f Dockerfile.cuda12 .
    - docker push $CI_REGISTRY_IMAGE:cuda12-$CI_COMMIT_SHORT_SHA

deploy_production:
  stage: deploy
  only:
    - main
  script:
    - docker run -d --name sharpai -p 80:80 --gpus all $CI_REGISTRY_IMAGE:cuda12-$CI_COMMIT_SHORT_SHA
```

---

## 🔒 Security Considerations

### Production Checklist

- [ ] Run behind reverse proxy with SSL termination
- [ ] Implement API authentication if needed
- [ ] Configure firewall rules
- [ ] Regular security updates
- [ ] Monitor resource usage
- [ ] Implement rate limiting
- [ ] Regular backups of model data
- [ ] Use non-root user in container

### Container Security

```dockerfile
# Example security hardening
USER 1001
COPY --chown=1001:1001 . /app
RUN chmod -R 555 /app
EXPOSE 8080
```

---

## 🔍 Troubleshooting

### Common Issues

**Model downloading fails**
```bash
# Check disk space
df -h

# Check network connectivity
curl -I https://raw.githubusercontent.com/microsoft/Phi-3-mini-4k-instruct-gguf/main/Phi-3-mini-4k-instruct-q4.gguf

# Check container logs
docker logs sharpai
```

**GPU not detected**
```bash
# Verify NVIDIA runtime
docker info | grep nvidia

# Test GPU access
docker run --rm --gpus all nvidia/cuda:12.0-base-ubuntu20.04 nvidia-smi
```

**High memory usage**
```bash
# Monitor container resources
docker stats sharpai

# Limit container memory
docker run --memory="4g" sharpai:cuda12
```

### Performance Tuning

1. **Adjust GPU layers** in model configuration
2. **Optimize context size** based on use case  
3. **Configure model concurrency** limits
4. **Monitor and tune memory allocation**

---

## 📈 Scaling

### Horizontal Scaling

Deploy multiple instances behind a load balancer:

```yaml
# docker-compose.scale.yml
version: '3.8'

services:
  sharpai:
    image: sharpai:cuda12
    deploy:
      replicas: 3
    ports:
      - "5000-5002:80"
    volumes:
      - ./Data:/app/Data:ro  # Read-only shared storage

  nginx:
    image: nginx:alpine
    ports:
      - "80:80"
    volumes:
      - ./nginx.conf:/etc/nginx/nginx.conf
    depends_on:
      - sharpai
```

### Kubernetes Deployment

```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: sharpai
spec:
  replicas: 2
  selector:
    matchLabels:
      app: sharpai
  template:
    metadata:
      labels:
        app: sharpai
    spec:
      containers:
      - name: sharpai
        image: sharpai:cuda12
        ports:
        - containerPort: 80
        resources:
          requests:
            nvidia.com/gpu: 1
          limits:
            nvidia.com/gpu: 1
        volumeMounts:
        - name: model-storage
          mountPath: /app/Data
      volumes:
      - name: model-storage
        persistentVolumeClaim:
          claimName: sharpai-models-pvc
```

This deployment guide should cover most production scenarios. Choose the deployment method that best fits your infrastructure and requirements.