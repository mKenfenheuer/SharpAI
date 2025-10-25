# Deployment Guide

This guide covers deployment options for SharpAI, from development setups to production environments. SharpAI can be deployed as a standalone .NET application or using Docker containers.

## Deployment Options

### 1. Local Development Deployment

The simplest way to run SharpAI for development and testing.

#### Prerequisites

- .NET 9.0 SDK
- Git

#### Steps

1. **Clone the repository:**
   ```bash
   git clone <repository-url>
   cd sharpai
   ```

2. **Build the project:**
   ```bash
   dotnet build
   ```

3. **Run the application:**
   ```bash
   cd SharpAI
   dotnet run
   ```

4. **Access the API:**
   - Default URL: `http://localhost:5000`
   - API endpoints: `http://localhost:5000/v1/`

### 2. Docker Deployment (CPU)

Recommended for most production deployments and consistent environments.

#### Prerequisites

- Docker
- Docker Compose (optional)

#### Build and Run

1. **Build the Docker image:**
   ```bash
   docker build -t sharpai .
   ```

2. **Run the container:**
   ```bash
   docker run -d \
     --name sharpai \
     -p 80:80 \
     -v $(pwd)/models:/app/models \
     -v $(pwd)/config:/app/config \
     sharpai
   ```

3. **Access the API:**
   - URL: `http://localhost`

#### Docker Compose

Create a `docker-compose.yml` file:

```yaml
version: '3.8'

services:
  sharpai:
    build: .
    ports:
      - "80:80"
      - "443:443"
    volumes:
      - ./models:/app/models
      - ./config:/app/config
      - ./logs:/app/logs
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - ASPNETCORE_HTTP_PORTS=80
      - ASPNETCORE_HTTPS_PORTS=443
    restart: unless-stopped
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost/v1/models"]
      interval: 30s
      timeout: 10s
      retries: 3

  nginx:
    image: nginx:alpine
    ports:
      - "80:80"
      - "443:443"
    volumes:
      - ./nginx.conf:/etc/nginx/nginx.conf
      - ./ssl:/etc/nginx/ssl
    depends_on:
      - sharpai
    restart: unless-stopped
```

Run with:
```bash
docker-compose up -d
```

### 3. Docker Deployment (CUDA GPU)

For deployments requiring GPU acceleration.

#### Prerequisites

- Docker with NVIDIA Container Toolkit
- NVIDIA GPU with CUDA support
- NVIDIA drivers

#### Setup NVIDIA Container Toolkit

```bash
# Add NVIDIA package repository
distribution=$(. /etc/os-release;echo $ID$VERSION_ID)
curl -s -L https://nvidia.github.io/nvidia-docker/gpgkey | sudo apt-key add -
curl -s -L https://nvidia.github.io/nvidia-docker/$distribution/nvidia-docker.list | sudo tee /etc/apt/sources.list.d/nvidia-docker.list

# Install nvidia-container-toolkit
sudo apt-get update && sudo apt-get install -y nvidia-container-toolkit

# Restart Docker
sudo systemctl restart docker
```

#### Build and Run CUDA Image

1. **Build the CUDA Docker image:**
   ```bash
   docker build -f Dockerfile.cuda -t sharpai-cuda .
   ```

2. **Run with GPU support:**
   ```bash
   docker run -d \
     --name sharpai-cuda \
     --gpus all \
     -p 80:80 \
     -v $(pwd)/models:/app/models \
     -e CUDA_VISIBLE_DEVICES=0 \
     sharpai-cuda
   ```

3. **Docker Compose with GPU:**
   ```yaml
   version: '3.8'
   
   services:
     sharpai:
       build:
         dockerfile: Dockerfile.cuda
       deploy:
         resources:
           reservations:
             devices:
               - driver: nvidia
                 count: 1
                 capabilities: [gpu]
       ports:
         - "80:80"
       volumes:
         - ./models:/app/models
       environment:
         - CUDA_VISIBLE_DEVICES=0
   ```

## Configuration

### Environment Variables

Configure the application using environment variables:

```bash
# Server configuration
ASPNETCORE_URLS=http://0.0.0.0:80
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_HTTP_PORTS=80
ASPNETCORE_HTTPS_PORTS=443

# Logging
Logging__LogLevel__Default=Information
Logging__LogLevel__Microsoft.AspNetCore=Warning

# Brave Search API (optional)
BraveSearch__ApiKey=your-api-key
BraveSearch__BaseUrl=https://api.search.brave.com/res/v1
```

### Configuration Files

#### appsettings.Production.json

Create a production configuration file:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "System.Net.Http.HttpClient": "Warning",
      "SharpAI.Services.LLamaNativeLogging": "Critical",
      "SharpAI.Services.WhisperModelInstance": "Critical"
    }
  },
  "AllowedHosts": "*",
  "BraveSearch": {
    "ApiKey": "${BRAVE_SEARCH_API_KEY}",
    "BaseUrl": "https://api.search.brave.com/res/v1"
  },
  "Kestrel": {
    "Limits": {
      "MaxRequestBodySize": 104857600,
      "RequestHeadersTimeout": "00:01:00"
    }
  }
}
```

### Volume Mounts

Map important directories for persistence and configuration:

```bash
# Models directory (for AI models)
-v /host/path/models:/app/models

# Configuration directory
-v /host/path/config:/app/config

# Logs directory
-v /host/path/logs:/app/logs

# Temporary files
-v /host/path/temp:/tmp
```

## Reverse Proxy Setup

### Nginx Configuration

Create `/etc/nginx/sites-available/sharpai`:

```nginx
upstream sharpai {
    server localhost:5000;
}

server {
    listen 80;
    server_name your-domain.com;
    
    # Redirect HTTP to HTTPS
    return 301 https://$server_name$request_uri;
}

server {
    listen 443 ssl http2;
    server_name your-domain.com;

    # SSL Configuration
    ssl_certificate /path/to/your/cert.pem;
    ssl_certificate_key /path/to/your/key.pem;
    ssl_protocols TLSv1.2 TLSv1.3;
    ssl_ciphers HIGH:!aNULL:!MD5;

    # Large file uploads
    client_max_body_size 100M;
    client_body_timeout 300s;
    client_header_timeout 300s;

    # Proxy settings
    proxy_connect_timeout 300s;
    proxy_send_timeout 300s;
    proxy_read_timeout 300s;

    location / {
        proxy_pass http://sharpai;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        
        # WebSocket support (for streaming)
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        
        # Disable buffering for streaming responses
        proxy_buffering off;
        proxy_cache off;
    }

    # Health check endpoint
    location /health {
        proxy_pass http://sharpai/v1/models;
        access_log off;
    }
}
```

Enable the site:
```bash
sudo ln -s /etc/nginx/sites-available/sharpai /etc/nginx/sites-enabled/
sudo nginx -t
sudo systemctl reload nginx
```

### Apache Configuration

For Apache users, create a virtual host:

```apache
<VirtualHost *:80>
    ServerName your-domain.com
    Redirect permanent / https://your-domain.com/
</VirtualHost>

<VirtualHost *:443>
    ServerName your-domain.com
    
    SSLEngine on
    SSLCertificateFile /path/to/your/cert.pem
    SSLCertificateKeyFile /path/to/your/key.pem
    
    # Large file uploads
    LimitRequestBody 104857600
    
    ProxyPreserveHost On
    ProxyPass / http://localhost:5000/
    ProxyPassReverse / http://localhost:5000/
    
    # WebSocket support
    RewriteEngine On
    RewriteCond %{HTTP:Upgrade} websocket [NC]
    RewriteCond %{HTTP:Connection} upgrade [NC]
    RewriteRule ^/?(.*) "ws://localhost:5000/$1" [P,L]
</VirtualHost>
```

## Performance Optimization

### Memory Configuration

Optimize memory usage for your deployment:

```bash
# Environment variables for memory management
export DOTNET_GCServer=1
export DOTNET_GCConcurrent=1
export DOTNET_GCRetainVM=1

# For containers, set memory limits
docker run --memory=8g --memory-swap=8g sharpai
```

### CPU Optimization

```bash
# Set CPU affinity for better performance
docker run --cpuset-cpus="0-7" sharpai

# Adjust .NET thread pool
export DOTNET_ThreadPool_UnfairSemaphoreSpinLimit=6
export DOTNET_SYSTEM_NET_SOCKETS_INLINE_COMPLETIONS=1
```

### GPU Memory Management

For CUDA deployments:

```bash
# Limit GPU memory usage
export CUDA_VISIBLE_DEVICES=0
export CUDA_MEMORY_FRACTION=0.8

# In Docker
docker run --gpus '"device=0"' -e CUDA_MEMORY_FRACTION=0.8 sharpai-cuda
```

## Monitoring and Logging

### Health Checks

SharpAI includes health check endpoints:

- `/v1/models` - Check if the service is running and models are loaded

### Logging Configuration

Configure structured logging for production:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning",
      "Microsoft.Hosting.Lifetime": "Information"
    },
    "Console": {
      "IncludeScopes": true,
      "TimestampFormat": "yyyy-MM-dd HH:mm:ss "
    }
  }
}
```

### Metrics and Monitoring

Use tools like Prometheus for monitoring:

```yaml
# docker-compose.yml addition
  prometheus:
    image: prom/prometheus
    ports:
      - "9090:9090"
    volumes:
      - ./prometheus.yml:/etc/prometheus/prometheus.yml

  grafana:
    image: grafana/grafana
    ports:
      - "3000:3000"
    environment:
      - GF_SECURITY_ADMIN_PASSWORD=admin
```

## Security Considerations

### Network Security

- Use HTTPS in production
- Configure firewall rules to restrict access
- Use VPN or private networks for internal access

### Container Security

```bash
# Run as non-root user
docker run --user 1001:1001 sharpai

# Read-only filesystem where possible
docker run --read-only --tmpfs /tmp sharpai

# Drop capabilities
docker run --cap-drop=ALL sharpai
```

### API Security

- Implement authentication/authorization
- Use API keys or JWT tokens
- Rate limiting
- Input validation and sanitization

## Backup and Recovery

### Model Backup

```bash
# Backup model directory
tar -czf models-backup-$(date +%Y%m%d).tar.gz /path/to/models/

# Restore models
tar -xzf models-backup-20231225.tar.gz -C /path/to/models/
```

### Configuration Backup

```bash
# Backup configuration
cp appsettings.Production.json appsettings.Production.json.backup
```

### Database Backup (if applicable)

If using persistent storage:

```bash
# Docker volume backup
docker run --rm -v sharpai_data:/data -v $(pwd):/backup alpine tar czf /backup/backup.tar.gz /data
```

## Troubleshooting

### Common Issues

1. **Out of Memory Errors**
   - Increase container memory limits
   - Optimize model loading
   - Monitor memory usage

2. **CUDA Not Found**
   - Verify NVIDIA drivers
   - Check CUDA version compatibility
   - Ensure nvidia-container-toolkit is installed

3. **Model Loading Failures**
   - Check model file permissions
   - Verify model file integrity
   - Ensure sufficient disk space

4. **Performance Issues**
   - Monitor CPU/GPU usage
   - Check network latency
   - Optimize batch sizes

### Debugging

Enable debug logging:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "SharpAI": "Debug"
    }
  }
}
```

## Scaling

### Horizontal Scaling

Deploy multiple instances behind a load balancer:

```yaml
version: '3.8'

services:
  sharpai-1:
    build: .
    environment:
      - INSTANCE_ID=1
  
  sharpai-2:
    build: .
    environment:
      - INSTANCE_ID=2
  
  load-balancer:
    image: nginx:alpine
    ports:
      - "80:80"
    volumes:
      - ./nginx-lb.conf:/etc/nginx/nginx.conf
    depends_on:
      - sharpai-1
      - sharpai-2
```

### Vertical Scaling

Increase resources for single instances:

```bash
# More CPU and memory
docker run --cpus="8" --memory="16g" sharpai

# Multiple GPUs
docker run --gpus "device=0,1" sharpai-cuda
```

This deployment guide provides comprehensive instructions for deploying SharpAI in various environments, from development to production-scale deployments.