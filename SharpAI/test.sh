#!/bin/bash

# Check if a model name was provided
if [ -z "$1" ]; then
  echo "Usage: $0 <model_name>"
  exit 1
fi

MODEL_NAME=$1

# Send POST request using curl
curl -X POST http://localhost:5000/v1/chat/completions \
-H "Content-Type: application/json" \
-d "{
    \"model\": \"$MODEL_NAME\",
    \"messages\": [
        {
            \"role\": \"user\",
            \"content\": \"Schreibe ein schönes Gedicht auf Deutsch über den Herbst und seine Farben.\"
        }
    ]
}"
