# yShorts — Vertical Video Generator (AI Powered)

yShorts is a powerful C# WPF application that generates ready-to-batch vertical videos (TikTok/Reels/Shorts style) starting from just a topic. It uses Gemini AI for scripting, Pexels for stock footage, Edge-TTS for high-quality voiceovers, and FFmpeg for the heavy lifting.

## 🚀 Features
- **AI Scripting**: Generates engaging narrations using Google Gemini.
- **Auto Stock Footage**: Downloads relevant portrait videos from Pexels API.
- **Manual Clips**: Want your own footage? Drag-and-drop or select local files with instant thumbnail previews.
- **Viral Subtitles**: Clean, high-readability subtitles automatically burned into the video.
- **SEO Ready**: Automatically generates optimized YouTube/TikTok titles, descriptions, and hashtags.
- **Hebrew Support**: Full native support for Hebrew (fonts, RTL, TTS).

## 🛠 Prerequisites
The app requires the following tools to be installed and available in your PATH (or configured in `appsettings.json`):
1. **FFmpeg & FFprobe**: For video processing.
2. **Edge-TTS**: (Python package `edge-tts`) for voice generation.
3. **.NET 8 SDK**: To build and run the application.

## ⚙️ Setup
1. Clone the repository.
2. Get your **Gemini AI API Key** from [Google AI Studio](https://aistudio.google.com/).
3. Get your **Pexels API Key** from [Pexels Developer Portal](https://www.pexels.com/api/).
4. Launch the app and click the **Settings ⚙️** icon to enter your keys.

## 💻 Running the Project
```bash
dotnet build
dotnet run
```

## 📄 License
This project is for educational/personal use. Please ensure you comply with the terms of service for Gemini and Pexels when generating content.
