#!/bin/bash

# Test the TTS hosted service endpoint with advanced features

echo "Testing TTS hosted service endpoint with ChatterboxMultilingualTTS..."

# Start the service in the background
echo "Starting SharpAI service..."
cd /workspaces/sharpai
dotnet run --project SharpAI &
SERVICE_PID=$!

# Wait for service to start
echo "Waiting for service to start (30 seconds)..."
sleep 30

# Test 1: Basic TTS
echo "Test 1: Basic TTS..."
curl -X POST "http://localhost:5000/v1/audio/speech" \
  -H "Content-Type: application/json" \
  -d '{
    "input": "Hello world, this is a test of the hosted TTS service.",
    "model": "tts-1", 
    "voice": "alloy",
    "response_format": "wav"
  }' \
  --output test_basic_speech.wav \
  --max-time 60 \
  --silent

echo "✅ Basic TTS test completed"

# Test 2: Emotional speech with emotion_intensity
echo "Test 2: Emotional speech with emotion_intensity..."
curl -X POST "http://localhost:5000/v1/audio/speech" \
  -H "Content-Type: application/json" \
  -d '{
    "input": "I am absolutely thrilled and excited about this amazing breakthrough in technology!",
    "model": "tts-1", 
    "voice": "nova",
    "response_format": "wav",
    "emotion_intensity": 0.8
  }' \
  --output test_emotional_speech.wav \
  --max-time 60 \
  --silent

echo "✅ Emotional speech test completed"

# Test 3: Different language (Spanish)
echo "Test 3: Spanish language test..."
curl -X POST "http://localhost:5000/v1/audio/speech" \
  -H "Content-Type: application/json" \
  -d '{
    "input": "Hola mundo, esto es una prueba del sistema de síntesis de voz multilingüe.",
    "model": "tts-1", 
    "voice": "echo",
    "response_format": "wav"
  }' \
  --output test_spanish_speech.wav \
  --max-time 60 \
  --silent

echo "✅ Spanish language test completed"

# Test 4: Manual exaggeration control
echo "Test 4: Manual exaggeration control..."
curl -X POST "http://localhost:5000/v1/audio/speech" \
  -H "Content-Type: application/json" \
  -d '{
    "input": "This demonstrates manual control of exaggeration for fine-tuned dramatic expression.",
    "model": "tts-1", 
    "voice": "shimmer",
    "response_format": "wav",
    "exaggeration": 0.7
  }' \
  --output test_manual_exaggeration.wav \
  --max-time 60 \
  --silent

echo "✅ Manual exaggeration control test completed"

echo ""
echo "All tests completed! Generated files:"
echo "- test_basic_speech.wav (Basic TTS)"
echo "- test_emotional_speech.wav (High emotion_intensity=0.8 with auto-optimized cfg_weight)"
echo "- test_spanish_speech.wav (Spanish language auto-detection)"
echo "- test_manual_exaggeration.wav (Manual exaggeration=0.7 with auto-optimized cfg_weight)"

# Clean up
echo ""
echo "Stopping service..."
kill $SERVICE_PID 2>/dev/null || true

echo "Test script finished."