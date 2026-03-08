using System;

namespace Yuukei.Runtime
{
    public readonly struct FileDropEventData
    {
        public FileDropEventData(string path, string fileName, string extension, string kind)
        {
            Path = path;
            FileName = fileName;
            Extension = extension;
            Kind = kind;
        }

        public string Path { get; }
        public string FileName { get; }
        public string Extension { get; }
        public string Kind { get; }
    }

    public readonly struct PeriodicTickEventData
    {
        public PeriodicTickEventData(DateTimeOffset timestamp, float sessionElapsedSeconds)
        {
            Timestamp = timestamp;
            SessionElapsedSeconds = sessionElapsedSeconds;
        }

        public DateTimeOffset Timestamp { get; }
        public float SessionElapsedSeconds { get; }
    }
}
