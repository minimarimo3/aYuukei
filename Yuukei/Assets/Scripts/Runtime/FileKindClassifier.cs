using System;
using System.Collections.Generic;
using System.IO;

namespace Yuukei.Runtime
{
    public static class FileKindClassifier
    {
        private static readonly Dictionary<string, string> Map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["png"] = "image",
            ["jpg"] = "image",
            ["jpeg"] = "image",
            ["gif"] = "image",
            ["bmp"] = "image",
            ["webp"] = "image",
            ["tif"] = "image",
            ["tiff"] = "image",
            ["mp3"] = "audio",
            ["wav"] = "audio",
            ["ogg"] = "audio",
            ["flac"] = "audio",
            ["m4a"] = "audio",
            ["aac"] = "audio",
            ["mp4"] = "video",
            ["mov"] = "video",
            ["avi"] = "video",
            ["mkv"] = "video",
            ["webm"] = "video",
            ["wmv"] = "video",
            ["txt"] = "text",
            ["md"] = "text",
            ["json"] = "text",
            ["yaml"] = "text",
            ["yml"] = "text",
            ["xml"] = "text",
            ["csv"] = "text",
            ["log"] = "text",
            ["pdf"] = "document",
            ["doc"] = "document",
            ["docx"] = "document",
            ["xls"] = "document",
            ["xlsx"] = "document",
            ["ppt"] = "document",
            ["pptx"] = "document",
            ["zip"] = "archive",
            ["7z"] = "archive",
            ["rar"] = "archive",
            ["tar"] = "archive",
            ["gz"] = "archive",
            ["vrm"] = "model",
            ["glb"] = "model",
            ["gltf"] = "model",
            ["fbx"] = "model",
            ["obj"] = "model",
        };

        public static string Classify(string path, bool isDirectory)
        {
            if (isDirectory)
            {
                return "folder";
            }

            var extension = Path.GetExtension(path)?.TrimStart('.');
            if (string.IsNullOrWhiteSpace(extension))
            {
                return "other";
            }

            return Map.TryGetValue(extension, out var kind) ? kind : "other";
        }
    }
}
