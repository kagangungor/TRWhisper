import asyncio
import os
import subprocess
import wave
import struct
import edge_tts
import static_ffmpeg

static_ffmpeg.add_paths()

AUDIO_DIR = os.path.join(os.path.dirname(__file__), "audio")
os.makedirs(AUDIO_DIR, exist_ok=True)

async def synthesize(text, filename):
    mp3_path = os.path.join(AUDIO_DIR, f"{filename}.mp3")
    wav_path = os.path.join(AUDIO_DIR, f"{filename}.wav")
    communicate = edge_tts.Communicate(text, "tr-TR-AhmetNeural")
    await communicate.save(mp3_path)
    subprocess.run(
        ["ffmpeg", "-y", "-i", mp3_path, "-ar", "16000", "-ac", "1", "-c:a", "pcm_s16le", wav_path],
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
        check=True
    )
    if os.path.exists(mp3_path):
        os.remove(mp3_path)
    with wave.open(wav_path, "r") as w:
        duration = w.getnframes() / w.getframerate()
    print(f"Generated {filename}.wav ({duration:.2f} s): '{text}'")
    return duration

def generate_silence(filename, seconds=2.0):
    wav_path = os.path.join(AUDIO_DIR, f"{filename}.wav")
    num_samples = int(16000 * seconds)
    with wave.open(wav_path, "w") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(16000)
        w.writeframes(struct.pack("<" + ("h" * num_samples), *([0] * num_samples)))
    print(f"Generated {filename}.wav ({seconds:.2f} s silence)")

async def main():
    # 1. Short speech (~3.2 s)
    await synthesize("TRWhisper sesli dikte testi.", "speech_3s")
    
    # 2. Long speech (~10.7 s)
    long_text = (
        "TRWhisper, Windows üzerinde tamamen yerel çalışan, "
        "gizliliğe önem veren modern bir dikte aracıdır. "
        "Cümleleri anında metne çevirir."
    )
    await synthesize(long_text, "speech_10s")
    
    # 3. Empty / Silence (2.0 s)
    generate_silence("silence_2s", 2.0)

if __name__ == "__main__":
    asyncio.run(main())
