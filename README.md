# ⚡ yShorts — Advanced AI Vertical Video Generator

yShorts is a professional C# WPF application designed to create viral, high-quality vertical videos (TikTok, Reels, Youtube Shorts) with zero manual editing. By leveraging powerful AI and automation tools, it handles everything from scriptwriting to final rendering.

---

## ✨ Key Features

### 1. **Two Generation Modes**
*   **Basic Mode (Viral Topic):** Just enter a topic (e.g., "History of Tea"). The AI writes the script, finds matching footage, generates the voiceover, and burns subtitles.
*   **Advanced Mode (Stages):** Define specific segments for your video. For each stage, you can provide:
    *   **Narrative Goal:** What the AI should talk about in this part.
    *   **Visual Content/Search:** Keywords for Pexels or...
    *   **Local Video Attachment:** Attach your own clip (MP4/MOV) to a specific speech segment.

### 2. **Professional-Grade Assets**
*   **Gemini AI Scripting:** Smart scripting that understands context and rhythm.
*   **Pexels Integration:** Automatic download of 1080x1920 vertical stock footage.
*   **Edge-TTS Voiceover:** High-quality, human-like neural voices (supports multiple languages including Hebrew).
*   **Subtitle Burning:** Aesthetic, high-contrast captions synced perfectly with the audio.
*   **SEO Suite:** Optimized titles, descriptions, and viral hashtags ready to copy.

### 3. **Perfect Sync Engine**
The heart of yShorts is its custom video engine that ensures:
*   Each spoken sentence matches the visual clip on screen.
*   In Advanced Mode, stages are strictly divided (7-14 seconds each) for high retention.
*   Automatic looping/trimming of videos to match narration duration.

---

## 🛠 Prerequisites & Installation

### 1. External Tools
The application relies on these powerful engines. Ensure they are installed:

*   **FFmpeg & FFprobe:** Used for all video/audio processing. [Download here](https://ffmpeg.org/download.html).
*   **Edge-TTS (Python):** Used for the neural voiceovers.
    *   Install via terminal: `pip install edge-tts`
*   **.NET 8 SDK:** Required to run the application. [Download here](https://dotnet.microsoft.com/en-us/download/dotnet/8.0).

### 2. API Keys
You will need two free (or paid) API keys:
1.  **Gemini API Key:** From [Google AI Studio](https://aistudio.google.com/).
2.  **Pexels API Key:** From [Pexels Developers](https://www.pexels.com/api/).

---

## ⚙️ Configuration

When you first launch the app, click the **Settings ⚙️** icon at the top right.

*   **API Keys:** Enter your Gemini and Pexels keys.
*   **Paths:** The app attempts to auto-detect FFmpeg and Edge-TTS. If it fails, manually point to:
    *   `ffmpeg.exe` and `ffprobe.exe`
    *   `edge-tts.exe` (usually in your Python scripts folder, e.g., `C:\Python312\Scripts\edge-tts.exe`).
*   **Voice:** Select your preferred neural voice (e.g., `he-IL-HilaNeural` for Hebrew).

---

## 🚀 How to Use

### Basic Workflow
1.  Enter a **Topic**.
2.  (Optional) Select **Local Clips** if you want to use your own background footage instead of Pexels.
3.  Click **Generate Video**.

### Advanced Workflow (The "Pro" Way)
1.  Toggle **Advanced Mode (Stages)**.
2.  Add stages (e.g., Intro, Main Point 1, Outro).
3.  For each stage:
    *   Write a goal (e.g., "The health benefits of green tea").
    *   Attach a local video of tea pouring OR type "green tea" for Pexels.
4.  Click **Generate**.

---

## 🏗️ Building from Source

```bash
# Clone the repo
git clone https://github.com/youruser/yShorts.git
cd yShorts

# Build the project
dotnet build

# Run
dotnet run
```

---

## 🌍 Language Support
Currently optimized for **Hebrew** and **English**. 
*   Includes **Open Sans Hebrew** font integration for clean RTL subtitles.
*   Smart AI prompting ensures Hebrew scripts are grammatically correct and engaging.

---

## 📄 License
Educational use only. Be mindful of Pexels and Gemini Terms of Service.
