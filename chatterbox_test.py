import torchaudio as ta
from chatterbox.mtl_tts import ChatterboxMultilingualTTS

# Multilingual examples
multilingual_model = ChatterboxMultilingualTTS.from_pretrained(device="cuda")

german_text = "Hello World!"
wav_german = multilingual_model.generate(german_text, language_id="de")
ta.save("test-german.wav", wav_german, multilingual_model.sr)