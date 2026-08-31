using System.IO;

namespace Insider.Cli;

internal static class PortableExecutableInspector
{
    public static string GetArchitecture(string executablePath)
    {
        try
        {
            using var stream = File.OpenRead(executablePath);
            using var reader = new BinaryReader(stream);

            if (reader.ReadUInt16() != 0x5A4D)
            {
                return "Unknown";
            }

            stream.Position = 0x3C;
            var peOffset = reader.ReadInt32();
            if (peOffset < 0 || peOffset > stream.Length - 6)
            {
                return "Unknown";
            }

            stream.Position = peOffset;
            if (reader.ReadUInt32() != 0x00004550)
            {
                return "Unknown";
            }

            return reader.ReadUInt16() switch
            {
                0x014C => "x86",
                0x8664 => "x64",
                0xAA64 => "arm64",
                _ => "Unknown",
            };
        }
        catch (IOException)
        {
            return "Unknown";
        }
    }
}
